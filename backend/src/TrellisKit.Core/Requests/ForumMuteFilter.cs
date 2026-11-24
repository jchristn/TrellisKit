namespace TrellisKit.Core.Requests
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Responses;

    /// <summary>
    /// Forum mute enumeration filter (docs/PHASE3_CONTRACT.md section 11.3).
    /// </summary>
    public class ForumMuteFilter
    {
        #region Public-Members

        /// <summary>
        /// Forums in the caller's scope, or null for every forum.
        /// </summary>
        public List<string>? ScopeForumIds { get; set; } = null;

        /// <summary>
        /// Only mutes in this forum.
        /// </summary>
        public string? ForumId { get; set; } = null;

        /// <summary>
        /// Only mutes of this member.
        /// </summary>
        public string? UserId { get; set; } = null;

        /// <summary>
        /// Only active mutes.
        /// </summary>
        public bool ActiveOnly { get; set; } = true;

        /// <summary>
        /// Evaluation time.
        /// </summary>
        public DateTime NowUtc { get; set; } = DateTime.UtcNow;

        #endregion
    }
}
