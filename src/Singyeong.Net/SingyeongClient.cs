using Singyeong.Protocol;
using Singyeong.Internal;
using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Net.WebSockets;
using System.Text.Json;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Singyeong.Converters;
using System.Collections.Concurrent;
using System.Linq;
using Polly;

namespace Singyeong
{
    /// <summary>
    /// A client capable of connecting to server instances of Singyeong.
    /// </summary>
    public sealed partial class SingyeongClient
    {
        private readonly string _applicationId;
        private readonly string[] _applicationTags;
        private readonly Action<ClientWebSocketOptions>? _configureWebSocket;

        private readonly ConcurrentDictionary<string, SingyeongMetadata>
            _metadata;
        private readonly IAsyncPolicy? _reconnectPolicy;
        private readonly Channel<JsonDocument> _receiveQueue;
        private readonly Channel<SingyeongPayload> _sendQueue;
        private readonly JsonSerializerOptions _serializerOptions;

        private string? _clientId;
        private long _lastHeartbeat;
        private long _lastHeartbeatAck;
        private int _state = 0;


        /// <summary>
        /// Gets a <see cref="ChannelReader{T}"/> used to read dispatches from
        /// the Singyeong server.
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

        internal SingyeongClient(IAsyncPolicy? reconnectPolicy,
            string applicationId, string[] applicationTags,
            ChannelOptions? sendOptions, ChannelOptions? receiveOptions,
            JsonSerializerOptions? serializerOptions,
            Action<ClientWebSocketOptions>? configureWebSocket,
            IDictionary<string, SingyeongMetadata> _initialMetadata)
        {
            _applicationId = applicationId;
            _applicationTags = applicationTags;
            _configureWebSocket = configureWebSocket;
            _metadata = new ConcurrentDictionary<string, SingyeongMetadata>(
                _initialMetadata);
            _reconnectPolicy = reconnectPolicy;
            _serializerOptions = GetSerializerOptions(serializerOptions);

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
        /// Connects to Singyeong and processes events until an exception
        /// occurs or the client disconnects.
        /// </summary>
        /// <param name="endpoint">
        /// The endpoint to connect to.
        /// </param>
        /// <param name="authToken">
        /// The authentication token to use when connecting to
        /// <paramref name="endpoint"/>.
        /// </param>
        /// <param name="cancellationToken">
        /// A <see cref="CancellationToken"/> used to stop the client.
        /// </param>
        /// <returns>
        /// A <see cref="Task"/> which completes when the client is stopped.
        /// </returns>
        public async Task RunAsync(Uri endpoint, string? authToken,
            CancellationToken cancellationToken = default)
        {
            if (Interlocked.Exchange(ref _state, 1) == 1)
                throw new InvalidOperationException(
                    "Cannot start an already running client");

            if (_reconnectPolicy != null)
                await _reconnectPolicy.ExecuteAsync(
                    (ct) => RunInternalAsync(endpoint, authToken, ct),
                    cancellationToken);
            else
                await RunInternalAsync(endpoint, authToken, cancellationToken);
            _state = 0;
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
        /// <param name="optional">
        /// Whether the routing query is optional or not.
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
            bool allowRestricted = false, bool optional = false,
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
                        AllowOptional = optional,
                        Query = query
                    },
                    Payload = item
                }
            }, cancellationToken);
        }

        /// <summary>
        /// Enqueues an item to be sent to a single client
        /// </summary>
        /// <param name="tags">
        /// The application tags to perform service discovery with.
        /// </param>
        /// <param name="item">
        /// The item to send.
        /// </param>
        /// <param name="allowRestricted">
        /// Whether to allow restricted clients to be chosen when querying.
        /// </param>
        /// <param name="optional">
        /// Whether the routing query is optional or not.
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
        public ValueTask SendToAsync(string[] tags, object item,
            bool allowRestricted = false, bool optional = false,
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
                        ApplicationTags = tags,
                        AllowRestricted = allowRestricted,
                        ConsistentHashKey = Guid.NewGuid().ToString(),
                        AllowOptional = optional,
                        Query = query
                    },
                    Payload = item,
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
        /// <param name="optional">
        /// Whether the routing query is optional or not.
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
            bool allowRestricted = false, bool optional = false,
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
                        AllowOptional = optional,
                        Query = query
                    },
                    Payload = item
                }
            }, cancellationToken);
        }

        /// <summary>
        /// Enqueues an item to be sent to multiple clients.
        /// </summary>
        /// <param name="tags">
        /// The application tags to perform service discovery with.
        /// </param>
        /// <param name="item">
        /// The item to send.
        /// </param>
        /// <param name="allowRestricted">
        /// Whether to allow restricted clients to be chosen when querying.
        /// </param>
        /// <param name="optional">
        /// Whether the routing query is optional or not.
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
        public ValueTask BroadcastToAsync(string[] tags, object item,
            bool allowRestricted = false, bool optional = false,
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
                        ApplicationTags = tags,
                        AllowRestricted = allowRestricted,
                        AllowOptional = optional,
                        Query = query
                    },
                    Payload = item,
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

        private static ValueTask<bool> WriteToQueueAsync<T>(ChannelWriter<T> writer,
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
