namespace TrellisKit.Core.Requests
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// Create or edit a venue (docs/PHASE4_CONTRACT.md sections 2.2 and 3.9). Omitted fields stay on edit.
    /// </summary>
    public class VenueRequest
    {
        #region Public-Members

        /// <summary>
        /// Name (2 to 120).
        /// </summary>
        public string? Name { get; set; } = null;

        /// <summary>
        /// Slug (derived from the name when omitted on create).
        /// </summary>
        public string? Slug { get; set; } = null;

        /// <summary>
        /// Affiliation (0 to 120).
        /// </summary>
        public string? Affiliation { get; set; } = null;

        /// <summary>
        /// Summary (0 to 300).
        /// </summary>
        public string? Summary { get; set; } = null;

        /// <summary>
        /// Description (0 to directory.maxDescriptionLength).
        /// </summary>
        public string? Description { get; set; } = null;

        /// <summary>
        /// Address.
        /// </summary>
        public AddressRequest? Address { get; set; } = null;

        /// <summary>
        /// Address visibility (default Exact for staff-created venues).
        /// </summary>
        public AddressVisibilityEnum? AddressVisibility { get; set; } = null;

        /// <summary>
        /// Manual pin (geo.source Manual); null lat and lng clear it.
        /// </summary>
        public GeoRequest? Geo { get; set; } = null;

        /// <summary>
        /// Run the geocoder now and fail with 400 GeocodeFailed when nothing matches.
        /// </summary>
        public bool? Geocode { get; set; } = null;

        /// <summary>
        /// IANA time zone id.
        /// </summary>
        public string? TimeZone { get; set; } = null;

        /// <summary>
        /// Phone (up to 40).
        /// </summary>
        public string? Phone { get; set; } = null;

        /// <summary>
        /// Email.
        /// </summary>
        public string? Email { get; set; } = null;

        /// <summary>
        /// Website (https).
        /// </summary>
        public string? WebsiteUrl { get; set; } = null;

        /// <summary>
        /// Social links (at most directory.maxSocialLinks).
        /// </summary>
        public List<SocialLinkRequest>? SocialLinks { get; set; } = null;

        /// <summary>
        /// Schedule (0 to 1000).
        /// </summary>
        public string? ScheduleText { get; set; } = null;

        /// <summary>Structured weekly schedule; null retains the legacy text display. On edit, omit to preserve or send null to clear.</summary>
        public TrellisKit.Core.Models.VenueSchedule? Schedule { get; set; } = null;

        /// <summary>
        /// Program keys.
        /// </summary>
        public List<string>? Programs { get; set; } = null;

        /// <summary>
        /// Visitor policy.
        /// </summary>
        public VisitorPolicyEnum? VisitorPolicy { get; set; } = null;

        /// <summary>
        /// Visitor policy note (0 to 300).
        /// </summary>
        public string? VisitorPolicyNote { get; set; } = null;

        /// <summary>
        /// Ready image media ids in display order (at most directory.maxPhotos).
        /// </summary>
        public List<string>? PhotoMediaIds { get; set; } = null;

        /// <summary>
        /// Audience (Public only by SystemAdmin).
        /// </summary>
        public ContentAudienceEnum? Audience { get; set; } = null;

        /// <summary>
        /// Labels.
        /// </summary>
        public List<string>? Labels { get; set; } = null;

        /// <summary>
        /// Tags.
        /// </summary>
        public Dictionary<string, string>? Tags { get; set; } = null;

        #endregion
    }
}
