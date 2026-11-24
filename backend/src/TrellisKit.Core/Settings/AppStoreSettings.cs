namespace TrellisKit.Core.Settings
{
    using System;

    /// <summary>
    /// Apple App Store settings for in-app subscriptions. An empty bundle id disables App Store verification.
    /// </summary>
    public class AppStoreSettings
    {
        #region Public-Members

        /// <summary>
        /// iOS bundle identifier, for example com.example.app.
        /// </summary>
        public string BundleId { get; set; } = String.Empty;

        /// <summary>
        /// Auto-renewable subscription product id. Default empty: use the property membership.storeProductId.
        /// </summary>
        public string ProductId { get; set; } = String.Empty;

        /// <summary>
        /// Numeric Apple ID of the app (required for production notification verification).
        /// </summary>
        public long AppAppleId { get; set; } = 0;

        /// <summary>
        /// App Store Connect API issuer id.
        /// </summary>
        public string IssuerId { get; set; } = String.Empty;

        /// <summary>
        /// App Store Server API key id.
        /// </summary>
        public string KeyId { get; set; } = String.Empty;

        /// <summary>
        /// App Store Server API private key (.p8 contents). Secret.
        /// </summary>
        public string PrivateKeyPem { get; set; } = String.Empty;

        /// <summary>
        /// Whether sandbox transactions are accepted (true for development and TestFlight).
        /// </summary>
        public bool AllowSandbox { get; set; } = true;

        #endregion
    }
}
