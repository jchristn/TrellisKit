namespace TrellisKit.Core.Responses
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// The source of a LinkedFeedPost thread.
    /// </summary>
    public class ThreadSourceView
    {
        #region Public-Members

        /// <summary>
        /// Source type.
        /// </summary>
        public string Type { get; set; } = "FeedPost";

        /// <summary>
        /// Source identifier.
        /// </summary>
        public string Id { get; set; } = String.Empty;

        /// <summary>
        /// Source title, or null.
        /// </summary>
        public string? Title { get; set; } = null;

        /// <summary>
        /// Source author, or null.
        /// </summary>
        public UserSummary? Author { get; set; } = null;

        #endregion
    }
}
