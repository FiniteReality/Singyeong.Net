using System;
using System.Buffers;
using System.Diagnostics;
using System.IO.Pipelines;
using System.Linq;
using System.Net.WebSockets;
using System.Text.Json;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Singyeong.Internal;
using Singyeong.Protocol;

namespace Singyeong
{
    public sealed partial class SingyeongClient
    {
        private const int ConnectionTimeoutMillis = 30_0000;
        private const int IdentifyTimeoutMillis = 60_0000;

        private async Task Timeout(
            Func<CancellationToken, Task> taskToRun,
            int timeoutMillis, CancellationToken cancellationToken)
        {
            using var timeoutToken = new CancellationTokenSource(
                timeoutMillis);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(
                timeoutToken.Token, cancellationToken);

            await taskToRun(linked.Token);
        }

        private async Task<TResult> Timeout<TResult>(
            Func<CancellationToken, Task<TResult>> taskToRun,
            int timeoutMillis, CancellationToken cancellationToken)
        {
            using var timeoutToken = new CancellationTokenSource(
                timeoutMillis);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(
                timeoutToken.Token, cancellationToken);

            return await taskToRun(linked.Token);
        }

        private async Task RunInternalAsync(Uri endpoint, string? authToken,
            CancellationToken cancellationToken = default)
        {
            using var connectionToken = new CancellationTokenSource();
            using var sessionToken = CancellationTokenSource
                .CreateLinkedTokenSource(connectionToken.Token,
                    cancellationToken);

            var receivePipe = new Pipe();

            using var client = new ClientWebSocket();
            _configureWebSocket?.Invoke(client.Options);
            _clientId = Guid.NewGuid().ToString();

            await client.ConnectAsync(endpoint, sessionToken.Token);
            var receiveTask = ReceiveAsync(client, receivePipe.Writer,
                sessionToken.Token);
            var sendTask = SendAsync(client, _sendQueue.Reader,
                sessionToken.Token);

            try
            {
                var hello = await Timeout(
                    (ct) => ReceivePayloadAsync<SingyeongHello>(
                        SingyeongOpcode.Hello, receivePipe.Reader,
                        _serializerOptions, ct),
                    ConnectionTimeoutMillis, sessionToken.Token);

                await WriteToQueueAsync(_sendQueue.Writer,
                    new SingyeongPayload
                    {
                        Opcode = SingyeongOpcode.Identify,
                        Payload = new SingyeongIdentify
                        {
                            ClientId = _clientId,
                            ApplicationId = _applicationId,
                            Authentication = authToken,
                            Tags = _applicationTags
                        }
                    }, cancellationToken);

                var ready = await Timeout(
                    (ct) => ReceivePayloadAsync<SingyeongReady>(
                        SingyeongOpcode.Ready, receivePipe.Reader,
                        _serializerOptions, ct),
                    IdentifyTimeoutMillis, sessionToken.Token);

                if (!_metadata.IsEmpty)
                    _ = await WriteToQueueAsync(_sendQueue.Writer,
                        new SingyeongDispatch
                        {
                            DispatchType = "UPDATE_METADATA",
                            Payload = _metadata.ToDictionary(
                                x => x.Key,
                                x => x.Value)
                        }, cancellationToken);


                // Wait for an exception to occur
                await Task.WhenAll(
                    ProcessAsync(receivePipe.Reader, sessionToken.Token),
                    HeartbeatAsync(_sendQueue.Writer,
                        hello.HeartbeatIntervalMs, sessionToken.Token),
                    sendTask,
                    receiveTask
                );
            }
            finally
            {
                if (client.State != WebSocketState.Closed &&
                    client.State != WebSocketState.Aborted)
                    await client.CloseAsync(WebSocketCloseStatus.NormalClosure,
                        null, default);
                sessionToken.Cancel();

                await receiveTask;
            }

            static async Task<T> ReceivePayloadAsync<T>(
                SingyeongOpcode expectedOpcode, PipeReader reader,
                JsonSerializerOptions serializerOptions,
                CancellationToken cancellationToken)
            {
                try
                {
                    ReadResult readResult = default;
                    while (!readResult.IsCompleted)
                    {
                        readResult = await reader.ReadAsync(cancellationToken);
                        var buffer = readResult.Buffer;

                        var status = TryReadPayload(buffer,
                            out var payload, out var opcode,
                            out var dispatchType, out var timestamp,
                            out var endOfPayload);

                        if (status == OperationStatus.NeedMoreData)
                        {
                            // e.g. {"op
                            reader.AdvanceTo(buffer.Start, buffer.End);
                            continue;
                        }
                        else if (status == OperationStatus.InvalidData ||
                            opcode != expectedOpcode)
                            // e.g. invalid json
                            // e.g. {"op": notExpectedOpcode, ...}
                            throw new InvalidOperationException(
                                $"Received payload was not {expectedOpcode}");
                        else
                        {
                            // read valid payload
                            reader.AdvanceTo(endOfPayload);
                            return DeserializeSequence<T>(payload,
                                serializerOptions);
                        }
                    }

                    // If we get to this point, then the connection was closed
                    // without anything being transmitted.
                    throw new InvalidOperationException(
                        "Did not receive any payload");
                }
                catch
                {
                    await reader.CompleteAsync();
                    throw;
                }
            }
        }

        private static async Task ReceiveAsync(ClientWebSocket client,
                PipeWriter writer, CancellationToken cancellationToken)
        {
            try
            {
                FlushResult flushResult = default;
                while (!flushResult.IsCompleted)
                {
                    var buffer = writer.GetMemory();
                    var receiveResult = await client.ReceiveAsync(buffer,
                        cancellationToken);

                    writer.Advance(receiveResult.Count);
                    flushResult = await writer.FlushAsync(
                        cancellationToken);

                    if (receiveResult.MessageType ==
                        WebSocketMessageType.Close)
                        break;
                }
            }
            finally
            {
                await writer.CompleteAsync();
            }
        }

        private async Task SendAsync(ClientWebSocket client,
            ChannelReader<SingyeongPayload> payloadReader,
            CancellationToken cancellationToken)
        {
            while (await payloadReader.WaitToReadAsync(cancellationToken))
            {
                while (payloadReader.TryRead(out var payload))
                {
                    byte[] buffer;

                    if (payload is SingyeongDispatch dispatch)
                        buffer = JsonSerializer.SerializeToUtf8Bytes(dispatch,
                            _serializerOptions);
                    else
                        buffer = JsonSerializer.SerializeToUtf8Bytes(payload,
                            _serializerOptions);

                    await client.SendAsync(new ArraySegment<byte>(buffer),
                        WebSocketMessageType.Text, true, cancellationToken);
                }
            }
        }

        private async Task ProcessAsync(PipeReader reader,
            CancellationToken cancellationToken)
        {
            try
            {
                ReadResult readResult = default;
                while (!readResult.IsCompleted ||
                    !readResult.Buffer.IsEmpty)
                {
                    readResult = await reader.ReadAsync(cancellationToken);
                    var buffer = readResult.Buffer;

                    var status = TryReadPayload(buffer,
                        out var payload, out var opcode,
                        out var dispatchType, out var timestamp,
                        out var endOfPayload);

                    if (status == OperationStatus.NeedMoreData)
                    {
                        // e.g. {"op
                        reader.AdvanceTo(buffer.Start, buffer.End);
                    }
                    else if (status == OperationStatus.InvalidData)
                        // e.g. invalid json
                        // e.g. {"op": notExpectedOpcode, ...}
                        throw new InvalidOperationException(
                            "Received invalid data");
                    else
                    {
                        // read valid payload
                        if (!await HandlePayloadAsync(payload,
                            opcode!.Value, dispatchType,
                            cancellationToken))
                            throw new InvalidOperationException(
                                "Failed to process opcode: " +
                                $"{opcode.Value}");

                        reader.AdvanceTo(endOfPayload);
                    }
                }
            }
            finally
            {
                await reader.CompleteAsync();
            }
        }

        private async Task HeartbeatAsync(
            ChannelWriter<SingyeongPayload> payloadWriter,
            int heartbeatIntervalMs, CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                await Task.Delay(heartbeatIntervalMs, cancellationToken);

                _lastHeartbeat += 1;

                if ((_lastHeartbeat - _lastHeartbeatAck) > 1)
                    throw new MissedHeartbeatException();

                Debug.Assert(_clientId != null);
                var success = await WriteToQueueAsync(payloadWriter,
                    new SingyeongPayload
                    {
                        Opcode = SingyeongOpcode.Heartbeat,
                        Payload = new SingyeongHeartbeat
                        {
                            ClientId = _clientId
                        }
                    }, cancellationToken);

                if (!success)
                    return;
            }
        }
    }
}
