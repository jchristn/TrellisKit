namespace TrellisKit.Core.Responses
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// Bounded preview of a forum's most recently active thread.
    /// </summary>
    public class ForumLastThreadView
    {
        #region Public-Members

        /// <summary>
        /// Thread identifier.
        /// </summary>
        public string Id { get; set; } = String.Empty;

        /// <summary>
        /// Thread title.
        /// </summary>
        public string Title { get; set; } = String.Empty;

        /// <summary>
        /// Thread author.
        /// </summary>
        public UserSummary Author
        {
            get { return _Author; }
            set { _Author = value ?? new UserSummary(); }
        }

        /// <summary>
        /// Last activity.
        /// </summary>
        public DateTime LastActivityUtc { get; set; } = DateTime.UtcNow;

        #endregion

        #region Private-Members

        private UserSummary _Author = new UserSummary();

        #endregion
    }
}
