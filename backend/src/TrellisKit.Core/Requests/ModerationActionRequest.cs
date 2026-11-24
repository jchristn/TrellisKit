namespace TrellisKit.Core.Requests
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// POST /api/v1/admin/moderation/actions body (contract section 9.3).
    /// </summary>
    public class ModerationActionRequest
    {
        #region Public-Members

        /// <summary>
        /// FeedPost or Comment.
        /// </summary>
        public string? TargetType { get; set; } = null;

        /// <summary>
        /// Target identifier.
        /// </summary>
        public string? TargetId { get; set; } = null;

        /// <summary>
        /// Hide, Unhide, Remove, or Restore.
        /// </summary>
        public string? Action { get; set; } = null;

        /// <summary>
        /// Reason (3 to 500 characters): free text or a report reason code.
        /// </summary>
        public string? Reason { get; set; } = null;

        /// <summary>
        /// Optional moderator note (at most 2000).
        /// </summary>
        public string? Note { get; set; } = null;

        /// <summary>
        /// Send a ModerationAction notification. Default true for Remove and Restore, false for Hide and Unhide.
        /// </summary>
        public bool? NotifyAuthor { get; set; } = null;

        /// <summary>
        /// Mark every Open report on the target ActionTaken with this action. Default true.
        /// </summary>
        public bool? ResolveReports { get; set; } = null;

        /// <summary>
        /// Optional labels for the new moderation action (docs/ENUMERATION_AND_METADATA.md section 1). Moderators may set staff: entries.
        /// </summary>
        public List<string>? Labels { get; set; } = null;

        /// <summary>
        /// Optional tags for the new moderation action (docs/ENUMERATION_AND_METADATA.md section 1). Moderators may set staff: entries.
        /// </summary>
        public Dictionary<string, string>? Tags { get; set; } = null;

        #endregion
    }
}
