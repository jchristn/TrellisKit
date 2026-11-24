namespace TrellisKit.Server.Services
{
    using System;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Models;

    /// <summary>
    /// The conversation a comment tree belongs to: a feed post or a forum thread (Phase 3 section 4.10). The comment
    /// service reads and writes comments against a host; the forum module resolves thread hosts and applies the
    /// thread rules (locked, archived, forum mutes, forum media switches).
    /// </summary>
    public class CommentHost
    {
        #region Public-Members

        /// <summary>
        /// Target type of the comments (FeedPost or Thread).
        /// </summary>
        public TargetTypeEnum TargetType { get; set; } = TargetTypeEnum.FeedPost;

        /// <summary>
        /// Target identifier.
        /// </summary>
        public string TargetId { get; set; } = String.Empty;

        /// <summary>
        /// Author of the target (receives PostComment; may block commenters).
        /// </summary>
        public string AuthorUserId { get; set; } = String.Empty;

        /// <summary>
        /// Visible comments of the target.
        /// </summary>
        public long CommentCount { get; set; } = 0;

        /// <summary>
        /// Whether comments may carry media.
        /// </summary>
        public bool AllowMedia { get; set; } = true;

        /// <summary>
        /// Whether comments may carry video.
        /// </summary>
        public bool AllowVideo { get; set; } = true;

        /// <summary>
        /// Whether the caller moderates the target's comments (sees hidden and removed content).
        /// </summary>
        public bool ModeratorInScope { get; set; } = false;

        /// <summary>
        /// The accepted answer of a Question thread, or null.
        /// </summary>
        public string? AcceptedAnswerId { get; set; } = null;

        /// <summary>
        /// The thread of a Thread host, or null.
        /// </summary>
        public ForumThread? Thread { get; set; } = null;

        /// <summary>
        /// The idea of an Idea host, or null.
        /// </summary>
        public Idea? Idea { get; set; } = null;

        #endregion
    }
}
