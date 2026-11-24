namespace TrellisKit.Core.Responses
{
    using System;

    /// <summary>
    /// The LinkPreview shape of contract section 5.9.
    /// </summary>
    public class LinkPreviewView
    {
        #region Public-Members

        /// <summary>
        /// Normalized URL the preview was built for.
        /// </summary>
        public string Url { get; set; } = String.Empty;

        /// <summary>
        /// Canonical URL, or null.
        /// </summary>
        public string? CanonicalUrl { get; set; } = null;

        /// <summary>
        /// Site name, or null.
        /// </summary>
        public string? SiteName { get; set; } = null;

        /// <summary>
        /// Title, or null.
        /// </summary>
        public string? Title { get; set; } = null;

        /// <summary>
        /// Description, or null.
        /// </summary>
        public string? Description { get; set; } = null;

        /// <summary>
        /// Image URL served by the engine (never the third-party URL), or null.
        /// </summary>
        public string? ImageUrl { get; set; } = null;

        /// <summary>
        /// Image width, or null.
        /// </summary>
        public int? ImageWidth { get; set; } = null;

        /// <summary>
        /// Image height, or null.
        /// </summary>
        public int? ImageHeight { get; set; } = null;

        /// <summary>
        /// When the page was fetched (UTC).
        /// </summary>
        public DateTime FetchedUtc { get; set; } = DateTime.UtcNow;

        #endregion
    }
}
