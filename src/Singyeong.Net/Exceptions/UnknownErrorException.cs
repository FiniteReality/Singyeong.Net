using System;

namespace Singyeong
{
    /// <summary>
    /// An exception thrown when a Singyeong server sends
    /// <see cref="SingyeongOpcode.Invalid" /> indicating that an error
    /// occured.
    /// </summary>
    public class UnknownErrorException : Exception
    {
        /// <summary>
        /// Initializes a new instance of
        /// <see cref="UnknownErrorException"/>.
        /// </summary>
        /// <param name="message">
        /// The error message sent by the server.
        /// </param>
        public UnknownErrorException(string message) : base(message)
        { }
    }
}