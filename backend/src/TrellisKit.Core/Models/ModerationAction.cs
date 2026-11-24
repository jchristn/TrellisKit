namespace TrellisKit.Core.Models
{
    using System;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// One moderation action row (contract sections 9.3 and 15): a moderator's Hide, Unhide, Remove, Restore, or Dismiss,
    /// or a system AutoHide (no moderator).
    /// </summary>
    public class ModerationAction
    {
        #region Public-Members

        /// <summary>
        /// Identifier (mac_).
        /// </summary>
        public string Id { get; set; } = String.Empty;

        /// <summary>
        /// Moderator, or null for the system or the admin token.
        /// </summary>
        public string? ModeratorUserId { get; set; } = null;

        /// <summary>
        /// Whether the break-glass admin token took the action (ModeratorUserId is then null). False for system actions.
        /// </summary>
        public bool BreakGlass { get; set; } = false;

        /// <summary>
        /// Target type.
        /// </summary>
        public TargetTypeEnum TargetType { get; set; } = TargetTypeEnum.FeedPost;

        /// <summary>
        /// Target identifier.
        /// </summary>
        public string TargetId { get; set; } = String.Empty;

        /// <summary>
        /// Author of the target.
        /// </summary>
        public string? TargetAuthorUserId { get; set; } = null;

        /// <summary>
        /// Action.
        /// </summary>
        public ModerationActionTypeEnum Action { get; set; } = ModerationActionTypeEnum.Hide;

        /// <summary>
        /// Reason.
        /// </summary>
        public string Reason { get; set; } = String.Empty;

        /// <summary>
        /// Moderator note, or null.
        /// </summary>
        public string? Note { get; set; } = null;

        /// <summary>
        /// Whether the author was notified.
        /// </summary>
        public bool NotifiedAuthor { get; set; } = false;

        /// <summary>
        /// Created (UTC).
        /// </summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Source forum of a Move action, or null.
        /// </summary>
        public string? FromForumId { get; set; } = null;

        /// <summary>
        /// Destination forum of a Move action, or null.
        /// </summary>
        public string? ToForumId { get; set; } = null;

        #endregion
    }
}
