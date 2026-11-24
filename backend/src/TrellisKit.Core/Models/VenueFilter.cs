namespace TrellisKit.Core.Models
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// Filters of venue enumerations (docs/PHASE4_CONTRACT.md sections 3.2 and 3.9).
    /// </summary>
    public class VenueFilter
    {
        #region Public-Members

        /// <summary>
        /// Near query, or null.
        /// </summary>
        public GeoNear? Near { get; set; } = null;

        /// <summary>
        /// Bounding box, or null.
        /// </summary>
        public GeoBox? Box { get; set; } = null;

        /// <summary>
        /// Any of these program keys.
        /// </summary>
        public List<string> Programs { get; set; } = new List<string>();

        /// <summary>
        /// Exact country code (case-insensitive), or null.
        /// </summary>
        public string? CountryCode { get; set; } = null;

        /// <summary>
        /// Exact region (case-insensitive), or null.
        /// </summary>
        public string? Region { get; set; } = null;

        /// <summary>
        /// Exact locality (case-insensitive), or null.
        /// </summary>
        public string? Locality { get; set; } = null;

        /// <summary>
        /// Any of these visitor policies.
        /// </summary>
        public List<VisitorPolicyEnum> VisitorPolicies { get; set; } = new List<VisitorPolicyEnum>();

        /// <summary>
        /// Only verified venues.
        /// </summary>
        public bool VerifiedOnly { get; set; } = false;

        /// <summary>
        /// Admin filter on verification, or null.
        /// </summary>
        public bool? Verified { get; set; } = null;

        /// <summary>
        /// Include Closed venues.
        /// </summary>
        public bool IncludeClosed { get; set; } = false;

        /// <summary>
        /// Admin status filter, or null for the member rule.
        /// </summary>
        public List<VenueStatusEnum>? Statuses { get; set; } = null;

        /// <summary>
        /// Admin geo source filter (None finds venues that need a pin), or null.
        /// </summary>
        public GeoSourceEnum? GeoSource { get; set; } = null;

        /// <summary>
        /// Venues linked to this instructor, or null.
        /// </summary>
        public string? InstructorId { get; set; } = null;

        /// <summary>
        /// Audiences the caller may receive, or null for all.
        /// </summary>
        public List<ContentAudienceEnum>? Audiences { get; set; } = null;

        /// <summary>
        /// Requested audience filter.
        /// </summary>
        public List<ContentAudienceEnum> AudienceFilter { get; set; } = new List<ContentAudienceEnum>();

        /// <summary>
        /// Leave LocalityOnly venues out (map).
        /// </summary>
        public bool ExcludeLocalityOnly { get; set; } = false;

        /// <summary>
        /// Only venues without a moderation state (members).
        /// </summary>
        public bool LiveOnly { get; set; } = true;

        /// <summary>
        /// Staff view (sees every status).
        /// </summary>
        public bool Staff { get; set; } = false;

        /// <summary>
        /// Text search over name, affiliation, summary, locality, region, and linked instructor names, or null.
        /// </summary>
        public string? SearchText { get; set; } = null;

        #endregion
    }
}
