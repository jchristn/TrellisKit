namespace TrellisKit.Core.Settings
{
    using System;

    /// <summary>
    /// Google Play settings for in-app subscriptions. An empty package name disables Google Play verification.
    /// </summary>
    public class GooglePlaySettings
    {
        #region Public-Members

        /// <summary>
        /// Android package name, for example com.example.app.
        /// </summary>
        public string PackageName { get; set; } = String.Empty;

        /// <summary>
        /// Subscription product id. Default empty: use the property membership.storeProductId.
        /// </summary>
        public string ProductId { get; set; } = String.Empty;

        /// <summary>
        /// Service account key JSON with Android Publisher access. Secret.
        /// </summary>
        public string ServiceAccountJson { get; set; } = String.Empty;

        /// <summary>
        /// Expected audience of Pub/Sub push tokens (the webhook URL).
        /// </summary>
        public string PubSubAudience { get; set; } = String.Empty;

        /// <summary>
        /// Service account email that signs Pub/Sub push tokens.
        /// </summary>
        public string PubSubServiceAccountEmail { get; set; } = String.Empty;

        #endregion
    }
}
