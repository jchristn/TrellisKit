namespace TrellisKit.Core.Integrations.Models
{
    using System;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// A verified App Store transaction (decoded JWSTransaction).
    /// </summary>
    public class AppStoreTransaction
    {
        #region Public-Members

        /// <summary>
        /// Transaction identifier.
        /// </summary>
        public string TransactionId { get; set; } = String.Empty;

        /// <summary>
        /// Original transaction identifier; stable across renewals.
        /// </summary>
        public string OriginalTransactionId { get; set; } = String.Empty;

        /// <summary>
        /// Product identifier.
        /// </summary>
        public string ProductId { get; set; } = String.Empty;

        /// <summary>
        /// App bundle identifier.
        /// </summary>
        public string BundleId { get; set; } = String.Empty;

        /// <summary>
        /// Purchase time (UTC).
        /// </summary>
        public DateTime PurchaseUtc { get; set; } = DateTime.MinValue;

        /// <summary>
        /// Expiration time (UTC), or null for non-expiring products.
        /// </summary>
        public DateTime? ExpiresUtc { get; set; } = null;

        /// <summary>
        /// Revocation (refund) time (UTC), or null.
        /// </summary>
        public DateTime? RevocationUtc { get; set; } = null;

        /// <summary>
        /// appAccountToken set by the app at purchase, or null.
        /// </summary>
        public Guid? AppAccountToken { get; set; } = null;

        /// <summary>
        /// Production or Sandbox.
        /// </summary>
        public StoreEnvironmentEnum Environment { get; set; } = StoreEnvironmentEnum.Production;

        /// <summary>
        /// Decoded JWS payload, kept for audit (unmanaged JSON).
        /// </summary>
        public string RawPayloadJson { get; set; } = String.Empty;

        #endregion
    }
}
