namespace TrellisKit.Core.Requests
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Responses;

    /// <summary>
    /// Forum enumeration filter (docs/PHASE3_CONTRACT.md sections 3.1 and 3.2).
    /// </summary>
    public class ForumFilter
    {
        #region Public-Members

        /// <summary>
        /// Only forums in this section.
        /// </summary>
        public string? Section { get; set; } = null;

        /// <summary>
        /// Only forums this member follows.
        /// </summary>
        public string? FollowedByUserId { get; set; } = null;

        /// <summary>
        /// Include archived forums.
        /// </summary>
        public bool IncludeArchived { get; set; } = false;

        /// <summary>
        /// Only archived (true) or active (false) forums (admin).
        /// </summary>
        public bool? IsArchived { get; set; } = null;

        /// <summary>
        /// Audiences the caller may read, or null for every audience.
        /// </summary>
        public List<ContentAudienceEnum>? Audiences { get; set; } = null;

        /// <summary>
        /// Only forums with this audience.
        /// </summary>
        public ContentAudienceEnum? Audience { get; set; } = null;

        #endregion
    }
}
