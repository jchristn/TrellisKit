namespace TrellisKit.Server.Services
{
    using System;

    /// <summary>
    /// A tile proxy answer.
    /// </summary>
    public class TileProxyResponse
    {
        #region Public-Members

        /// <summary>
        /// HTTP status to send.
        /// </summary>
        public int StatusCode { get; set; } = 200;

        /// <summary>
        /// Content type.
        /// </summary>
        public string ContentType { get; set; } = "application/octet-stream";

        /// <summary>
        /// Body.
        /// </summary>
        public byte[] Body { get; set; } = Array.Empty<byte>();

        /// <summary>
        /// Whether the answer came from the in-memory cache.
        /// </summary>
        public bool FromCache { get; set; } = false;

        /// <summary>
        /// When the cached copy expires.
        /// </summary>
        public DateTime ExpiresUtc { get; set; } = DateTime.UtcNow;

        #endregion
    }
}
