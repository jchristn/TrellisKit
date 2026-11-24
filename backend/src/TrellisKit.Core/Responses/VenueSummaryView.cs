namespace TrellisKit.Core.Responses
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// A venue list item (docs/PHASE4_CONTRACT.md section 2.2): the Venue without description, social links, schedule, instructors, and photos.
    /// </summary>
    public class VenueSummaryView : IMetadataPayload
    {
        #region Public-Members

        /// <summary>
        /// Identifier.
        /// </summary>
        public string Id { get; set; } = String.Empty;

        /// <summary>
        /// Name.
        /// </summary>
        public string Name { get; set; } = String.Empty;

        /// <summary>
        /// Current slug.
        /// </summary>
        public string Slug { get; set; } = String.Empty;

        /// <summary>
        /// Absolute URL of the venue's public landing page, or null when it has none (docs/PHASE4_CONTRACT.md section 18).
        /// </summary>
        public string? PublicUrl { get; set; } = null;

        /// <summary>
        /// Affiliation.
        /// </summary>
        public string? Affiliation { get; set; } = null;

        /// <summary>
        /// Summary.
        /// </summary>
        public string? Summary { get; set; } = null;

        /// <summary>
        /// Address as the caller may see it.
        /// </summary>
        public AddressView Address { get; set; } = new AddressView();

        /// <summary>
        /// Address visibility.
        /// </summary>
        public AddressVisibilityEnum AddressVisibility { get; set; } = AddressVisibilityEnum.Exact;

        /// <summary>
        /// Position as the caller may see it.
        /// </summary>
        public GeoView Geo { get; set; } = new GeoView();

        /// <summary>
        /// Distance from the near point (near queries only; whole kilometers for LocalityOnly), or null.
        /// </summary>
        public double? DistanceKm { get; set; } = null;

        /// <summary>
        /// IANA time zone.
        /// </summary>
        public string? TimeZone { get; set; } = null;

        /// <summary>
        /// Phone (omitted on public routes unless directory.publicShowsContact).
        /// </summary>
        public string? Phone { get; set; } = null;

        /// <summary>
        /// Email (omitted on public routes).
        /// </summary>
        public string? Email { get; set; } = null;

        /// <summary>
        /// Website.
        /// </summary>
        public string? WebsiteUrl { get; set; } = null;

        /// <summary>
        /// Programs with their labels.
        /// </summary>
        public List<PublicOption> Programs { get; set; } = new List<PublicOption>();

        /// <summary>
        /// Visitor policy.
        /// </summary>
        public VisitorPolicyEnum VisitorPolicy { get; set; } = VisitorPolicyEnum.Unknown;

        /// <summary>
        /// Visitor policy note.
        /// </summary>
        public string? VisitorPolicyNote { get; set; } = null;

        /// <summary>
        /// The first photo, or null.
        /// </summary>
        public MediaView? CoverPhoto { get; set; } = null;

        /// <summary>
        /// Linked instructors.
        /// </summary>
        public int InstructorCount { get; set; } = 0;

        /// <summary>
        /// Average star rating of the live reviews with one decimal, or null without reviews (docs/PHASE4_CONTRACT.md section 3.12).
        /// </summary>
        public double? RatingAverage { get; set; } = null;

        /// <summary>
        /// Live reviews.
        /// </summary>
        public int RatingCount { get; set; } = 0;

        /// <summary>
        /// Live member media items (section 3.13).
        /// </summary>
        public int MediaCount { get; set; } = 0;

        /// <summary>
        /// Verified by staff.
        /// </summary>
        public bool IsVerified { get; set; } = false;

        /// <summary>
        /// When it was verified.
        /// </summary>
        public DateTime? VerifiedUtc { get; set; } = null;

        /// <summary>
        /// Status.
        /// </summary>
        public VenueStatusEnum Status { get; set; } = VenueStatusEnum.Active;

        /// <summary>
        /// Audience.
        /// </summary>
        public ContentAudienceEnum Audience { get; set; } = ContentAudienceEnum.Members;

        /// <summary>
        /// Locked stub (the caller needs a membership to open it).
        /// </summary>
        public bool Locked { get; set; } = false;

        /// <summary>
        /// Labels.
        /// </summary>
        public List<string> Labels { get; set; } = new List<string>();

        /// <summary>
        /// Tags.
        /// </summary>
        public Dictionary<string, string> Tags { get; set; } = new Dictionary<string, string>();

        /// <summary>
        /// Caller capabilities (null on public routes).
        /// </summary>
        public VenueViewer? Viewer { get; set; } = new VenueViewer();

        /// <summary>
        /// Created.
        /// </summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Updated.
        /// </summary>
        public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;

        #endregion
    }
}
