namespace TrellisKit.Core.Integrations.Models
{
    using System;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// A verified App Store Server Notification V2.
    /// </summary>
    public class AppStoreNotification
    {
        #region Public-Members

        /// <summary>
        /// Unique notification identifier, used for idempotency.
        /// </summary>
        public string NotificationUuid { get; set; } = String.Empty;

        /// <summary>
        /// notificationType, for example DID_RENEW or EXPIRED.
        /// </summary>
        public string NotificationType { get; set; } = String.Empty;

        /// <summary>
        /// subtype, or null.
        /// </summary>
        public string? Subtype { get; set; } = null;

        /// <summary>
        /// Production or Sandbox.
        /// </summary>
        public StoreEnvironmentEnum Environment { get; set; } = StoreEnvironmentEnum.Production;

        /// <summary>
        /// Decoded signedTransactionInfo, or null when absent.
        /// </summary>
        public AppStoreTransaction? Transaction { get; set; } = null;

        /// <summary>
        /// autoRenewStatus from signedRenewalInfo, or null when absent.
        /// </summary>
        public bool? AutoRenewEnabled { get; set; } = null;

        /// <summary>
        /// Decoded notification payload (unmanaged JSON).
        /// </summary>
        public string RawPayloadJson { get; set; } = String.Empty;

        #endregion
    }
}
