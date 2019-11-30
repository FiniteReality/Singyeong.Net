using System;

namespace Singyeong
{
    /// <summary>
    /// Contains query helpers to form queries to a Singyeong server.
    /// </summary>
    public sealed class SingyeongQuery
    {
        /// <summary>
        /// A query helper for getting the value of a given metadata field.
        /// </summary>
        /// <param name="metadataName">
        /// The metadata field name to query for.
        /// </param>
        /// <typeparam name="T">
        /// The type of value to retrieve.
        /// </typeparam>
        /// <returns>
        /// This method does not return, and is intended to be used from send
        /// and broadcast filters.
        /// </returns>
        public T Metadata<T>(string metadataName)
            => throw new NotImplementedException();
    }
}