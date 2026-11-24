namespace TrellisKit.Core.Models
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// Filters of instructor enumerations (docs/PHASE4_CONTRACT.md section 3.1).
    /// </summary>
    public class InstructorFilter
    {
        #region Public-Members

        /// <summary>
        /// Instructors linked to this venue, or null.
        /// </summary>
        public string? VenueId { get; set; } = null;

        /// <summary>
        /// Any of these rank keys.
        /// </summary>
        public List<string> RankKeys { get; set; } = new List<string>();

        /// <summary>
        /// Linked to an account or not, or null.
        /// </summary>
        public bool? Claimed { get; set; } = null;

        /// <summary>
        /// Linked to this member, or null.
        /// </summary>
        public string? LinkedUserId { get; set; } = null;

        /// <summary>
        /// Audiences the caller may receive, or null for all.
        /// </summary>
        public List<ContentAudienceEnum>? Audiences { get; set; } = null;

        /// <summary>
        /// Requested audience filter.
        /// </summary>
        public List<ContentAudienceEnum> AudienceFilter { get; set; } = new List<ContentAudienceEnum>();

        /// <summary>
        /// Admin status filter, or null for Active only.
        /// </summary>
        public List<InstructorStatusEnum>? Statuses { get; set; } = null;

        /// <summary>
        /// Only instructors without a moderation state (members).
        /// </summary>
        public bool LiveOnly { get; set; } = true;

        /// <summary>
        /// Text search over display name and affiliation, or null.
        /// </summary>
        public string? SearchText { get; set; } = null;

        #endregion
    }
}
