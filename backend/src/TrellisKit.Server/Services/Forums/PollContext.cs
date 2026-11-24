namespace TrellisKit.Server.Services
{
    using System;
    using TrellisKit.Core.Models;

    /// <summary>
    /// A poll with its thread context.
    /// </summary>
    public class PollContext
    {
        #region Public-Members

        /// <summary>
        /// The poll.
        /// </summary>
        public Poll Poll
        {
            get { return _Poll; }
            set { _Poll = value ?? throw new ArgumentNullException(nameof(Poll)); }
        }

        /// <summary>
        /// The poll's thread, forum, and source.
        /// </summary>
        public ThreadContext Thread
        {
            get { return _Thread; }
            set { _Thread = value ?? throw new ArgumentNullException(nameof(Thread)); }
        }

        #endregion

        #region Private-Members

        private Poll _Poll = new Poll();
        private ThreadContext _Thread = new ThreadContext();

        #endregion
    }
}
