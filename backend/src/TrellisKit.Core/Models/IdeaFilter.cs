namespace TrellisKit.Core.Models
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// Conditions of an idea enumeration (docs/PHASE5_CONTRACT.md sections 7.1 and 7.2), built by the ideas service.
    /// </summary>
    public class IdeaFilter
    {
        #region Public-Members

        /// <summary>
        /// Whether the caller is ideas staff (every audience and moderation state).
        /// </summary>
        public bool IsStaff { get; set; } = false;

        /// <summary>
        /// The caller, or null when signed out (authors always see their own ideas).
        /// </summary>
        public string? ViewerUserId { get; set; } = null;

        /// <summary>
        /// Audiences the caller receives (ignored for staff).
        /// </summary>
        public List<ContentAudienceEnum> Audiences { get; set; } = new List<ContentAudienceEnum>();

        /// <summary>
        /// Only ideas with this audience (the audience filter), or null.
        /// </summary>
        public ContentAudienceEnum? AudienceFilter { get; set; } = null;

        /// <summary>
        /// Only these status keys (empty for any).
        /// </summary>
        public List<string> StatusKeys { get; set; } = new List<string>();

        /// <summary>
        /// Exclude these status keys (the open filter passes the closed keys).
        /// </summary>
        public List<string> ExcludedStatusKeys { get; set; } = new List<string>();

        /// <summary>
        /// Only ideas this member voted for, or null.
        /// </summary>
        public string? VotedByUserId { get; set; } = null;

        /// <summary>
        /// Only ideas by this author, or null.
        /// </summary>
        public string? AuthorUserId { get; set; } = null;

        /// <summary>
        /// Include merged ideas.
        /// </summary>
        public bool IncludeMerged { get; set; } = false;

        /// <summary>
        /// Staff filter: true only merged ideas, false only unmerged, null both (with IncludeMerged).
        /// </summary>
        public bool? Merged { get; set; } = null;

        /// <summary>
        /// Staff filter: true only hidden or removed ideas, false only unmoderated, null both.
        /// </summary>
        public bool? Hidden { get; set; } = null;

        /// <summary>
        /// Free text of a Relevance ordering, or null.
        /// </summary>
        public string? RelevanceText { get; set; } = null;

        #endregion
    }
}
