namespace TrellisKit.Core.Models
{
    using System;

    /// <summary>
    /// Push coalescing state per conversation and member.
    /// </summary>
    public class MessagePushState
    {
        #region Public-Members

        /// <summary>
        /// Conversation.
        /// </summary>
        public string ConversationId { get; set; } = String.Empty;

        /// <summary>
        /// Member.
        /// </summary>
        public string UserId { get; set; } = String.Empty;

        /// <summary>
        /// Last push (UTC).
        /// </summary>
        public DateTime LastPushUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Sequence number of the last pushed message.
        /// </summary>
        public long LastPushSeq { get; set; } = 0;

        #endregion
    }
}
