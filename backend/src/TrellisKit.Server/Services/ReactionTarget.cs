namespace TrellisKit.Server.Services
{
    using System;

    /// <summary>
    /// The reactable content a reaction change applies to: its author, its post, and its excerpt (for the
    /// notification).
    /// </summary>
    public class ReactionTarget
    {
        #region Public-Members

        /// <summary>
        /// Author of the content.
        /// </summary>
        public string AuthorUserId { get; set; } = String.Empty;

        /// <summary>
        /// Feed post the content belongs to, or null.
        /// </summary>
        public string? FeedPostId { get; set; } = null;

        /// <summary>
        /// Excerpt of the content, or null.
        /// </summary>
        public string? Excerpt { get; set; } = null;

        /// <summary>
        /// Thread of a thread or thread comment target, or null.
        /// </summary>
        public string? ThreadId { get; set; } = null;

        /// <summary>
        /// Forum of a thread or thread comment target, or null.
        /// </summary>
        public string? ForumId { get; set; } = null;

        /// <summary>
        /// App path of the reacted object for the Reaction notification (content target providers set it), or null.
        /// </summary>
        public string? Path { get; set; } = null;

        #endregion
    }
}
