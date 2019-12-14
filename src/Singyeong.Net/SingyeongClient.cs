using Singyeong.Protocol;
using Singyeong.Internal;
using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO.Pipelines;
using System.Linq.Expressions;
using System.Net.WebSockets;
using System.Text.Json;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Singyeong.Converters;
using System.Collections.Concurrent;
using System.Linq;

namespace Singyeong
{
    /// <summary>
    /// A client capable of connecting to server instances of Singyeong
    /// </summary>
    public sealed class SingyeongClient : IDisposable
    {
        // Exponential backoff multiplication factor
        private const double BackoffFactor = 1.2;

        private readonly JsonSerializerOptions _serializerOptions;

        private readonly IReadOnlyList<(Uri endpoint, string authToken)>
            _endpoints;
        private readonly string _applicationId;
        private readonly ClientWebSocket _client;
        private readonly CancellationTokenSource _disposeCancelToken;
        private readonly ValueTaskCompletionSource<int> _heartbeatPromise;

        private readonly ConcurrentDictionary<string, SingyeongMetadata>
            _metadata;

        private readonly Channel<SingyeongPayload> _sendQueue;
        private readonly Channel<JsonDocument> _receiveQueue;


        private string? _clientId;
        private int _started = 0;

        private bool _isReconnect;
        private string? _currentAuthToken;

        private long _lastHeartbeatAck;
        private long _lastHeartbeat;

        /// <summary>
        /// A <see cref="ChannelReader{T}"/> used to read dispatches from
        /// Singyeong.
        /// </summary>
        public ChannelReader<JsonDocument> DispatchReader
            => _receiveQueue.Reader;

        private static JsonSerializerOptions GetSerializerOptions(
            JsonSerializerOptions? existing = null)
        {
            var options = existing ?? new JsonSerializerOptions();

            options.Converters.Add(new SingyeongTargetConverter());

            return options;
        }

        internal SingyeongClient(IReadOnlyList<(Uri, string)> endpoints,
            string applicationId, ChannelOptions? sendOptions,
            ChannelOptions? receiveOptions,
            JsonSerializerOptions? serializerOptions,
            Action<ClientWebSocketOptions>? configureWebSocket,
            IDictionary<string, SingyeongMetadata> _initialMetadata)
        {
            _endpoints = endpoints;
            _applicationId = applicationId;

            _client = new ClientWebSocket();
            configureWebSocket?.Invoke(_client.Options);

            _serializerOptions = GetSerializerOptions(serializerOptions);

            _disposeCancelToken = new CancellationTokenSource();
            _heartbeatPromise = new ValueTaskCompletionSource<int>();

            _heartbeatPromise.SetResult(0);

            _metadata = new ConcurrentDictionary<string, SingyeongMetadata>(
                _initialMetadata);

            if (sendOptions == null)
                _sendQueue = Channel.CreateBounded<SingyeongPayload>(128);
            else if (sendOptions is BoundedChannelOptions boundedSendOptions)
                _sendQueue = Channel.CreateBounded<SingyeongPayload>(
                    boundedSendOptions);
            else if (sendOptions is UnboundedChannelOptions
                unboundedSendOptions)
                _sendQueue = Channel.CreateUnbounded<SingyeongPayload>(
                    unboundedSendOptions);
            else
                throw new ArgumentException(
                    "Send channel options must be bounded or unbounded.",
                    nameof(sendOptions));

            if (receiveOptions == null)
                _receiveQueue = Channel.CreateUnbounded<JsonDocument>();
            else if (receiveOptions is BoundedChannelOptions
                boundedReceiveOptions)
                _receiveQueue = Channel.CreateBounded<JsonDocument>(
                    boundedReceiveOptions);
            else if (receiveOptions is UnboundedChannelOptions
                unboundedReceiveOptions)
                _receiveQueue = Channel.CreateUnbounded<JsonDocument>(
                    unboundedReceiveOptions);
            else
                throw new ArgumentException(
                    "Receive channel options must be bounded or unbounded.",
                    nameof(sendOptions));
        }

        /// <summary>
        /// Disposes of any resources used by the client.
        /// </summary>
        public void Dispose()
        {
            _disposeCancelToken.Cancel();
            _client.Dispose();
        }

        /// <summary>
        /// Runs the client, until an unrecoverable error occurs.
        /// </summary>
        /// <param name="cancellationToken">
        /// A <see cref="CancellationToken"/> used to stop the client.
        /// </param>
        /// <returns>
        /// A <see cref="Task"/> which completes when the client is stopped.
        /// </returns>
        public async Task RunAsync(
            CancellationToken cancellationToken = default)
        {
            if (Interlocked.Exchange(ref _started, 1) == 1)
                throw new InvalidOperationException(
                    "Cannot start an already running client");

            using var sessionToken = CancellationTokenSource
                .CreateLinkedTokenSource(cancellationToken,
                    _disposeCancelToken.Token);

            var random = new Random();
            var connectionAttempts = 0;
            var endpointInfo = GetEndpoint(random);
            _clientId = Guid.NewGuid().ToString();

            while (true)
            {
                try
                {
                    Uri url;
                    (url, _currentAuthToken) = endpointInfo;
                    _isReconnect = connectionAttempts > 0;

                    await RunInternalAsync(url, sessionToken.Token);
                }
                catch (OperationCanceledException)
                {
                    // No need for an Interlocked here since we're only
                    // allowing a single call to succeed earlier
                    _started = 0;
                    throw;
                }
                catch (Exception e)
                {
                    if (!IsRecoverable(e))
                    {
                        _started = 0;
                        throw;
                    }
                }

                await Task.Delay(
                    (int)Math.Pow(BackoffFactor, connectionAttempts) * 1000,
                    sessionToken.Token);

                if (connectionAttempts++ > 5)
                {
                    _clientId = Guid.NewGuid().ToString();
                    endpointInfo = GetEndpoint(random);
                    connectionAttempts = 0;
                }
            }

            static bool IsRecoverable(Exception e)
            {
                return false;
            }
        }

        /// <summary>
        /// Enqueues an item to be sent to a single client
        /// </summary>
        /// <param name="application">
        /// The application to send the message to.
        /// </param>
        /// <param name="item">
        /// The item to send.
        /// </param>
        /// <param name="allowRestricted">
        /// Whether to allow restricted clients to be chosen when querying.
        /// </param>
        /// <param name="query">
        /// The query to perform.
        /// </param>
        /// <param name="cancellationToken">
        /// A <see cref="CancellationToken"/> used to monitor for cancellation.
        /// </param>
        /// <returns>
        /// A <see cref="ValueTask"/> which completes when the write operation
        /// completes.
        /// </returns>
        public ValueTask SendToAsync(string application, object item,
            bool allowRestricted = false,
            Expression<Func<SingyeongQuery, bool>>? query = default,
            CancellationToken cancellationToken = default)
        {
            return _sendQueue.Writer.WriteAsync(new SingyeongDispatch
            {
                DispatchType = "SEND",
                Payload = new SingyeongSend
                {
                    Target = new SingyeongTarget
                    {
                        ApplicationId = application,
                        AllowRestricted = allowRestricted,
                        ConsistentHashKey = Guid.NewGuid().ToString(),
                        Query = query
                    },
                    Payload = item
                }
            }, cancellationToken);
        }

        /// <summary>
        /// Enqueues an item to be sent to multiple clients.
        /// </summary>
        /// <param name="application">
        /// The application to send the message to.
        /// </param>
        /// <param name="item">
        /// The item to send.
        /// </param>
        /// <param name="allowRestricted">
        /// Whether to allow restricted clients to be chosen when querying.
        /// </param>
        /// <param name="query">
        /// The query to perform.
        /// </param>
        /// <param name="cancellationToken">
        /// A <see cref="CancellationToken"/> used to monitor for cancellation.
        /// </param>
        /// <returns>
        /// A <see cref="ValueTask"/> which completes when the write operation
        /// completes.
        /// </returns>
        public ValueTask BroadcastToAsync(string application, object item,
            bool allowRestricted = false,
            Expression<Func<SingyeongQuery, bool>>? query = default,
            CancellationToken cancellationToken = default)
        {
            return _sendQueue.Writer.WriteAsync(new SingyeongDispatch
            {
                DispatchType = "BROADCAST",
                Payload = new SingyeongBroadcast
                {
                    Target = new SingyeongTarget
                    {
                        ApplicationId = application,
                        AllowRestricted = allowRestricted,
                        ConsistentHashKey = Guid.NewGuid().ToString(),
                        Query = query
                    },
                    Payload = item
                }
            }, cancellationToken);
        }

        /// <summary>
        /// Sets a piece of metadata on the client and enqueues a metadata update
        /// to be sent to the Singyeong server.
        /// </summary>
        /// <param name="key">
        /// The metadata key to set.
        /// </param>
        /// <param name="value">
        /// The metadata value to set.
        /// </param>
        /// <typeparam name="T">
        /// The type of value to specify.
        /// </typeparam>
        /// <returns>
        /// A <see cref="ValueTask"/> which complete when the write operation
        /// completes.
        /// </returns>
        public ValueTask SetMetadataAsync<T>(string key, T value)
        {
            if (!TypeUtility.IsSupported<T>())
                throw new ArgumentException(
                    $"{typeof(T).Name} is not a supported metadata type.",
                    nameof(value));

            var metadata = _metadata.GetOrAdd(key,
                (_) => new SingyeongMetadata
                {
                    Type = TypeUtility.GetTypeName(value),
                    Value = value
                });

            if (!ReferenceEquals(metadata.Value, value))
            {
                metadata.Type = TypeUtility.GetTypeName(value);
                metadata.Value = value;
            }

            return _sendQueue.Writer.WriteAsync(new SingyeongDispatch
            {
                DispatchType = "UPDATE_METADATA",
                Payload = _metadata.ToDictionary(x => x.Key, x => x.Value)
            });
        }

        /// <summary>
        /// Removes a piece of metadata on the client and enqueues a metadata
        /// update to be sent to the Singyeong server.
        /// </summary>
        /// <remarks>
        /// The metadata update is only enqueued if the metadata field was
        /// actually removed.
        /// </remarks>
        /// <param name="key">
        /// The metadata key to remove.
        /// </param>
        /// <returns>
        /// A <see cref="ValueTask"/> which complete when the write operation
        /// completes.
        /// </returns>
        public ValueTask RemoveMetadataAsync(string key)
        {
            if (!_metadata.TryRemove(key, out var _))
                return default;

            return _sendQueue.Writer.WriteAsync(new SingyeongDispatch
            {
                DispatchType = "UPDATE_METADATA",
                Payload = _metadata.ToDictionary(x => x.Key, x => x.Value)
            });
        }

        private (Uri, string) GetEndpoint(Random random)
        {
            return _endpoints[random.Next(_endpoints.Count)];
        }

        private async Task RunInternalAsync(Uri endpoint,
            CancellationToken cancellationToken = default)
        {
            var pipe = new Pipe();
            _heartbeatPromise.Reset();

            await _client.ConnectAsync(endpoint, cancellationToken);

            try
            {
                await Task.WhenAll(
                    ReceiveAsync(_client, pipe.Writer, cancellationToken),
                    WriteAsync(_client, _sendQueue.Reader, _serializerOptions,
                        cancellationToken),
                    ProcessAsync(this, pipe.Reader, _sendQueue.Writer,
                        cancellationToken),
                    HeartbeatAsync(_heartbeatPromise, _sendQueue.Writer,
                        cancellationToken)
                );
            }
            finally
            {
                if (_client.State != WebSocketState.Closed)
                    await _client.CloseAsync(
                        WebSocketCloseStatus.NormalClosure, null, default);
            }

            static async Task ReceiveAsync(WebSocket client, PipeWriter writer,
                CancellationToken cancellationToken)
            {
                FlushResult flushResult = default;
                try
                {
                    while (!flushResult.IsCompleted)
                    {
                        var memory = writer.GetMemory();
                        var receiveResult = await client.ReceiveAsync(memory,
                            cancellationToken);

                        writer.Advance(receiveResult.Count);

                        if (receiveResult.EndOfMessage)
                            flushResult = await writer.FlushAsync(
                                cancellationToken);

                        if (receiveResult.MessageType == WebSocketMessageType.Close)
                            break;
                    }
                }
                finally
                {
                    await writer.CompleteAsync();
                }
            }

            static async Task WriteAsync(WebSocket client,
                ChannelReader<SingyeongPayload> sendQueue,
                JsonSerializerOptions serializerOptions,
                CancellationToken cancellationToken)
            {
                while (true)
                {
                    // TODO: find a safe non-allocating way of doing this
                    var message = await sendQueue.ReadAsync(cancellationToken);

                    byte[] buffer;
                    if (message is SingyeongDispatch dispatch)
                        buffer = JsonSerializer.SerializeToUtf8Bytes(dispatch,
                            options: serializerOptions);
                    else
                        buffer = JsonSerializer.SerializeToUtf8Bytes(message,
                            options: serializerOptions);

                    await client.SendAsync(new ArraySegment<byte>(buffer),
                        WebSocketMessageType.Text, true, cancellationToken);
                }
            }
        }

        private static async Task ProcessAsync(SingyeongClient client,
            PipeReader reader, ChannelWriter<SingyeongPayload> sendQueue,
            CancellationToken cancellationToken)
        {
            ReadResult readResult = default;
            try
            {
                while (!readResult.IsCompleted)
                {
                    readResult = await reader.ReadAsync(cancellationToken);

                    var buffer = readResult.Buffer;

                    while (true)
                    {
                        if (!await TryProcessAsync(client, sendQueue,
                            ref buffer, client._serializerOptions,
                                cancellationToken))
                            break;
                    }

                    reader.AdvanceTo(buffer.Start, buffer.End);
                }
            }
            finally
            {
                await reader.CompleteAsync();
            }

            static ValueTask<bool> TryProcessAsync(SingyeongClient client,
                ChannelWriter<SingyeongPayload> sendQueue,
                ref ReadOnlySequence<byte> buffer,
                JsonSerializerOptions serializerOptions,
                CancellationToken cancellationToken)
            {
                if (buffer.IsEmpty)
                    return new ValueTask<bool>(false);

                Utf8JsonReader reader = new Utf8JsonReader(buffer);

                if (!TryPositionData(buffer, ref reader, out var opcode,
                    out var dispatchType, out var timestamp, out var position))
                    return new ValueTask<bool>(false);

                buffer = buffer.Slice(position);

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
                switch (dispatchType)
                {
                    case SingyeongDispatchType.Send:
                        return client.HandleSendAsync(ref reader,
                            serializerOptions, cancellationToken);
                    case SingyeongDispatchType.Broadcast:
                        return client.HandleBroadcastAsync(ref reader,
                            serializerOptions, cancellationToken);
                    default:
                        throw new UnhandledOpcodeException(
                            SingyeongOpcode.Dispatch);
                }
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

        private async Task HeartbeatAsync(
            ValueTaskCompletionSource<int> heartbeatPromise,
            ChannelWriter<SingyeongPayload> sendQueue,
            CancellationToken cancellationToken)
        {
            var interval = await heartbeatPromise.WaitAsync(
                cancellationToken);

            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await Task.Delay(interval, cancellationToken);

                _lastHeartbeat += 1;

                if ((_lastHeartbeat - _lastHeartbeatAck) > 1)
                    throw new MissedHeartbeatException();

                var ok = await WriteToQueue(sendQueue, new SingyeongPayload
                {
                    Opcode = SingyeongOpcode.Heartbeat,
                    Payload = new SingyeongHeartbeat
                    {
                        ClientId = _clientId!
                    }
                }, cancellationToken);

                if (!ok)
                    break;
            }
        }

        private ValueTask<bool> HandleHelloAsync(SingyeongHello hello,
            ChannelWriter<SingyeongPayload> sendQueue,
            CancellationToken cancellationToken)
        {
            _ = _heartbeatPromise.TrySetResult(hello.HeartbeatIntervalMs);

            return WriteToQueue(sendQueue, new SingyeongPayload
            {
                Opcode = SingyeongOpcode.Identify,
                Payload = new SingyeongIdentify
                {
                    ClientId = _clientId!,
                    ApplicationId = _applicationId,
                    Reconnect = _isReconnect,
                    Authentication = _currentAuthToken
                }
            }, cancellationToken);
        }

        private ValueTask<bool> HandleReadyAsync(SingyeongReady ready,
            ChannelWriter<SingyeongPayload> sendQueue,
            CancellationToken cancellationToken)
        {
            if (ready.ClientId != _clientId)
                return new ValueTask<bool>(false);

            if (!_metadata.IsEmpty)
                return WriteToQueue(sendQueue, new SingyeongDispatch
                {
                    DispatchType = "UPDATE_METADATA",
                    Payload = _metadata.ToDictionary(x => x.Key, x => x.Value)
                }, cancellationToken);

            return new ValueTask<bool>(true);
        }

        private ValueTask<bool> HandleHeartbeatAckAsync(
            SingyeongHeartbeat heartbeat,
            CancellationToken cancellationToken)
        {
            if (heartbeat.ClientId != _clientId)
                return new ValueTask<bool>(false);

            _lastHeartbeatAck += 1;

            return new ValueTask<bool>(true);
        }

        private ValueTask<bool> HandleBroadcastAsync(ref Utf8JsonReader reader,
            JsonSerializerOptions serializerOptions,
            CancellationToken cancellationToken)
        {
            var document = JsonDocument.ParseValue(ref reader);

            return WriteToQueue(_receiveQueue.Writer, document,
                cancellationToken);
        }

        private ValueTask<bool> HandleSendAsync(ref Utf8JsonReader reader,
            JsonSerializerOptions serializerOptions,
            CancellationToken cancellationToken)
        {
            var document = JsonDocument.ParseValue(ref reader);

            return WriteToQueue(_receiveQueue.Writer, document,
                cancellationToken);
        }

        private static ValueTask<bool> WriteToQueue<T>(ChannelWriter<T> writer,
            T value, CancellationToken cancellationToken)
        {
            return writer.TryWrite(value)
                ? new ValueTask<bool>(true)
                : SlowPath(value, writer, cancellationToken);

            static async ValueTask<bool> SlowPath(
                T value, ChannelWriter<T> writer,
                CancellationToken cancellationToken)
            {
                while (await writer.WaitToWriteAsync(cancellationToken))
                    if (writer.TryWrite(value)) return true;

                return false;
            }
        }
    }
}
