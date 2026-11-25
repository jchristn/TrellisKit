namespace TrellisKit.Core.PropertyConfig
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// The optional property.json publicPages section (docs/TRELLISKIT.md section 2.6, docs/PHASE4_CONTRACT.md section 18):
    /// whether the server renders public landing pages and the first path segment of each kind, for example
    /// /venues/{slug}. Absent means the engine defaults shown.
    /// </summary>
    public class PropertyPublicPages
    {
        #region Public-Members

        /// <summary>
        /// Default venues path word.
        /// </summary>
        public const string DefaultVenuesPath = "venues";

        /// <summary>
        /// Default events path word.
        /// </summary>
        public const string DefaultEventsPath = "events";

        /// <summary>
        /// Default profiles path word.
        /// </summary>
        public const string DefaultProfilesPath = "profiles";

        /// <summary>
        /// Default catalog path word.
        /// </summary>
        public const string DefaultCatalogPath = "catalog";

        /// <summary>
        /// First path segments the website, the member app, and the server already use; no path word may be one of them.
        /// </summary>
        public static readonly IReadOnlyList<string> ReservedPaths = new[]
        {
            "account-deleted", "admin", "announcements", "api", "app", "assets", "assistants", "auth", "brand", "embed", "forgot-password", "healthz", "join", "lessons", "openapi",
            "platform", "public", "reset-password", "sign-in", "sign-up", "sitemap", "swagger", "threads", "verify-email"
        };

        /// <summary>
        /// Whether the server renders the pages and lists them in the sitemap. Default true.
        /// </summary>
        public bool Enabled { get; set; } = true;

        /// <summary>
        /// First path segment of venue pages (2 to 40 lower case letters, digits, and single hyphens). Default venues.
        /// </summary>
        public string VenuesPath
        {
            get { return _VenuesPath; }
            set { _VenuesPath = String.IsNullOrWhiteSpace(value) ? DefaultVenuesPath : value.Trim(); }
        }

        /// <summary>
        /// First path segment of event pages. Default events.
        /// </summary>
        public string EventsPath
        {
            get { return _EventsPath; }
            set { _EventsPath = String.IsNullOrWhiteSpace(value) ? DefaultEventsPath : value.Trim(); }
        }

        /// <summary>
        /// First path segment of member profile pages. Default profiles.
        /// </summary>
        public string ProfilesPath
        {
            get { return _ProfilesPath; }
            set { _ProfilesPath = String.IsNullOrWhiteSpace(value) ? DefaultProfilesPath : value.Trim(); }
        }

        /// <summary>
        /// First path segment of published catalog entry pages (2 to 40 lower case letters, digits, and single hyphens).
        /// Default catalog. Pages render only when features.catalog is on.
        /// </summary>
        public string CatalogPath
        {
            get { return _CatalogPath; }
            set { _CatalogPath = String.IsNullOrWhiteSpace(value) ? DefaultCatalogPath : value.Trim(); }
        }

        #endregion

        #region Private-Members

        private string _VenuesPath = DefaultVenuesPath;
        private string _EventsPath = DefaultEventsPath;
        private string _ProfilesPath = DefaultProfilesPath;
        private string _CatalogPath = DefaultCatalogPath;

        #endregion

        #region Public-Methods

        /// <summary>
        /// The path word of a kind.
        /// </summary>
        /// <param name="kind">Kind.</param>
        /// <returns>Path word.</returns>
        public string PathOf(PublicPageKindEnum kind)
        {
            if (kind == PublicPageKindEnum.Venue) return VenuesPath;
            if (kind == PublicPageKindEnum.Event) return EventsPath;
            if (kind == PublicPageKindEnum.Catalog) return CatalogPath;
            return ProfilesPath;
        }

        /// <summary>
        /// The kind whose path word matches, or null.
        /// </summary>
        /// <param name="path">First path segment.</param>
        /// <returns>Kind, or null.</returns>
        public PublicPageKindEnum? KindOf(string? path)
        {
            if (String.IsNullOrEmpty(path)) return null;
            if (String.Equals(path, VenuesPath, StringComparison.Ordinal)) return PublicPageKindEnum.Venue;
            if (String.Equals(path, EventsPath, StringComparison.Ordinal)) return PublicPageKindEnum.Event;
            if (String.Equals(path, ProfilesPath, StringComparison.Ordinal)) return PublicPageKindEnum.Profile;
            if (String.Equals(path, CatalogPath, StringComparison.Ordinal)) return PublicPageKindEnum.Catalog;
            return null;
        }

        #endregion
    }
}
