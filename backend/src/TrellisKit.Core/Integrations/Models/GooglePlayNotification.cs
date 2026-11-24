namespace TrellisKit.Core.Integrations.Models
{
    using System;

    /// <summary>
    /// A Real-Time Developer Notification delivered by Pub/Sub push.
    /// </summary>
    public class GooglePlayNotification
    {
        #region Public-Members

        /// <summary>
        /// Pub/Sub message identifier, used for idempotency.
        /// </summary>
        public string MessageId { get; set; } = String.Empty;

        /// <summary>
        /// App package name.
        /// </summary>
        public string PackageName { get; set; } = String.Empty;

        /// <summary>
        /// Subscription purchase token, or null for test notifications.
        /// </summary>
        public string? PurchaseToken { get; set; } = null;

        /// <summary>
        /// Subscription product identifier, or null.
        /// </summary>
        public string? SubscriptionId { get; set; } = null;

        /// <summary>
        /// subscriptionNotification.notificationType (for example 2 RENEWED, 13 EXPIRED), or 0.
        /// </summary>
        public int NotificationType { get; set; } = 0;

        /// <summary>
        /// Whether this is a test notification from the Play Console.
        /// </summary>
        public bool IsTest { get; set; } = false;

        /// <summary>
        /// Decoded developer notification (unmanaged JSON).
        /// </summary>
        public string RawPayloadJson { get; set; } = String.Empty;

        #endregion
    }
}
