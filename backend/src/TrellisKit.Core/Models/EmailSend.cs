namespace TrellisKit.Core.Models
{
    using System;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// A row of emailsends: the idempotency record of one category email to one member about one source item.
    /// </summary>
    public class EmailSend
    {
        /// <summary>
        /// Identifier (ems_).
        /// </summary>
        public string Id { get; set; } = String.Empty;

        /// <summary>
        /// Recipient.
        /// </summary>
        public string UserId { get; set; } = String.Empty;

        /// <summary>
        /// Notification category, for example FeedPosts.
        /// </summary>
        public NotificationCategoryEnum Category { get; set; } = NotificationCategoryEnum.FeedPosts;

        /// <summary>
        /// Source item, for example a feed post identifier.
        /// </summary>
        public string SourceId { get; set; } = String.Empty;

        /// <summary>
        /// Status.
        /// </summary>
        public EmailSendStatusEnum Status { get; set; } = EmailSendStatusEnum.Sent;

        /// <summary>
        /// Created (UTC).
        /// </summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    }
}
