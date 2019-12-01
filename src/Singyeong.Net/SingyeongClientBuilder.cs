using System;
using System.Collections.Generic;
using System.Net.WebSockets;
using System.Threading.Channels;
using Singyeong.Internal;
using Singyeong.Protocol;

namespace Singyeong
{
    /// <summary>
    /// A builder for instances of <see cref="SingyeongClient"/>.
    /// </summary>
    public sealed class SingyeongClientBuilder
    {
        private readonly List<(Uri endpoint, string authToken)> _endpoints;
        private readonly Dictionary<string, SingyeongMetadata> _metadata;
        private readonly string _applicationId;

        private ChannelOptions? _sendChannelOptions;
        private ChannelOptions? _receiveChannelOptions;
        private Action<ClientWebSocketOptions>? _configureClientWebSocket;

        /// <summary>
        /// Initializes a new instance of <see cref="SingyeongClientBuilder"/>.
        /// </summary>
        /// <param name="applicationId">
        /// The appplication id for this client.
        /// </param>
        public SingyeongClientBuilder(string applicationId)
        {
            _endpoints = new List<(Uri, string)>();
            _metadata = new Dictionary<string, SingyeongMetadata>();
            _applicationId = applicationId;
        }

        /// <summary>
        /// Specifies send channel options for the constructed
        /// <see cref="SingyeongClient"/>.
        /// </summary>
        /// <param name="options">
        /// The options to use.
        /// </param>
        /// <returns>
        /// A reference to this instance after the operation has completed.
        /// </returns>
        public SingyeongClientBuilder WithSendChannelOptions(
            ChannelOptions options)
        {
            options.AllowSynchronousContinuations = false;
            options.SingleReader = true;

            _sendChannelOptions = options;

            return this;
        }

        /// <summary>
        /// Specifies receive channel options for the constructed
        /// <see cref="SingyeongClient"/>.
        /// </summary>
        /// <param name="options">
        /// The options to use.
        /// </param>
        /// <returns>
        /// A reference to this instance after the operation has completed.
        /// </returns>
        public SingyeongClientBuilder WithReceiveChannelOptions(
            ChannelOptions options)
        {
            options.AllowSynchronousContinuations = false;
            options.SingleReader = true;
            options.SingleWriter = true;

            _receiveChannelOptions = options;

            return this;
        }

        /// <summary>
        /// Configures websocket options for the created
        /// <see cref="SingyeongClient"/>.
        /// </summary>
        /// <param name="configureWebSocket">
        /// The callback to execute to configure websocket options.
        /// </param>
        /// <returns>
        /// A reference to this instance after the operation has completed.
        /// </returns>
        public SingyeongClientBuilder WithWebSocketOptions(
            Action<ClientWebSocketOptions> configureWebSocket)
        {
            _configureClientWebSocket = configureWebSocket;

            return this;
        }

        /// <summary>
        /// Adds a connection endpoint to the singyeong client.
        /// </summary>
        /// <param name="endpoint">
        /// The endpoint to add.
        /// </param>
        /// <param name="authToken">
        /// The authentication token to use for this endpoint.
        /// </param>
        /// <returns>
        /// A reference to this instance after the operation has completed.
        /// </returns>
        public SingyeongClientBuilder AddEndpoint(string endpoint,
            string authToken)
        {
            _endpoints.Add((new Uri(endpoint), authToken));

            return this;
        }

        /// <summary>
        /// Adds a connection endpoint to the singyeong client.
        /// </summary>
        /// <param name="endpoint">
        /// The endpoint to add.
        /// </param>
        /// <param name="authToken">
        /// The authentication token to use for this endpoint.
        /// </param>
        /// <returns>
        /// A reference to this instance after the operation has completed.
        /// </returns>
        public SingyeongClientBuilder AddEndpoint(Uri endpoint,
            string authToken)
        {
            _endpoints.Add((endpoint, authToken));

            return this;
        }

        /// <summary>
        /// Adds an initial metadata value to the singyeong client.
        /// </summary>
        /// <param name="key">
        /// The metadata key to add.
        /// </param>
        /// <param name="value">
        /// The metadata value to add.
        /// </param>
        /// <typeparam name="T">
        /// The type of value to specify.
        /// </typeparam>
        /// <returns>
        /// A reference to this instance after the operation has completed.
        /// </returns>
        public SingyeongClientBuilder AddMetadata<T>(string key, T value)
        {
            if (!TypeUtility.IsSupported<T>())
                throw new ArgumentException(
                    $"{typeof(T).Name} is not a supported metadata type.",
                    nameof(value));

            if (!_metadata.TryAdd(key, new SingyeongMetadata
            {
                Type = TypeUtility.GetTypeName(value),
                Value = value
            }))
                throw new ArgumentException(
                    $"Metadata '{key}' was already added to the client",
                    nameof(key));

            return this;
        }

        /// <summary>
        /// Builds the singyeong client with the given options.
        /// </summary>
        /// <returns>
        /// A <see cref="SingyeongClient"/> which can be used to connect to a
        /// Singyeong server.
        /// </returns>
        public SingyeongClient Build()
        {
            return new SingyeongClient(_endpoints, _applicationId,
                _sendChannelOptions, _receiveChannelOptions,
                _configureClientWebSocket, _metadata);
        }
    }
}