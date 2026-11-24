namespace TrellisKit.Core.Integrations.Models
{
    using System;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// A Google Play subscription purchase (purchases.subscriptionsv2).
    /// </summary>
    public class GooglePlaySubscription
    {
        #region Public-Members

        /// <summary>
        /// Purchase token.
        /// </summary>
        public string PurchaseToken { get; set; } = String.Empty;

        /// <summary>
        /// Subscription product identifier.
        /// </summary>
        public string ProductId { get; set; } = String.Empty;

        /// <summary>
        /// Latest order identifier, or null.
        /// </summary>
        public string? LatestOrderId { get; set; } = null;

        /// <summary>
        /// Subscription state.
        /// </summary>
        public GooglePlaySubscriptionStateEnum State { get; set; } = GooglePlaySubscriptionStateEnum.Unspecified;

        /// <summary>
        /// Start time (UTC), or null.
        /// </summary>
        public DateTime? StartUtc { get; set; } = null;

        /// <summary>
        /// Expiry of the current period (UTC), or null.
        /// </summary>
        public DateTime? ExpiryUtc { get; set; } = null;

        /// <summary>
        /// Whether the subscription will renew.
        /// </summary>
        public bool AutoRenewing { get; set; } = false;

        /// <summary>
        /// Whether the purchase has been acknowledged.
        /// </summary>
        public bool Acknowledged { get; set; } = false;

        /// <summary>
        /// obfuscatedExternalAccountId set by the app, or null.
        /// </summary>
        public string? ObfuscatedAccountId { get; set; } = null;

        /// <summary>
        /// Token of the purchase this one replaces (upgrade or resubscribe), or null.
        /// </summary>
        public string? LinkedPurchaseToken { get; set; } = null;

        /// <summary>
        /// Production, or Sandbox for license-tester purchases.
        /// </summary>
        public StoreEnvironmentEnum Environment { get; set; } = StoreEnvironmentEnum.Production;

        /// <summary>
        /// Raw API response (unmanaged JSON).
        /// </summary>
        public string RawPayloadJson { get; set; } = String.Empty;

        #endregion
    }
}
