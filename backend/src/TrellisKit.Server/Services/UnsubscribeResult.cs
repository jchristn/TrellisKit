namespace TrellisKit.Server.Services
{
    using TrellisKit.Core.Enums;

    /// <summary>
    /// Response of POST /api/v1/email/unsubscribe.
    /// </summary>
    public class UnsubscribeResult
    {
        /// <summary>
        /// Always true on success.
        /// </summary>
        public bool Unsubscribed { get; set; } = true;

        /// <summary>
        /// Category whose email was turned off.
        /// </summary>
        public NotificationCategoryEnum Category { get; set; } = NotificationCategoryEnum.FeedPosts;
    }
}
