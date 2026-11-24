namespace TrellisKit.Server.Services.PublicPages
{
    using System;

    /// <summary>
    /// One public landing page to render: head metadata, the structured data, and the encoded main content.
    /// </summary>
    public class PublicPageDocument
    {
        #region Public-Members

        /// <summary>
        /// Page title (the title element and og:title).
        /// </summary>
        public string Title { get; set; } = String.Empty;

        /// <summary>
        /// Plain text description (meta description, og:description), at most 300 characters.
        /// </summary>
        public string Description { get; set; } = String.Empty;

        /// <summary>
        /// Absolute canonical URL, or null (the not found page).
        /// </summary>
        public string? CanonicalUrl { get; set; } = null;

        /// <summary>
        /// Absolute social preview image URL.
        /// </summary>
        public string ImageUrl { get; set; } = String.Empty;

        /// <summary>
        /// Alternative text of the preview image.
        /// </summary>
        public string ImageAlt { get; set; } = String.Empty;

        /// <summary>
        /// Open Graph type (website, profile, or event is not an OG type, so website or profile).
        /// </summary>
        public string OgType { get; set; } = "website";

        /// <summary>
        /// Serialized JSON-LD object, or null.
        /// </summary>
        public string? JsonLd { get; set; } = null;

        /// <summary>
        /// Encoded HTML of the main element's content (every value already escaped).
        /// </summary>
        public string MainHtml { get; set; } = String.Empty;

        /// <summary>
        /// Absolute member app link for the same object, or null.
        /// </summary>
        public string? AppUrl { get; set; } = null;

        /// <summary>
        /// Label of the member app link.
        /// </summary>
        public string AppLabel { get; set; } = String.Empty;

        /// <summary>
        /// Whether crawlers should skip the page (noindex).
        /// </summary>
        public bool NoIndex { get; set; } = false;

        #endregion
    }
}
