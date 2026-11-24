namespace TrellisKit.Core.Models
{
    using System;

    /// <summary>
    /// A member's direct message unread counts.
    /// </summary>
    public class MessagingUnreadCounts
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
