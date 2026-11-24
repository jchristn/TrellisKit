namespace TrellisKit.Server.Services
{
    /// <summary>
    /// Metadata parsed from a page for a link preview, already decoded, stripped, normalized, and truncated. URLs are
    /// as found in the page (not yet resolved).
    /// </summary>
    public class LinkPreviewMetadata
    {
        #region Public-Members

        /// <summary>
        /// og:title, else twitter:title, else the title element; or null.
        /// </summary>
        public string? Title { get; set; } = null;

        /// <summary>
        /// og:description, else twitter:description, else meta name=description; or null.
        /// </summary>
        public string? Description { get; set; } = null;

        /// <summary>
        /// og:site_name, or null.
        /// </summary>
        public string? SiteName { get; set; } = null;

        /// <summary>
        /// og:image (or og:image:url, og:image:secure_url), else twitter:image (or twitter:image:src); or null.
        /// </summary>
        public string? ImageUrl { get; set; } = null;

        /// <summary>
        /// og:url, else link rel=canonical; or null.
        /// </summary>
        public string? CanonicalUrl { get; set; } = null;

        #endregion
    }
}
