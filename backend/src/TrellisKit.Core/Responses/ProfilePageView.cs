namespace TrellisKit.Core.Responses
{
    using System;

    /// <summary>
    /// A member's public profile page settings (GET /api/v1/me/public-page; MeResponse.publicPage carries all but
    /// suggestedSlug).
    /// </summary>
    public class ProfilePageView
    {
        #region Public-Members

        /// <summary>
        /// Whether the page is on. Default false.
        /// </summary>
        public bool Enabled { get; set; } = false;

        /// <summary>
        /// Current slug, or null until one is chosen.
        /// </summary>
        public string? Slug { get; set; } = null;

        /// <summary>
        /// A free slug from the display name and the public locality and region (only on the settings route).
        /// </summary>
        public string? SuggestedSlug { get; set; } = null;

        /// <summary>
        /// Site-relative path of the page (null without a slug).
        /// </summary>
        public string? Path { get; set; } = null;

        /// <summary>
        /// Absolute URL of the page when it is on, otherwise null.
        /// </summary>
        public string? Url { get; set; } = null;

        #endregion
    }
}
