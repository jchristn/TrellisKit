namespace TrellisKit.Server.Services.PublicPages
{
    using System;
    using TrellisKit.Core.PropertyConfig;

    /// <summary>
    /// The property brand a public landing page is rendered in.
    /// </summary>
    public class PublicPageBrand
    {
        #region Public-Members

        /// <summary>
        /// Property name.
        /// </summary>
        public string Name { get; set; } = String.Empty;

        /// <summary>
        /// Property tagline, or null.
        /// </summary>
        public string? Tagline { get; set; } = null;

        /// <summary>
        /// Absolute logo URL, or null.
        /// </summary>
        public string? LogoUrl { get; set; } = null;

        /// <summary>
        /// Absolute icon URL, or null.
        /// </summary>
        public string? IconUrl { get; set; } = null;

        /// <summary>
        /// Website origin without a trailing slash (web.publicBaseUrl).
        /// </summary>
        public string SiteUrl { get; set; } = String.Empty;

        /// <summary>
        /// Join link (sign up, or the membership page when purchases are on).
        /// </summary>
        public string JoinUrl { get; set; } = String.Empty;

        /// <summary>
        /// Sign in link.
        /// </summary>
        public string SignInUrl { get; set; } = String.Empty;

        /// <summary>
        /// Effective brand colors (light and dark).
        /// </summary>
        public PropertyBrandColors Colors { get; set; } = new PropertyBrandColors();

        /// <summary>
        /// Locale, for example en-US.
        /// </summary>
        public string Locale { get; set; } = "en-US";

        /// <summary>
        /// The property's word for a member.
        /// </summary>
        public string MemberWord { get; set; } = "member";

        #endregion
    }
}
