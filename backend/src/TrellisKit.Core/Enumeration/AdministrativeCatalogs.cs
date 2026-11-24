namespace TrellisKit.Core.Enumeration
{
    using TrellisKit.Core.Enums;

    /// <summary>Catalog collections use standard paging, search, dates and metadata semantics. Immutable rows use Unix epoch.</summary>
    public static class AdministrativeCatalogs
    {
        /// <summary>Deployment setting fields.</summary>
        public static readonly EnumerationCollection Settings = Create("setting-fields", true);
        /// <summary>Property setting fields.</summary>
        public static readonly EnumerationCollection PropertySettings = Create("property-setting-fields", true);
        /// <summary>Directory integration setting fields.</summary>
        public static readonly EnumerationCollection DirectorySettings = Create("directory-setting-fields", true);
        /// <summary>Email templates.</summary>
        public static readonly EnumerationCollection EmailTemplates = Create("email-templates", true, "category");
        /// <summary>Staff email events.</summary>
        public static readonly EnumerationCollection EmailEvents = Create("email-staff-events", true);
        /// <summary>Member notification categories.</summary>
        public static readonly EnumerationCollection NotificationPreferences = Create("notification-preferences", false);
        /// <summary>Configured reactions.</summary>
        public static readonly EnumerationCollection Reactions = Create("reaction-set", false)
            .Order("ConfiguredAscending", "rank", false, EnumerationKeyKindEnum.Number).DefaultTo("ConfiguredAscending");
        /// <summary>Finite ranked assistant context shortlist; no source creation timestamp is available.</summary>
        public static readonly EnumerationCollection KnowledgePreview = Create("knowledge-preview", true, "query", "asUserId")
            .Order("RankAscending", "rank", false, EnumerationKeyKindEnum.Number).DefaultTo("RankAscending");
        /// <summary>The finite ranked provider result for a scoped place query, at most five candidates.</summary>
        public static readonly EnumerationCollection GeocodeCandidates = Create("geocode-candidates", false, "query")
            .Order("RankAscending", "rank", false, EnumerationKeyKindEnum.Number).DefaultTo("RankAscending");
        /// <summary>Requested, visible media status rows.</summary>
        public static readonly EnumerationCollection MediaBatch = Create("media-batch", false, "ids");
        /// <summary>Declared documents.</summary>
        public static readonly EnumerationCollection Documents = Create("admin-documents", true);
        /// <summary>Registered domains.</summary>
        public static readonly EnumerationCollection Domains = Create("property-domains", true);
        /// <summary>Registered applications.</summary>
        public static readonly EnumerationCollection Apps = Create("property-apps", true);
        /// <summary>Option distribution for a declared profile field.</summary>
        public static readonly EnumerationCollection ProfileOptions = Create("profile-field-option-counts", true);

        private static EnumerationCollection Create(string name, bool admin, params string[] filters)
        {
            return new EnumerationCollection(name, null, "key", "createdutc", admin) { HasInlineMetadata = true }
                .Order("KeyAscending", "key", false, EnumerationKeyKindEnum.Text).Order("KeyDescending", "key", true, EnumerationKeyKindEnum.Text).Search("key").Filters(filters);
        }
    }
}
