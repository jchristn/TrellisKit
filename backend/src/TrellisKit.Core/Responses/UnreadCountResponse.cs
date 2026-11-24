namespace TrellisKit.Core.Responses
{
    using System;

    /// <summary>
    /// Unread notification count (Phase 2 contract 8.3 and 8.4).
    /// </summary>
    public class UnreadCountResponse
    {
        /// <summary>
        /// Unread notifications.
        /// </summary>
        public long UnreadCount { get; set; } = 0;

        /// <summary>
        /// Latest updatedUtc of the member's notifications, or null when there are none. Omitted by the read and unread
        /// routes, which return only the count.
        /// </summary>
        public DateTime? LatestUpdatedUtc { get; set; } = null;
    }
}
