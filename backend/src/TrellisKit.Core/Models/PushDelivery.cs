namespace TrellisKit.Core.Models
{
    using System;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// A row of pushdeliveries: one push message to one token, with its Expo ticket.
    /// </summary>
    public class PushDelivery
    {
        /// <summary>
        /// Identifier (pdl_).
        /// </summary>
        public string Id { get; set; } = String.Empty;

        /// <summary>
        /// Notification.
        /// </summary>
        public string NotificationId { get; set; } = String.Empty;

        /// <summary>
        /// Push token.
        /// </summary>
        public string PushTokenId { get; set; } = String.Empty;

        /// <summary>
        /// Expo ticket identifier, or null when the send failed.
        /// </summary>
        public string? TicketId { get; set; } = null;

        /// <summary>
        /// Status.
        /// </summary>
        public PushDeliveryStatusEnum Status { get; set; } = PushDeliveryStatusEnum.Queued;

        /// <summary>
        /// Error code from Expo (for example DeviceNotRegistered), or null.
        /// </summary>
        public string? Error { get; set; } = null;

        /// <summary>
        /// Created (UTC).
        /// </summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// When the receipt was checked, or null.
        /// </summary>
        public DateTime? ReceiptCheckedUtc { get; set; } = null;
    }
}
