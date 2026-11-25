namespace TrellisKit.Core.Models
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// A directory venue row with its child lists (docs/PHASE4_CONTRACT.md sections 2.2 and 14).
    /// </summary>
    public class Venue
    {
        #region Public-Members

        /// <summary>
        /// Identifier (ven_).
        /// </summary>
        public string Id { get; set; } = String.Empty;

        /// <summary>
        /// URL slug, unique per property (case-insensitive) among live venues.
        /// </summary>
        public string Slug { get; set; } = String.Empty;

        /// <summary>
        /// Display name (2 to 120 characters).
        /// </summary>
        public string Name { get; set; } = String.Empty;

        /// <summary>
        /// Network, association, or parent organization (0 to 120), or null.
        /// </summary>
        public string? Affiliation { get; set; } = null;

        /// <summary>
        /// Short summary (0 to 300), or null.
        /// </summary>
        public string? Summary { get; set; } = null;

        /// <summary>
        /// Description in the Markdown subset, or null.
        /// </summary>
        public string? Description { get; set; } = null;

        /// <summary>
        /// Address line 1, or null.
        /// </summary>
        public string? Line1 { get; set; } = null;

        /// <summary>
        /// Address line 2, or null.
        /// </summary>
        public string? Line2 { get; set; } = null;

        /// <summary>
        /// Locality (city), or null.
        /// </summary>
        public string? Locality { get; set; } = null;

        /// <summary>
        /// Region (state or province), or null.
        /// </summary>
        public string? Region { get; set; } = null;

        /// <summary>
        /// Postal code, or null.
        /// </summary>
        public string? PostalCode { get; set; } = null;

        /// <summary>
        /// ISO 3166-1 alpha-2 country code, or null.
        /// </summary>
        public string? CountryCode { get; set; } = null;

        /// <summary>
        /// How much of the address members see.
        /// </summary>
        public AddressVisibilityEnum AddressVisibility { get; set; } = AddressVisibilityEnum.Exact;

        /// <summary>
        /// Stored latitude (WGS 84), or null.
        /// </summary>
        public double? Lat { get; set; } = null;

        /// <summary>
        /// Stored longitude (WGS 84), or null.
        /// </summary>
        public double? Lng { get; set; } = null;

        /// <summary>
        /// Where the position came from.
        /// </summary>
        public GeoSourceEnum GeoSource { get; set; } = GeoSourceEnum.None;

        /// <summary>
        /// When the position was last set, or null.
        /// </summary>
        public DateTime? GeocodedUtc { get; set; } = null;

        /// <summary>
        /// When a background geocode was requested (GeocodeWorker), or null.
        /// </summary>
        public DateTime? GeocodePendingUtc { get; set; } = null;

        /// <summary>
        /// IANA time zone id, or null.
        /// </summary>
        public string? TimeZone { get; set; } = null;

        /// <summary>
        /// Phone (E.164 or free text up to 40), or null.
        /// </summary>
        public string? Phone { get; set; } = null;

        /// <summary>
        /// Contact email, or null.
        /// </summary>
        public string? Email { get; set; } = null;

        /// <summary>
        /// Website (https), or null.
        /// </summary>
        public string? WebsiteUrl { get; set; } = null;

        /// <summary>
        /// Free text schedule, or null.
        /// </summary>
        public string? ScheduleText { get; set; } = null;

        /// <summary>Structured weekly schedule; null retains the legacy text display. On edit, omit to preserve or send null to clear.</summary>
        public TrellisKit.Core.Models.VenueSchedule? Schedule { get; set; } = null;

        /// <summary>
        /// Visitor policy.
        /// </summary>
        public VisitorPolicyEnum VisitorPolicy { get; set; } = VisitorPolicyEnum.Unknown;

        /// <summary>
        /// Visitor policy note (0 to 300), or null.
        /// </summary>
        public string? VisitorPolicyNote { get; set; } = null;

        /// <summary>
        /// Whether staff verified the listing.
        /// </summary>
        public bool IsVerified { get; set; } = false;

        /// <summary>
        /// When it was verified, or null.
        /// </summary>
        public DateTime? VerifiedUtc { get; set; } = null;

        /// <summary>
        /// Who verified it, or null.
        /// </summary>
        public string? VerifiedByUserId { get; set; } = null;

        /// <summary>
        /// Lifecycle status.
        /// </summary>
        public VenueStatusEnum Status { get; set; } = VenueStatusEnum.Active;

        /// <summary>
        /// Who may read it.
        /// </summary>
        public ContentAudienceEnum Audience { get; set; } = ContentAudienceEnum.Members;

        /// <summary>
        /// Linked instructors (recomputed from venueinstructors).
        /// </summary>
        public int InstructorCount { get; set; } = 0;

        /// <summary>
        /// Live reviews (recomputed from venuereviews: not deleted, moderation state None).
        /// </summary>
        public int RatingCount { get; set; } = 0;

        /// <summary>
        /// Sum of the star ratings of the live reviews (recomputed with RatingCount).
        /// </summary>
        public int RatingTotal { get; set; } = 0;

        /// <summary>
        /// Live member media items (recomputed from venuemedia).
        /// </summary>
        public int MediaCount { get; set; } = 0;

        /// <summary>
        /// Open reports.
        /// </summary>
        public int OpenReportCount { get; set; } = 0;

        /// <summary>
        /// Moderation state.
        /// </summary>
        public ModerationStateEnum ModerationState { get; set; } = ModerationStateEnum.None;

        /// <summary>
        /// When it was last moderated, or null.
        /// </summary>
        public DateTime? ModeratedUtc { get; set; } = null;

        /// <summary>
        /// Moderator, or null.
        /// </summary>
        public string? ModeratedByUserId { get; set; } = null;

        /// <summary>
        /// Moderation reason, or null.
        /// </summary>
        public string? ModerationReason { get; set; } = null;

        /// <summary>
        /// When it was closed, or null.
        /// </summary>
        public DateTime? ClosedUtc { get; set; } = null;

        /// <summary>
        /// Close reason, or null.
        /// </summary>
        public string? ClosedReason { get; set; } = null;

        /// <summary>
        /// Creator (staff, or the submitter of an approved submission), or null.
        /// </summary>
        public string? CreatedByUserId { get; set; } = null;

        /// <summary>
        /// Soft delete time, or null.
        /// </summary>
        public DateTime? DeletedUtc { get; set; } = null;

        /// <summary>
        /// Created.
        /// </summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Last change (the stale check of submissions compares it).
        /// </summary>
        public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Program keys, in key order.
        /// </summary>
        public List<string> Programs { get; set; } = new List<string>();

        /// <summary>
        /// Social links in display order.
        /// </summary>
        public List<DirectorySocialLink> SocialLinks { get; set; } = new List<DirectorySocialLink>();

        /// <summary>
        /// Photo media ids in display order.
        /// </summary>
        public List<string> PhotoMediaIds { get; set; } = new List<string>();

        /// <summary>
        /// Distance from the query point (near queries only), or null.
        /// </summary>
        public double? DistanceKm { get; set; } = null;

        #endregion

        #region Public-Methods

        /// <summary>
        /// A deep copy (child lists copied).
        /// </summary>
        /// <returns>Copy.</returns>
        public Venue Clone()
        {
            Venue copy = (Venue)MemberwiseClone();
            copy.Schedule = Schedule?.Clone();
            copy.Programs = new List<string>(Programs);
            copy.SocialLinks = SocialLinks.Select(l => new DirectorySocialLink { Id = l.Id, Kind = l.Kind, Url = l.Url, Position = l.Position }).ToList();
            copy.PhotoMediaIds = new List<string>(PhotoMediaIds);
            return copy;
        }

        #endregion
    }
}
