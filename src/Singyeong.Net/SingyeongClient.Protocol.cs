using System;
using System.Buffers;
using System.Diagnostics;
using System.Text.Json;
using Singyeong.Protocol;

namespace Singyeong
{
    public sealed partial class SingyeongClient
    {
        private static OperationStatus TryReadPayload(
            ReadOnlySequence<byte> sequence, ref JsonReaderState state,
            out ReadOnlySequence<byte> payload,
            out SingyeongOpcode? opcode,
            out SingyeongDispatchType? dispatchType,
            out long? timestamp,
            out SequencePosition endOfPayload)
        {
            payload = default;
            opcode = default;
            dispatchType = default;
            timestamp = default;
            endOfPayload = default;

            var reader = new Utf8JsonReader(sequence, false, state);

            while (reader.Read())
            {
                switch (reader.TokenType)
                {
                    case JsonTokenType.PropertyName
                        when reader.ValueTextEquals(
                            ProtocolConstants.OpcodePropertyName)
                            && reader.CurrentDepth == 1:
                    {
                        if (!reader.Read())
                        {
                            state = reader.CurrentState;
                            return OperationStatus.NeedMoreData;
                        }

                        if (reader.TokenType != JsonTokenType.Number)
                            return OperationStatus.InvalidData;

                        if (!reader.TryGetInt32(out var opcodeValue))
                            return OperationStatus.InvalidData;

                        opcode = (SingyeongOpcode)opcodeValue;
                        break;
                    }

                    case JsonTokenType.PropertyName
                        when reader.ValueTextEquals(
                            ProtocolConstants.DataPropertyName)
                            && reader.CurrentDepth == 1:
                    {
                        var start = sequence.Slice(reader.Position);

                        if (!reader.TrySkip())
                        {
                            state = reader.CurrentState;
                            return OperationStatus.NeedMoreData;
                        }

                        payload = start.Slice(0, reader.Position);
                        break;
                    }

                    case JsonTokenType.PropertyName
                        when reader.ValueTextEquals(
                            ProtocolConstants.TimestampPropertyName)
                            && reader.CurrentDepth == 1:
                    {
                        if (!reader.Read())
                        {
                            state = reader.CurrentState;
                            return OperationStatus.NeedMoreData;
                        }

                        if (reader.TokenType != JsonTokenType.Number)
                            return OperationStatus.InvalidData;

                        if (!reader.TryGetInt64(out var timestampValue))
                            return OperationStatus.InvalidData;

                        timestamp = timestampValue;
                        break;
                    }

                    case JsonTokenType.PropertyName
                        when reader.ValueTextEquals(
                            ProtocolConstants.EventTypePropertyName)
                            && reader.CurrentDepth == 1:
                    {
                        if (!reader.Read())
                        {
                            state = reader.CurrentState;
                            return OperationStatus.NeedMoreData;
                        }

                        if (reader.TokenType == JsonTokenType.String)
                        {
                            if (reader.ValueTextEquals(
                                ProtocolConstants.SendEventType))
                            {
                                dispatchType = SingyeongDispatchType.Send;
                                break;
                            }
                            else if (reader.ValueTextEquals(
                                ProtocolConstants.BroadcastEventType))
                            {
                                dispatchType = SingyeongDispatchType.Broadcast;
                                break;
                            }
                        }

                        // Unhandled dispatch types should fail to parse.
                        return OperationStatus.InvalidData;
                    }
                }

                if (!payload.IsEmpty && opcode.HasValue)
                {
                    if (opcode == SingyeongOpcode.Dispatch &&
                        !dispatchType.HasValue)
                        continue;

                    while (reader.CurrentDepth > 0)
                        if (!reader.Read())
                        {
                            state = reader.CurrentState;
                            return OperationStatus.NeedMoreData;
                        }

                    endOfPayload = reader.Position;
                    return OperationStatus.Done;
                }
            }

            return OperationStatus.NeedMoreData;
        }

        private static T DeserializeSequence<T>(
            ReadOnlySequence<byte> sequence, JsonSerializerOptions options)
        {
            var reader = new Utf8JsonReader(sequence);
            var model = JsonSerializer.Deserialize<T>(ref reader, options);

            Debug.Assert(reader.BytesConsumed == sequence.Length);

            return model;
        }
    }
}
