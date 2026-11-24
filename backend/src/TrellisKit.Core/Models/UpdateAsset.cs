namespace TrellisKit.Core.Models
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// A stored, content-addressed live update asset (table updateassets).
    /// </summary>
    public class UpdateAsset
    {
        /// <summary>
        /// SHA-256 hex.
        /// </summary>
        public string Sha256Hex { get; set; } = String.Empty;

        /// <summary>
        /// SHA-256 base64url without padding.
        /// </summary>
        public string HashB64Url { get; set; } = String.Empty;

        /// <summary>
        /// Content type of the first upload.
        /// </summary>
        public string ContentType { get; set; } = String.Empty;

        /// <summary>
        /// Size in bytes.
        /// </summary>
        public long SizeBytes { get; set; } = 0;

        /// <summary>
        /// When the verified bytes were stored, or null.
        /// </summary>
        public DateTime? StoredUtc { get; set; } = null;

        /// <summary>
        /// Last time an update referenced the asset.
        /// </summary>
        public DateTime LastReferencedUtc { get; set; } = DateTime.UtcNow;
    }
}
