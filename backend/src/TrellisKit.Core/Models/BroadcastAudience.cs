namespace TrellisKit.Core.Models
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// A validated broadcast segment compiled for SQL (docs/PHASE5_CONTRACT.md section 8.1). Every part is combined with
    /// AND; the values inside one group are alternatives (OR). The base rules (age attested, not banned, not deleted, and
    /// the access model's membership requirement) always apply.
    /// </summary>
    public class BroadcastAudience
    {
        #region Public-Members

        /// <summary>
        /// When true, the access model requires an active membership or a staff role to receive content (FullyGated).
        /// </summary>
        public bool RequireMembershipOrStaff { get; set; } = false;

        /// <summary>
        /// Contradictory filters (for example hasPushToken true and false): nobody matches.
        /// </summary>
        public bool MatchesNothing { get; set; } = false;

        /// <summary>
        /// Exactly these members (a test send to its caller), or empty for no restriction.
        /// </summary>
        public List<string> UserIds { get; set; } = new List<string>();

        /// <summary>
        /// Membership state: true active, false not active, null any.
        /// </summary>
        public bool? MembershipActive { get; set; } = null;

        /// <summary>
        /// Groups of membership sources (an active entitlement from one of the sources in each group).
        /// </summary>
        public List<List<string>> MembershipSourceGroups { get; set; } = new List<List<string>>();

        /// <summary>
        /// Groups of stores (an active subscription entitlement in one of the stores of each group).
        /// </summary>
        public List<List<string>> StoreGroups { get; set; } = new List<List<string>>();

        /// <summary>
        /// Profile field clauses (the member directory filter machinery).
        /// </summary>
        public List<ProfileFilterClause> Profile { get; set; } = new List<ProfileFilterClause>();

        /// <summary>
        /// Groups of normalized labels: the member has at least one label of each group.
        /// </summary>
        public List<List<string>> LabelAnyGroups { get; set; } = new List<List<string>>();

        /// <summary>
        /// Normalized labels the member must all have.
        /// </summary>
        public List<string> LabelsAll { get; set; } = new List<string>();

        /// <summary>
        /// Tag filters.
        /// </summary>
        public List<BroadcastTagFilter> Tags { get; set; } = new List<BroadcastTagFilter>();

        /// <summary>
        /// Signed up at or after this time, or null.
        /// </summary>
        public DateTime? SignedUpAfterUtc { get; set; } = null;

        /// <summary>
        /// Signed up before this time, or null.
        /// </summary>
        public DateTime? SignedUpBeforeUtc { get; set; } = null;

        /// <summary>
        /// Seen within this many days, or null.
        /// </summary>
        public int? ActiveWithinDays { get; set; } = null;

        /// <summary>
        /// Not seen for this many days (or never), or null.
        /// </summary>
        public int? InactiveForDays { get; set; } = null;

        /// <summary>
        /// Has (true) or has no (false) active push token, or null.
        /// </summary>
        public bool? HasPushToken { get; set; } = null;

        /// <summary>
        /// Has (true) or has no (false) verified email address, or null.
        /// </summary>
        public bool? HasVerifiedEmail { get; set; } = null;

        /// <summary>
        /// Groups of staff roles (the member holds one role of each group).
        /// </summary>
        public List<List<string>> RoleGroups { get; set; } = new List<List<string>>();

        /// <summary>
        /// Event RSVP filters (Phase 4 eventrsvps).
        /// </summary>
        public List<BroadcastEventRsvpFilter> EventRsvps { get; set; } = new List<BroadcastEventRsvpFilter>();

        /// <summary>
        /// Series the member follows (Phase 4 seriesfollows).
        /// </summary>
        public List<string> SeriesIds { get; set; } = new List<string>();

        #endregion
    }
}
