namespace TrellisKit.Server.Services
{
    using System;
    using TrellisKit.Core.Models;

    /// <summary>
    /// A thread with its forum and, for a LinkedFeedPost thread, its source feed post.
    /// </summary>
    public class ThreadContext
    {
        #region Public-Members

        /// <summary>
        /// The thread.
        /// </summary>
        public ForumThread Thread
        {
            get { return _Thread; }
            set { _Thread = value ?? throw new ArgumentNullException(nameof(Thread)); }
        }

        /// <summary>
        /// The thread's forum.
        /// </summary>
        public Forum Forum
        {
            get { return _Forum; }
            set { _Forum = value ?? throw new ArgumentNullException(nameof(Forum)); }
        }

        /// <summary>
        /// The source feed post of a LinkedFeedPost thread, or null.
        /// </summary>
        public FeedPost? Source { get; set; } = null;

        #endregion

        #region Private-Members

        private ForumThread _Thread = new ForumThread();
        private Forum _Forum = new Forum();

        #endregion
    }
}
