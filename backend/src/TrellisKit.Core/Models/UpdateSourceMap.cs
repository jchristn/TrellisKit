namespace TrellisKit.Core.Models
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// A private source map uploaded with an update (table updatesourcemaps). Never served to apps.
    /// </summary>
    public class UpdateSourceMap
    {
        /// <summary>
        /// Owning update.
        /// </summary>
        public string UpdateId { get; set; } = String.Empty;

        /// <summary>
        /// Position.
        /// </summary>
        public int Position { get; set; } = 0;

        /// <summary>
        /// SHA-256 hex.
        /// </summary>
        public string Sha256Hex { get; set; } = String.Empty;

        /// <summary>
        /// MD5 hex of the map.
        /// </summary>
        public string Key { get; set; } = String.Empty;

        /// <summary>
        /// Launch asset key the map belongs to.
        /// </summary>
        public string BundleKey { get; set; } = String.Empty;

        /// <summary>
        /// Size in bytes.
        /// </summary>
        public long SizeBytes { get; set; } = 0;

        /// <summary>
        /// Private object key.
        /// </summary>
        public string StorageKey { get; set; } = String.Empty;
    }
}
