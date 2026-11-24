namespace TrellisKit.Core.Models
{
    using System;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// One store subscription (Apple original transaction, Google purchase token, or Stripe subscription).
    /// </summary>
    public class Subscription
    {
        #region Public-Members

        /// <summary>
        /// Identifier (prefix sub_).
        /// </summary>
        public string Id { get; set; } = String.Empty;

        /// <summary>
        /// Member identifier.
        /// </summary>
        public string UserId { get; set; } = String.Empty;

        /// <summary>
        /// Store.
        /// </summary>
        public StoreEnum Store { get; set; } = StoreEnum.Stripe;

        /// <summary>
        /// Store identifier: Apple originalTransactionId, Google purchase token, or Stripe subscription id. Unique per store.
        /// </summary>
        public string StoreSubscriptionId { get; set; } = String.Empty;

        /// <summary>
        /// Product or price identifier.
        /// </summary>
        public string ProductId { get; set; } = String.Empty;

        /// <summary>
        /// Normalized status.
        /// </summary>
        public SubscriptionStatusEnum Status { get; set; } = SubscriptionStatusEnum.Active;

        /// <summary>
        /// Current period start (UTC), or null.
        /// </summary>
        public DateTime? CurrentPeriodStartUtc { get; set; } = null;

        /// <summary>
        /// Current period end (UTC), or null.
        /// </summary>
        public DateTime? CurrentPeriodEndUtc { get; set; } = null;

        /// <summary>
        /// Whether it renews at the end of the period.
        /// </summary>
        public bool AutoRenew { get; set; } = false;

        /// <summary>
        /// Production or Sandbox.
        /// </summary>
        public StoreEnvironmentEnum Environment { get; set; } = StoreEnvironmentEnum.Production;

        /// <summary>
        /// Latest store transaction or order identifier, or null.
        /// </summary>
        public string? LatestTransactionId { get; set; } = null;

        /// <summary>
        /// Created (UTC).
        /// </summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Last updated (UTC).
        /// </summary>
        public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Last webhook, verification, or reconciliation (UTC), or null.
        /// </summary>
        public DateTime? LastEventUtc { get; set; } = null;

        #endregion
    }
}
