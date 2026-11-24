namespace TrellisKit.Core.Responses
{
    /// <summary>
    /// EnumerationResult of notifications with the member's unread count next to it (Phase 2 contract 8.3).
    /// </summary>
    public class NotificationEnumerationResult : EnumerationResult<NotificationView>
    {
        /// <summary>
        /// Unread notifications of the member (all of them, not only this page).
        /// </summary>
        public long UnreadCount { get; set; } = 0;

        /// <summary>
        /// Copy a page and add the unread count.
        /// </summary>
        /// <param name="page">Page.</param>
        /// <param name="unreadCount">Unread count.</param>
        /// <returns>Result.</returns>
        public static NotificationEnumerationResult From(EnumerationResult<NotificationView> page, long unreadCount)
        {
            return new NotificationEnumerationResult
            {
                Success = page.Success,
                MaxResults = page.MaxResults,
                Skip = page.Skip,
                ContinuationToken = page.ContinuationToken,
                EndOfResults = page.EndOfResults,
                TotalRecords = page.TotalRecords,
                RecordsRemaining = page.RecordsRemaining,
                TimestampUtc = page.TimestampUtc,
                Objects = page.Objects,
                UnreadCount = unreadCount
            };
        }
    }
}
