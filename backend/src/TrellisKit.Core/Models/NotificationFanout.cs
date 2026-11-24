namespace TrellisKit.Core.Models
{
    using System;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// A row of notificationfanouts: a background job that notifies many members about one source item.
    /// </summary>
    public class NotificationFanout
    {
        /// <summary>
        /// Identifier (fnj_).
        /// </summary>
        public string Id { get; set; } = String.Empty;

        /// <summary>
        /// Notification type.
        /// </summary>
        public NotificationTypeEnum Type { get; set; } = NotificationTypeEnum.NewFeedPost;

        /// <summary>
        /// Source item, for example a feed post identifier.
        /// </summary>
        public string SourceId { get; set; } = String.Empty;

        /// <summary>
        /// Keyset position: the last recipient processed, or null before the first batch.
        /// </summary>
        public string? LastUserId { get; set; } = null;

        /// <summary>
        /// Status.
        /// </summary>
        public FanoutStatusEnum Status { get; set; } = FanoutStatusEnum.Pending;

        /// <summary>
        /// Notifications created so far.
        /// </summary>
        public int CreatedCount { get; set; } = 0;

        /// <summary>
        /// Worker instance holding the claim, or null.
        /// </summary>
        public string? ClaimedBy { get; set; } = null;

        /// <summary>
        /// Claim expiry, or null.
        /// </summary>
        public DateTime? ClaimUntilUtc { get; set; } = null;

        /// <summary>
        /// Created (UTC).
        /// </summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Completed (UTC), or null.
        /// </summary>
        public DateTime? CompletedUtc { get; set; } = null;
    }
}
