namespace TrellisKit.Core.Responses
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.PropertyConfig;

    /// <summary>
    /// The public property configuration returned by GET /api/v1/property: everything in property.json except the
    /// website build inputs (web), with absolute URLs for the brand images.
    /// </summary>
    public class PublicPropertyResponse
    {
        #region Public-Members

        /// <summary>
        /// Schema version.
        /// </summary>
        public int SchemaVersion { get; set; } = 0;

        /// <summary>
        /// Property identifier.
        /// </summary>
        public string Id { get; set; } = String.Empty;

        /// <summary>
        /// Cosmetic slug (folder names, image tags, infrastructure names). Never an identifier.
        /// </summary>
        public string Slug { get; set; } = String.Empty;

        /// <summary>
        /// Display name.
        /// </summary>
        public string Name { get; set; } = String.Empty;

        /// <summary>
        /// Short display name.
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
        /// Locale.
        /// </summary>
        public string Locale { get; set; } = String.Empty;

        /// <summary>
        /// Domains.
        /// </summary>
        public PropertyDomains Domains { get; set; } = new PropertyDomains();

        /// <summary>
        /// Contact addresses.
        /// </summary>
        public PropertyContact Contact { get; set; } = new PropertyContact();

        /// <summary>
        /// Brand images (with absolute URLs) and colors.
        /// </summary>
        public PropertyBrandView Brand { get; set; } = new PropertyBrandView();

        /// <summary>
        /// Mobile app identity.
        /// </summary>
        public PropertyMobile Mobile { get; set; } = new PropertyMobile();

        /// <summary>
        /// Membership product, or null in the Open access model (no purchases).
        /// </summary>
        public PropertyMembership? Membership { get; set; } = new PropertyMembership();

        /// <summary>
        /// The property's access configuration (docs/ACCESS_MODELS_CONTRACT.md section 5): model, anonymousBrowsing,
        /// gatedFeatures, defaultAudience, purchasesEnabled, signupCodesEnabled, revision. Clients adapt navigation,
        /// paywalls, locked items, and composers to it.
        /// </summary>
        public AccessPolicyView Access { get; set; } = new AccessPolicyView();

        /// <summary>
        /// The property minimum age in years (membership.minimumAge, also in the Open model where membership is null).
        /// </summary>
        public int MinimumAge { get; set; } = 18;

        /// <summary>
        /// Deprecated legacy profile taxonomy, generated from the seeded profile fields (rank, affiliation, goals,
        /// rankHistory); null when profileFields.legacyFieldsMode is Off. Use profileFields.
        /// </summary>
        public PropertyProfile? Profile { get; set; } = new PropertyProfile();

        /// <summary>
        /// Profile field definitions (enabled, visibility not Staff, built-ins included) in profile order. Clients render
        /// onboarding, the profile editor, profile pages, cards, and member filters from them.
        /// </summary>
        public List<PublicProfileField> ProfileFields
        {
            get { return _ProfileFields; }
            set { _ProfileFields = value ?? new List<PublicProfileField>(); }
        }

        /// <summary>
        /// Reactions in display order.
        /// </summary>
        public List<PropertyReaction> Reactions { get; set; } = new List<PropertyReaction>();

        /// <summary>
        /// Feature switches from property.json plus the engine-computed capability flags forums and search.
        /// </summary>
        public PropertyFeaturesView Features { get; set; } = new PropertyFeaturesView();

        /// <summary>Explicit collection consent/configuration; false until enabled by the property.</summary>
        public bool ProductEventsEnabled { get; set; } = false;

        /// <summary>
        /// Ideas board settings clients need: { memberSubmissions, publicBoardEnabled, voteOnClosed }.
        /// </summary>
        public PublicIdeasView Ideas { get; set; } = new PublicIdeasView();

        /// <summary>
        /// Terminology.
        /// </summary>
        public PropertyTerminology Terminology { get; set; } = new PropertyTerminology();

        /// <summary>
        /// Live update client options: checkIntervalMinutes from property.json, else the server setting.
        /// </summary>
        public PropertyUpdates Updates { get; set; } = new PropertyUpdates();

        /// <summary>
        /// Effective directory configuration (labels, programs, map without secrets).
        /// </summary>
        public PublicDirectoryConfig Directory { get; set; } = new PublicDirectoryConfig();

        /// <summary>
        /// Effective events configuration (categories).
        /// </summary>
        public PublicEventsConfig Events { get; set; } = new PublicEventsConfig();

        /// <summary>
        /// Effective library configuration (levels, downloads, progress cadence).
        /// </summary>
        public PublicLibraryConfig Library { get; set; } = new PublicLibraryConfig();

        /// <summary>
        /// Public landing pages: whether they are on and the path words of venue, event, and profile pages
        /// (docs/PHASE4_CONTRACT.md section 18). Never null.
        /// </summary>
        public PropertyPublicPages PublicPages { get; set; } = new PropertyPublicPages();

        /// <summary>
        /// The property's documents with their live current versions (docs/TRELLISKIT.md section 14), in property.json
        /// order. Bounded by property.json (at most 30). Never null.
        /// </summary>
        public List<DocumentSummaryView> Documents { get; set; } = new List<DocumentSummaryView>();

        #endregion

        #region Private-Members

        private List<PublicProfileField> _ProfileFields = new List<PublicProfileField>();

        #endregion
    }
}
