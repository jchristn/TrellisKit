namespace TrellisKit.Core.Requests
{
    using System;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// Typed conditions of a member's content history (moderation).
    /// </summary>
    public class ModerationContentFilter
    {
        #region Public-Members

        /// <summary>
        /// Author (required).
        /// </summary>
        public string AuthorUserId { get; set; } = String.Empty;

        /// <summary>
        /// FeedPost or Comment, or null for both.
        /// </summary>
        public TargetTypeEnum? TargetType { get; set; } = null;

        /// <summary>
        /// Visible, Hidden, Removed, or Deleted, or null.
        /// </summary>
        public string? State { get; set; } = null;

        #endregion
    }
}
