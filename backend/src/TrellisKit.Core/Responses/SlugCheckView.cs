namespace TrellisKit.Core.Responses
{
    using System;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// Result of POST /api/v1/public-pages/slugs/check.
    /// </summary>
    public class SlugCheckView
    {
        #region Public-Members

        /// <summary>
        /// Kind checked.
        /// </summary>
        public PublicPageKindEnum Kind { get; set; } = PublicPageKindEnum.Profile;

        /// <summary>
        /// Slug checked (trimmed, lower case).
        /// </summary>
        public string Slug { get; set; } = String.Empty;

        /// <summary>
        /// Whether the slug passes the format rules.
        /// </summary>
        public bool Valid { get; set; } = false;

        /// <summary>
        /// Whether the slug is valid and free for the object.
        /// </summary>
        public bool Available { get; set; } = false;

        /// <summary>
        /// Why not: required, length, format, reserved, or taken; null when available.
        /// </summary>
        public string? Problem { get; set; } = null;

        /// <summary>
        /// Site-relative path the page would have.
        /// </summary>
        public string? Path { get; set; } = null;

        /// <summary>
        /// Absolute URL the page would have.
        /// </summary>
        public string? Url { get; set; } = null;

        #endregion
    }
}
