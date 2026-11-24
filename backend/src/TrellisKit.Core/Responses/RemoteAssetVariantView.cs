namespace TrellisKit.Core.Responses
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// The dark variant of a remote asset.
    /// </summary>
    public class RemoteAssetVariantView
    {
        /// <summary>
        /// SHA-256 hex.
        /// </summary>
        public string Sha256 { get; set; } = String.Empty;

        /// <summary>
        /// Absolute URL.
        /// </summary>
        public string Url { get; set; } = String.Empty;

        /// <summary>
        /// Width.
        /// </summary>
        public int? Width { get; set; } = null;

        /// <summary>
        /// Height.
        /// </summary>
        public int? Height { get; set; } = null;

        /// <summary>
        /// Size.
        /// </summary>
        public long? SizeBytes { get; set; } = null;
    }
}
