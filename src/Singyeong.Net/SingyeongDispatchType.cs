namespace Singyeong
{
    /// <summary>
    /// Represents a Singyeong websocket dispatch type
    /// </summary>
    public enum SingyeongDispatchType
    {
        /// <summary>
        /// Sent by the client to update its metadata.
        /// </summary>
        UpdateMetadata,
        /// <summary>
        /// Sent by the client and server to send an event to a single client.
        /// </summary>
        Send,
        /// <summary>
        /// Sent by the client and server to broadcast an event to multiple
        /// clients.
        /// </summary>
        Broadcast
    }
}