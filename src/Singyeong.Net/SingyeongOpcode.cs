namespace Singyeong
{
    /// <summary>
    /// Represents a Singyeong websocket opcode.
    /// </summary>
    public enum SingyeongOpcode
    {
        /// <summary>
        /// Sent by the server on initial connection.
        /// </summary>
        Hello = 0,
        /// <summary>
        /// Sent by the client to identify with the server.
        /// </summary>
        Identify = 1,
        /// <summary>
        /// Sent by the server when the connection has been negotiated.
        /// </summary>
        Ready = 2,
        /// <summary>
        /// Sent by the server when an invalid operation has occured.
        /// </summary>
        Invalid = 3,
        /// <summary>
        /// Sent by both the client and server to dispatch an event.
        /// </summary>
        Dispatch = 4,
        /// <summary>
        /// Sent by the client to keep the connection alive.
        /// </summary>
        Heartbeat = 5,
        /// <summary>
        /// Sent by the server to acknowledge a previously sent heartbeat.
        /// </summary>
        HeartbeatAck = 6,
        /// <summary>
        /// Sent by the server to force a client to reconnect
        /// </summary>
        Goodbye = 7
    }
}