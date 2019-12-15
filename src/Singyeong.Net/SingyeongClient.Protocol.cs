using System;
using System.Buffers;
using System.Text.Json;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Singyeong.Protocol;

namespace Singyeong
{
    public sealed partial class SingyeongClient
    {
        private static ValueTask<bool> TryProcessPayloadAsync(
            SingyeongClient client, ChannelWriter<SingyeongPayload> sendQueue,
            ref ReadOnlySequence<byte> buffer,
            JsonSerializerOptions serializerOptions,
            CancellationToken cancellationToken)
        {
            if (buffer.IsEmpty)
                return new ValueTask<bool>(false);

            Utf8JsonReader reader = new Utf8JsonReader(buffer);

            if (!TryPositionData(buffer, ref reader, out var opcode,
                out var dispatchType, out var timestamp, out var endOfPayload))
                return new ValueTask<bool>(false);

            buffer = buffer.Slice(endOfPayload);

            switch (opcode)
            {
                case SingyeongOpcode.Hello:
                    var hello = JsonSerializer.Deserialize<SingyeongHello>(
                        ref reader, serializerOptions);
                    return client.HandleHelloAsync(hello, sendQueue,
                        cancellationToken);
                case SingyeongOpcode.Ready:
                    var ready = JsonSerializer.Deserialize<SingyeongReady>(
                        ref reader, serializerOptions);
                    return client.HandleReadyAsync(ready, sendQueue,
                        cancellationToken);
                case SingyeongOpcode.Invalid:
                    var error = JsonSerializer
                        .Deserialize<SingyeongInvalid>(ref reader,
                            serializerOptions);
                    throw new UnknownErrorException(error.Error);
                case SingyeongOpcode.Dispatch:
                    return TryHandleDispatchAsync(client,
                        dispatchType!.Value, ref reader, serializerOptions,
                        sendQueue, cancellationToken);
                case SingyeongOpcode.HeartbeatAck:
                    var heartbeat = JsonSerializer
                        .Deserialize<SingyeongHeartbeat>(ref reader,
                            serializerOptions);
                    return client.HandleHeartbeatAckAsync(heartbeat,
                        cancellationToken);
                case SingyeongOpcode.Goodbye:
                    throw new GoodbyeException();
                default:
                    throw new UnhandledOpcodeException(opcode);
            }
        }

        static ValueTask<bool> TryHandleDispatchAsync(
            SingyeongClient client, SingyeongDispatchType dispatchType,
            ref Utf8JsonReader reader,
            JsonSerializerOptions serializerOptions,
            ChannelWriter<SingyeongPayload> sendQueue,
            CancellationToken cancellationToken)
        {
            return dispatchType switch
            {
                SingyeongDispatchType.Send =>
                    client.HandleSendAsync(ref reader, serializerOptions,
                        cancellationToken),
                SingyeongDispatchType.Broadcast =>
                    client.HandleBroadcastAsync(ref reader,
                        serializerOptions, cancellationToken),
                _ => throw new UnhandledOpcodeException(
                    SingyeongOpcode.Dispatch),
            };
        }

        // This is necessary to read the top level json structure as it
        // contains polymorphic data.
        static bool TryPositionData(ReadOnlySequence<byte> buffer,
            ref Utf8JsonReader reader, out SingyeongOpcode opcode,
            out SingyeongDispatchType? dispatchType, out long timestamp,
            out SequencePosition finalPosition)
        {
            opcode = default;
            dispatchType = default;
            timestamp = default;
            finalPosition = default;

            SingyeongOpcode? savedOpcode = null;
            SingyeongDispatchType? savedDispatch = null;
            long? savedTimestamp = null;
            JsonReaderState? savedState = null;

            while (reader.Read())
            {
                switch (reader.TokenType)
                {
                    case JsonTokenType.PropertyName
                        when reader.ValueTextEquals(
                            ProtocolConstants.OpcodePropertyName)
                            && reader.CurrentDepth == 1:
                    {
                        if (!reader.Read() || reader.TokenType
                            != JsonTokenType.Number)
                            return false;

                        if (!reader.TryGetInt32(out var opcodeValue))
                            return false;

                        savedOpcode = (SingyeongOpcode)opcodeValue;
                        break;
                    }

                    case JsonTokenType.PropertyName
                        when reader.ValueTextEquals(
                            ProtocolConstants.DataPropertyName)
                            && reader.CurrentDepth == 1:
                    {
                        savedState = reader.CurrentState;
                        buffer = buffer.Slice(reader.Position);
                        if (!reader.TrySkip())
                            return false;
                        buffer = buffer.Slice(0, reader.Position);
                        break;
                    }

                    case JsonTokenType.PropertyName
                        when reader.ValueTextEquals(
                            ProtocolConstants.TimestampPropertyName)
                            && reader.CurrentDepth == 1:
                    {
                        if (!reader.Read() || reader.TokenType
                            != JsonTokenType.Number)
                            return false;

                        if (!reader.TryGetInt64(out var timestampValue))
                            return false;

                        savedTimestamp = timestampValue;
                        break;
                    }

                    case JsonTokenType.PropertyName
                        when reader.ValueTextEquals(
                            ProtocolConstants.EventTypePropertyName)
                            && reader.CurrentDepth == 1:
                    {
                        if (!reader.Read())
                            return false;

                        if (reader.TokenType == JsonTokenType.String)
                        {
                            if (reader.ValueTextEquals(
                                ProtocolConstants.SendEventType))
                                savedDispatch = SingyeongDispatchType.Send;
                            else if (reader.ValueTextEquals(
                                ProtocolConstants.BroadcastEventType))
                                savedDispatch =
                                    SingyeongDispatchType.Broadcast;
                        }
                        // TODO: remove this when/if this gets fixed:
                        // https://github.com/queer/singyeong/issues/73
                        else
                        {
                            savedDispatch = SingyeongDispatchType.Send;
                        }

                        // Other dispatch types will remain unhandled.
                        break;
                    }
                }

                if (savedOpcode != null
                    && savedTimestamp != null
                    && savedState != null)
                {
                    if (savedOpcode == SingyeongOpcode.Dispatch)
                    {
                        if (savedDispatch == null)
                            continue;

                        dispatchType = savedDispatch;
                    }

                    while (reader.CurrentDepth > 0)
                        if (!reader.Read())
                            return false;

                    finalPosition = reader.Position;
                    reader = new Utf8JsonReader(buffer, true,
                        savedState.Value);
                    opcode = savedOpcode.Value;
                    timestamp = savedTimestamp.Value;
                    return true;
                }
            }

            return false;
        }
    }
}
