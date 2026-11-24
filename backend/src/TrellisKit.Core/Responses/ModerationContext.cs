namespace TrellisKit.Core.Responses
{
    using System;

    /// <summary>
    /// Deep-link context of a moderation target: { feedPostId, parentCommentId }.
    /// </summary>
    public class ModerationContext
    {
        #region Public-Members

        /// <summary>
        /// Post of the target, or null.
        /// </summary>
        public string? FeedPostId { get; set; } = null;

        /// <summary>
        /// Parent comment, or null.
        /// </summary>
        public string? ParentCommentId { get; set; } = null;

        /// <summary>
        /// Thread of a Thread target, or of a comment on a thread (Phase 3), or null.
        /// </summary>
        public string? ThreadId { get; set; } = null;

        /// <summary>
        /// Forum of a thread or thread comment target (Phase 3), or null.
        /// </summary>
        public string? ForumId { get; set; } = null;

        #endregion
    }
}
