namespace TrellisKit.Core.Models
{
    using System;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// Append-only record of a store webhook, client verification, or reconciliation. The raw payload is unmanaged JSON kept for forensics; every field the application reads is promoted to a column.
    /// </summary>
    public class SubscriptionEvent
    {
        #region Public-Members

        /// <summary>
        /// Identifier (prefix sev_).
        /// </summary>
        public string Id { get; set; } = String.Empty;

        /// <summary>
        /// Subscription identifier, or null when it could not be matched.
        /// </summary>
        public string? SubscriptionId { get; set; } = null;

        /// <summary>
        /// Member identifier, or null when unknown.
        /// </summary>
        public string? UserId { get; set; } = null;

        /// <summary>
        /// Store.
        /// </summary>
        public StoreEnum Store { get; set; } = StoreEnum.Stripe;

        /// <summary>
        /// Store event identifier (Stripe event id, Apple notificationUUID, Pub/Sub message id), or a generated id for verify and reconcile events. Unique per store.
        /// </summary>
        public string StoreEventId { get; set; } = String.Empty;

        /// <summary>
        /// How the event arrived.
        /// </summary>
        public SubscriptionEventSourceEnum Source { get; set; } = SubscriptionEventSourceEnum.Webhook;

        /// <summary>
        /// Store event type, for example customer.subscription.updated or DID_RENEW.
        /// </summary>
        public string EventType { get; set; } = String.Empty;

        /// <summary>
        /// Received (UTC).
        /// </summary>
        public DateTime ReceivedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Processed (UTC), or null.
        /// </summary>
        public DateTime? ProcessedUtc { get; set; } = null;

        /// <summary>
        /// Outcome: Received, Processed, Ignored, or Failed.
        /// </summary>
        public string Outcome { get; set; } = "Received";

        /// <summary>
        /// Unmanaged raw store payload, or null.
        /// </summary>
        public string? RawPayloadJson { get; set; } = null;

        #endregion
    }
}
