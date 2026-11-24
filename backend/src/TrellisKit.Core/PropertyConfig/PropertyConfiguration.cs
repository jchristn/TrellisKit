namespace TrellisKit.Core.PropertyConfig
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text.Json.Serialization;

    /// <summary>
    /// A property: the configured personality (name, brand, domains, profile taxonomy, reactions, membership product)
    /// that the engine serves. Loaded from property.json in the property directory by <see cref="PropertyLoader"/>.
    /// The file is public by design and never contains secrets.
    /// Thread safety: treat as immutable after loading; safe to share across threads.
    /// </summary>
    public class PropertyConfiguration
    {
        #region Public-Members

        /// <summary>Generic metrics, events, alerts, and dashboard declarations.</summary>
        public PropertyMetricsConfiguration Metrics { get; set; } = new PropertyMetricsConfiguration();


        /// <summary>
        /// Schema version of the file. Must be <see cref="PropertyValidator.SupportedSchemaVersion"/>.
        /// </summary>
        public int SchemaVersion { get; set; } = 0;

        /// <summary>
        /// Opaque, generated property identifier of the form app_{k-sortable}_{random} (see Helpers.PropertyIds).
        /// Assigned when the property is created and never renamed: it is in every row, storage key, token, header,
        /// and mobile build. Required.
        /// </summary>
        public string Id { get; set; } = String.Empty;

        /// <summary>
        /// Cosmetic, human-readable slug (lowercase letters, digits, hyphens; 1 to 63 characters) used only for
        /// folder names, image tags, and infrastructure resource names. Never identifies the property in data, storage
        /// keys, headers, or URLs. Optional; empty derives one from shortName.
        /// </summary>
        public string? Slug { get; set; } = null;

        /// <summary>
        /// Display name. Required.
        /// </summary>
        public string Name { get; set; } = String.Empty;

        /// <summary>
        /// Short display name for constrained spaces. Required.
        /// </summary>
        public string ShortName { get; set; } = String.Empty;

        /// <summary>
        /// Tagline.
        /// </summary>
        public string Tagline { get; set; } = String.Empty;

        /// <summary>
        /// Description.
        /// </summary>
        public string Description { get; set; } = String.Empty;

        /// <summary>
        /// Owner.
        /// </summary>
        public PropertyOwner Owner { get; set; } = new PropertyOwner();

        /// <summary>
        /// Locale, for example en-US.
        /// </summary>
        public string Locale { get; set; } = "en-US";

        /// <summary>
        /// Domains.
        /// </summary>
        public PropertyDomains Domains { get; set; } = new PropertyDomains();

        /// <summary>
        /// Contact and sender addresses.
        /// </summary>
        public PropertyContact Contact { get; set; } = new PropertyContact();

        /// <summary>
        /// Brand images and colors.
        /// </summary>
        public PropertyBrand Brand { get; set; } = new PropertyBrand();

        /// <summary>
        /// Mobile app identity.
        /// </summary>
        public PropertyMobile Mobile { get; set; } = new PropertyMobile();

        /// <summary>
        /// Membership product.
        /// </summary>
        public PropertyMembership Membership { get; set; } = new PropertyMembership();

        /// <summary>
        /// Profile taxonomy.
        /// </summary>
        public PropertyProfile Profile { get; set; } = new PropertyProfile();

        /// <summary>
        /// Profile field definitions this property seeds (docs/MULTIPROPERTY_CONTRACT.md section 9.13), in the admin
        /// create-body shape and profile order, or null. When present they win over the version 1 profile section, which
        /// is then kept only for build-time clients. After seeding the database is the source of truth; in
        /// SingleProperty mode a change of this list on disk is merged again (the folder wins when it changes).
        /// </summary>
        public List<TrellisKit.Core.Models.ProfileField>? ProfileFields { get; set; } = null;

        /// <summary>
        /// Reactions in display order. Keys are unique.
        /// </summary>
        public List<PropertyReaction> Reactions { get; set; } = new List<PropertyReaction>();

        /// <summary>
        /// Feature switches.
        /// </summary>
        public PropertyFeatures Features { get; set; } = new PropertyFeatures();

        /// <summary>AI scope policy for this property. Defaults to a bounded community assistant.</summary>
        public PropertyAiPolicy AiPolicy
        {
            get { return _AiPolicy; }
            set { _AiPolicy = value ?? new PropertyAiPolicy(); }
        }

        private PropertyAiPolicy _AiPolicy = new PropertyAiPolicy();

        /// <summary>
        /// Optional initial forums, created only when the property has no forums yet (docs/PHASE3_CONTRACT.md section 15).
        /// </summary>
        public List<PropertyForumSeed> Forums { get; set; } = new List<PropertyForumSeed>();

        /// <summary>
        /// Website build inputs (not served by the API).
        /// </summary>
        public PropertyWeb Web { get; set; } = new PropertyWeb();

        /// <summary>
        /// Terminology.
        /// </summary>
        public PropertyTerminology Terminology { get; set; } = new PropertyTerminology();

        /// <summary>
        /// Live update client options (updates.checkIntervalMinutes), or null to use the server setting.
        /// </summary>
        public PropertyUpdates? Updates { get; set; } = null;

        /// <summary>
        /// Access model section (seeds the stored access configuration). Null when absent, which means FullyGated.
        /// </summary>
        public PropertyAccess? Access { get; set; } = null;

        /// <summary>
        /// Directory section (venue and instructor labels, programs, instructor rank field, submission and claim
        /// switches), or null to use the engine defaults (docs/PHASE4_CONTRACT.md section 0.2).
        /// </summary>
        [JsonPropertyName("directory")]
        public PropertyDirectorySection? DirectorySection { get; set; } = null;

        /// <summary>
        /// Events section (categories, member proposals), or null to use the engine defaults.
        /// </summary>
        [JsonPropertyName("events")]
        public PropertyEventsSection? EventsSection { get; set; } = null;

        /// <summary>
        /// Library section (level source, level field, custom levels), or null to use the engine defaults.
        /// </summary>
        [JsonPropertyName("library")]
        public PropertyLibrarySection? LibrarySection { get; set; } = null;

        /// <summary>
        /// Public landing pages (enabled switch and the path words of venue, event, and profile pages), or null for the
        /// engine defaults (venues, events, profiles; docs/PHASE4_CONTRACT.md section 18).
        /// </summary>
        public PropertyPublicPages? PublicPages { get; set; } = null;

        /// <summary>
        /// The public pages section with the engine defaults when absent. Never null.
        /// </summary>
        [JsonIgnore]
        public PropertyPublicPages EffectivePublicPages
        {
            get { return PublicPages ?? _DefaultPublicPages; }
        }

        /// <summary>
        /// Ideas board section (seeds the idea statuses), or null for the engine default statuses.
        /// </summary>
        public PropertyIdeas? Ideas { get; set; } = null;

        /// <summary>
        /// Support section (ticket types seeded once), or null for the engine defaults (docs/PHASE5_CONTRACT.md section 6).
        /// </summary>
        public PropertySupport? Support { get; set; } = null;

        /// <summary>
        /// Property documents (docs/TRELLISKIT.md section 14): legal and informational Markdown files the engine
        /// versions, serves, and asks members to accept, at most 30. Null or empty means none.
        /// </summary>
        public List<PropertyDocument>? Documents { get; set; } = null;

        /// <summary>
        /// Full path of the directory the property was loaded from, or empty for a property built in code.
        /// </summary>
        [JsonIgnore]
        public string Directory { get; set; } = String.Empty;

        /// <summary>
        /// The effective slug: <see cref="Slug"/> when set, otherwise shortName lowercased with every run of other
        /// characters replaced by a hyphen (for example "Example" becomes "example").
        /// </summary>
        [JsonIgnore]
        public string EffectiveSlug
        {
            get
            {
                if (!String.IsNullOrWhiteSpace(Slug)) return Slug.Trim();
                string source = (String.IsNullOrWhiteSpace(ShortName) ? Name : ShortName) ?? String.Empty;
                System.Text.StringBuilder builder = new System.Text.StringBuilder();
                foreach (char c in source.ToLowerInvariant())
                {
                    if ((c >= 'a' && c <= 'z') || (c >= '0' && c <= '9')) builder.Append(c);
                    else if (builder.Length > 0 && builder[builder.Length - 1] != '-') builder.Append('-');
                }
                string slug = builder.ToString().Trim('-');
                if (slug.Length > 63) slug = slug.Substring(0, 63).Trim('-');
                return slug.Length == 0 ? "property" : slug;
            }
        }

        #endregion

        #region Private-Members

        private static readonly PropertyPublicPages _DefaultPublicPages = new PropertyPublicPages();

        #endregion

        #region Public-Methods

        /// <summary>
        /// Find a rank by key (case-insensitive; the result carries the canonical key).
        /// </summary>
        /// <param name="key">Rank key.</param>
        /// <returns>Rank, or null when the key is not defined.</returns>
        public PropertyRankValue? FindRank(string? key)
        {
            if (String.IsNullOrWhiteSpace(key) || Profile?.Rank?.Values == null) return null;
            return Profile.Rank.Values.FirstOrDefault(v => v != null && String.Equals(v.Key, key, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Find a goal by key (case-insensitive; the result carries the canonical key).
        /// </summary>
        /// <param name="key">Goal key.</param>
        /// <returns>Goal, or null when the key is not defined.</returns>
        public PropertyGoalValue? FindGoal(string? key)
        {
            if (String.IsNullOrWhiteSpace(key) || Profile?.Goals?.Values == null) return null;
            return Profile.Goals.Values.FirstOrDefault(v => v != null && String.Equals(v.Key, key, StringComparison.OrdinalIgnoreCase));
        }

        #endregion
    }
}
