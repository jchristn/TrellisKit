namespace TrellisKit.Core.Models
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// An asset of one update (table updateassetrefs joined with updateassets).
    /// </summary>
    public class UpdateAssetRef
    {
        /// <summary>
        /// Owning update.
        /// </summary>
        public string UpdateId { get; set; } = String.Empty;

        /// <summary>
        /// Position (0 is the launch asset).
        /// </summary>
        public int Position { get; set; } = 0;

        /// <summary>
        /// SHA-256 hex (storage key).
        /// </summary>
        public string Sha256Hex { get; set; } = String.Empty;

        /// <summary>
        /// SHA-256 base64url without padding (manifest hash).
        /// </summary>
        public string HashB64Url { get; set; } = String.Empty;

        /// <summary>
        /// MD5 hex key from expo export.
        /// </summary>
        public string Key { get; set; } = String.Empty;

        /// <summary>
        /// File extension with the dot.
        /// </summary>
        public string FileExtension { get; set; } = String.Empty;

        /// <summary>
        /// Content type.
        /// </summary>
        public string ContentType { get; set; } = String.Empty;

        /// <summary>
        /// Size in bytes.
        /// </summary>
        public long SizeBytes { get; set; } = 0;

        /// <summary>
        /// Whether this is the JavaScript bundle.
        /// </summary>
        public bool IsLaunchAsset { get; set; } = false;
    }
}
