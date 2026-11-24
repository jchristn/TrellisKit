namespace TrellisKit.Core.Responses
{
    using System;

    /// <summary>
    /// Result of POST /api/v1/conversations/{id}/read.
    /// </summary>
    public class ConversationReadResponse
    {
        #region Public-Members

        /// <summary>
        /// The caller's read position afterwards.
        /// </summary>
        public long MyLastReadSeq { get; set; } = 0;

        /// <summary>
        /// Unread messages afterwards.
        /// </summary>
        public int UnreadCount { get; set; } = 0;

        #endregion
    }
}
