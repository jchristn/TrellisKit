namespace TrellisKit.Server.Services
{
    using System;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// A private media object resolved from a valid per-object URL: where it is stored, its size, its content type, and how
    /// long the URL stays valid.
    /// </summary>
    public class PrivateMediaFile
    {
        #region Public-Members

        /// <summary>
        /// Storage location.
        /// </summary>
        public MediaLocationEnum Location { get; set; } = MediaLocationEnum.Source;

        /// <summary>
        /// Object key.
        /// </summary>
        public string Key { get; set; } = String.Empty;

        /// <summary>
        /// Size in bytes.
        /// </summary>
        public long SizeBytes { get; set; } = 0;

        /// <summary>
        /// Content type.
        /// </summary>
        public string ContentType { get; set; } = "application/octet-stream";

        /// <summary>
        /// URL expiry (UTC).
        /// </summary>
        public DateTime ExpiresUtc { get; set; } = DateTime.UtcNow;

        #endregion
    }
}
