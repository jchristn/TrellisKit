namespace TrellisKit.Core.Models
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// Filters of venue review and venue media enumerations (docs/PHASE4_CONTRACT.md sections 3.12 and 3.13).
    /// </summary>
    public class VenueContributionFilter
    {
        #region Public-Members

        /// <summary>
        /// Venue (required).
        /// </summary>
        public string VenueId { get; set; } = String.Empty;

        /// <summary>
        /// Include hidden and removed rows (directory staff).
        /// </summary>
        public bool IncludeModerated { get; set; } = false;

        /// <summary>
        /// Rows by these members are left out (blocks in either direction).
        /// </summary>
        public List<string> HiddenAuthorIds { get; set; } = new List<string>();

        /// <summary>
        /// Only rows by this member, or null.
        /// </summary>
        public string? AuthorUserId { get; set; } = null;

        /// <summary>
        /// Reviews: any of these star ratings (empty for all).
        /// </summary>
        public List<int> Ratings { get; set; } = new List<int>();

        /// <summary>
        /// Reviews: only with (true) or without (false) a reply, or null for both.
        /// </summary>
        public bool? HasReply { get; set; } = null;

        /// <summary>
        /// Media: only this kind, or null.
        /// </summary>
        public MediaKindEnum? Kind { get; set; } = null;

        #endregion
    }
}
