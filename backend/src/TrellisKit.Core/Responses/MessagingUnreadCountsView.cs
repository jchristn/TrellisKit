namespace TrellisKit.Core.Responses
{
    using System;

    /// <summary>
    /// GET /api/v1/me/messaging/unread-count.
    /// </summary>
    public class MessagingUnreadCountsView
    {
        #region Public-Members

        /// <summary>
        /// Conversations with unread messages (not requests).
        /// </summary>
        public long Conversations { get; set; } = 0;

        /// <summary>
        /// Unread messages (not requests).
        /// </summary>
        public long Messages { get; set; } = 0;

        /// <summary>
        /// Pending requests.
        /// </summary>
        public long Requests { get; set; } = 0;

        #endregion
    }
}
