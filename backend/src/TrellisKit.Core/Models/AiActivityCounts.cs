namespace TrellisKit.Core.Models
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Conversation, message, and feedback counts over an analytics window.
    /// </summary>
    public class AiActivityCounts
    {
        #region Public-Members

        /// <summary>
        /// Conversations created.
        /// </summary>
        public long Conversations { get; set; } = 0;

        /// <summary>
        /// Member and visitor messages.
        /// </summary>
        public long Messages { get; set; } = 0;

        /// <summary>
        /// Assistant replies (not superseded).
        /// </summary>
        public long Replies { get; set; } = 0;

        /// <summary>
        /// Failed replies.
        /// </summary>
        public long FailedReplies { get; set; } = 0;

        /// <summary>
        /// Replies withheld by output moderation.
        /// </summary>
        public long BlockedReplies { get; set; } = 0;

        /// <summary>
        /// Replies answered by the fallback endpoint.
        /// </summary>
        public long FallbackReplies { get; set; } = 0;

        /// <summary>
        /// Members with a message in the window.
        /// </summary>
        public long ActiveMembers { get; set; } = 0;

        /// <summary>
        /// Visitors with a message in the window.
        /// </summary>
        public long Visitors { get; set; } = 0;

        /// <summary>
        /// Up ratings.
        /// </summary>
        public long ThumbsUp { get; set; } = 0;

        /// <summary>
        /// Down ratings.
        /// </summary>
        public long ThumbsDown { get; set; } = 0;

        #endregion
    }
}
