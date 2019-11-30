using System;

namespace Singyeong
{
    /// <summary>
    /// An exception thrown when a Singyeong server has missed a client
    /// heartbeat.
    /// </summary>
    public class MissedHeartbeatException : Exception
    {
    }
}