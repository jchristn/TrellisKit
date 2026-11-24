namespace TrellisKit.Core.Responses
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// The discussion thread of a feed post (docs/PHASE3_CONTRACT.md section 8.2).
    /// </summary>
    public class FeedPostDiscussionView
    {
        #region Public-Members

        /// <summary>
        /// Thread identifier.
        /// </summary>
        public string ThreadId { get; set; } = String.Empty;

        /// <summary>
        /// Forum identifier.
        /// </summary>
        public string ForumId { get; set; } = String.Empty;

        /// <summary>
        /// Forum name.
        /// </summary>
        public string ForumName { get; set; } = String.Empty;

        #endregion
    }
}
