namespace TrellisKit.Core.Models
{
    using System;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// One recipient of a broadcast (table broadcastdeliveries, unique per broadcast and member), with one status per
    /// channel.
    /// </summary>
    public class BroadcastDelivery
    {
        #region Public-Members

        /// <summary>
        /// Identifier (bdl_).
        /// </summary>
        public string Id { get; set; } = String.Empty;

        /// <summary>
        /// Broadcast.
        /// </summary>
        public string BroadcastId { get; set; } = String.Empty;

        /// <summary>
        /// Recipient.
        /// </summary>
        public string UserId { get; set; } = String.Empty;

        /// <summary>
        /// In-app status.
        /// </summary>
        public BroadcastDeliveryStatusEnum InAppStatus { get; set; } = BroadcastDeliveryStatusEnum.NotRequested;

        /// <summary>
        /// Push status.
        /// </summary>
        public BroadcastDeliveryStatusEnum PushStatus { get; set; } = BroadcastDeliveryStatusEnum.NotRequested;

        /// <summary>
        /// Email status.
        /// </summary>
        public BroadcastDeliveryStatusEnum EmailStatus { get; set; } = BroadcastDeliveryStatusEnum.NotRequested;

        /// <summary>
        /// The in-app notification, or null.
        /// </summary>
        public string? NotificationId { get; set; } = null;

        /// <summary>
        /// The queued email message, or null.
        /// </summary>
        public string? EmailMessageId { get; set; } = null;

        /// <summary>
        /// When the email was handed to the queue (the per-minute throttle counts these), or null.
        /// </summary>
        public DateTime? EmailQueuedUtc { get; set; } = null;

        /// <summary>
        /// Created.
        /// </summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Updated.
        /// </summary>
        public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;

        #endregion
    }
}
