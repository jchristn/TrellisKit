namespace TrellisKit.Core.Requests
{
    using System;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// Typed conditions of the moderation actions log.
    /// </summary>
    public class ModerationActionFilter
    {
        #region Public-Members

        /// <summary>
        /// Moderator, or null.
        /// </summary>
        public string? ModeratorUserId { get; set; } = null;

        /// <summary>
        /// Target type, or null.
        /// </summary>
        public TargetTypeEnum? TargetType { get; set; } = null;

        /// <summary>
        /// Target identifier, or null.
        /// </summary>
        public string? TargetId { get; set; } = null;

        /// <summary>
        /// Author of the target, or null.
        /// </summary>
        public string? TargetAuthorUserId { get; set; } = null;

        /// <summary>
        /// Action, or null.
        /// </summary>
        public ModerationActionTypeEnum? Action { get; set; } = null;

        /// <summary>
        /// Only system actions (no moderator).
        /// </summary>
        public bool SystemOnly { get; set; } = false;

        #endregion
    }
}
