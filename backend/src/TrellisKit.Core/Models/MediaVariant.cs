namespace TrellisKit.Core.Models
{
    using System;

    /// <summary>
    /// One stored image variant (table mediavariants), for example thumb in webp.
    /// </summary>
    public class MediaVariant
    {
        #region Public-Members

        /// <summary>
        /// Media identifier.
        /// </summary>
        public string MediaId { get; set; } = String.Empty;

        /// <summary>
        /// Variant name: thumb, medium, or large.
        /// </summary>
        public string Name { get; set; } = String.Empty;

        /// <summary>
        /// Format: webp or jpeg.
        /// </summary>
        public string Format { get; set; } = String.Empty;

        /// <summary>
        /// Width in pixels.
        /// </summary>
        public int Width { get; set; } = 0;

        /// <summary>
        /// Height in pixels.
        /// </summary>
        public int Height { get; set; } = 0;

        /// <summary>
        /// Stored size in bytes.
        /// </summary>
        public long SizeBytes { get; set; } = 0;

        /// <summary>
        /// File name under media/{id}/ in the images location, for example thumb.webp.
        /// </summary>
        public string FileName { get; set; } = String.Empty;

        /// <summary>
        /// SHA-256 of the file (hex, lowercase), or null when not computed (the download rendition carries it).
        /// </summary>
        public string? Sha256 { get; set; } = null;

        #endregion
    }
}
