using System;
using System.Buffers;
using System.Diagnostics;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Singyeong.Protocol;

namespace Singyeong
{
    public sealed partial class SingyeongClient
    {
        private async Task<bool> HandlePayloadAsync(
            ReadOnlySequence<byte> payload, SingyeongOpcode opcode,
            SingyeongDispatchType? dispatchType,
            CancellationToken cancellationToken)
        {
            switch (opcode)
            {
                case SingyeongOpcode.Invalid:
                    var error = DeserializeSequence<SingyeongInvalid>(payload,
                        _serializerOptions);
                    throw new UnknownErrorException(error.Error);
                case SingyeongOpcode.Dispatch:
                    switch (dispatchType)
                    {
                        case SingyeongDispatchType.Send:
                            return await HandleSendAsync(payload,
                                cancellationToken);
                        case SingyeongDispatchType.Broadcast:
                            return await HandleBroadcastAsync(payload,
                                cancellationToken);
                        default:
                            throw new UnhandledOpcodeException(
                                SingyeongOpcode.Dispatch);
                    }
                case SingyeongOpcode.HeartbeatAck:
                    var heartbeat = DeserializeSequence<SingyeongHeartbeat>(
                        payload, _serializerOptions);
                    return HandleHeartbeatAck(heartbeat);
                case SingyeongOpcode.Goodbye:
                    throw new GoodbyeException();
                default:
                    throw new UnhandledOpcodeException(opcode);
            }
        }

        private bool HandleHeartbeatAck(SingyeongHeartbeat heartbeat)
        {
            Debug.Assert(_clientId != null);
            if (heartbeat.ClientId != _clientId)
                return false;

            _lastHeartbeatAck += 1;

            return true;
        }

        private ValueTask<bool> HandleBroadcastAsync(
            ReadOnlySequence<byte> payload,
            CancellationToken cancellationToken)
        {
            var reader = new Utf8JsonReader(payload);
            var document = JsonDocument.ParseValue(ref reader);

            return WriteToQueueAsync(_receiveQueue.Writer, document,
                cancellationToken);
        }

        private ValueTask<bool> HandleSendAsync(ReadOnlySequence<byte> payload,
            CancellationToken cancellationToken)
        {
            var reader = new Utf8JsonReader(payload);
            var document = JsonDocument.ParseValue(ref reader);

            return WriteToQueueAsync(_receiveQueue.Writer, document,
                cancellationToken);
        }
    }
}
