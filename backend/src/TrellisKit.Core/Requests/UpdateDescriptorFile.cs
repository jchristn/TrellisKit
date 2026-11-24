namespace TrellisKit.Core.Requests
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// One file of an update descriptor: an asset, the launch asset, or a source map (the publish tool's shape).
    /// </summary>
    public class UpdateDescriptorFile
    {
        /// <summary>
        /// Multipart part name of the file (the asset key, or sourcemap-&lt;md5&gt; for source maps). Null uses the key.
        /// </summary>
        public string? Part { get; set; } = null;

        /// <summary>
        /// Path inside the export (informational).
        /// </summary>
        public string? Path { get; set; } = null;

        /// <summary>
        /// MD5 hex of the bytes (the Expo asset key).
        /// </summary>
        public string Key { get; set; } = String.Empty;

        /// <summary>
        /// SHA-256 base64url without padding.
        /// </summary>
        public string Hash { get; set; } = String.Empty;

        /// <summary>
        /// Content type.
        /// </summary>
        public string ContentType { get; set; } = String.Empty;

        /// <summary>
        /// File extension with the dot (assets).
        /// </summary>
        public string? FileExtension { get; set; } = null;

        /// <summary>
        /// Declared size in bytes, or null.
        /// </summary>
        public long? Size { get; set; } = null;

        /// <summary>
        /// Launch asset key a source map belongs to (source maps).
        /// </summary>
        public string? BundleKey { get; set; } = null;
    }
}
