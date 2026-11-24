namespace TrellisKit.Core.Integrations.Models
{
    using System;

    /// <summary>
    /// Metadata of a stored object.
    /// </summary>
    public class ObjectHead
    {
        #region Public-Members

        /// <summary>
        /// Size in bytes.
        /// </summary>
        public long SizeBytes { get; set; } = 0;

        /// <summary>
        /// Stored content type, or null.
        /// </summary>
        public string? ContentType { get; set; } = null;

        /// <summary>
        /// ETag, or null.
        /// </summary>
        public string? ETag { get; set; } = null;

        /// <summary>
        /// Last modification time.
        /// </summary>
        public DateTime LastModifiedUtc { get; set; } = DateTime.UtcNow;

        #endregion
    }
}
