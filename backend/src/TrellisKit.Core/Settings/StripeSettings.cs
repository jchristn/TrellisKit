namespace TrellisKit.Core.Settings
{
    using System;

    /// <summary>
    /// Stripe settings for web subscriptions. An empty secret key disables Stripe.
    /// </summary>
    public class StripeSettings
    {
        #region Public-Members

        /// <summary>
        /// Restricted or secret API key. Secret.
        /// </summary>
        public string SecretKey { get; set; } = String.Empty;

        /// <summary>
        /// Webhook signing secret (whsec_...). Secret.
        /// </summary>
        public string WebhookSecret { get; set; } = String.Empty;

        /// <summary>
        /// Price id of the membership.
        /// </summary>
        public string PriceId { get; set; } = String.Empty;

        /// <summary>
        /// Stripe API base URL. Override only for tests.
        /// </summary>
        public string ApiBaseUrl { get; set; } = "https://api.stripe.com";

        #endregion
    }
}
