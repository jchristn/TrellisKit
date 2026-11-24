namespace TrellisKit.Core.Responses
{
    using System;

    /// <summary>
    /// GET /api/v1/auth/providers: what is available and the public identifiers clients need (never secrets).
    /// </summary>
    public class ProvidersResponse
    {
        #region Public-Members

        /// <summary>
        /// Email and password sign-in (always true).
        /// </summary>
        public bool Email { get; set; } = true;

        /// <summary>
        /// Apple.
        /// </summary>
        public AppleProviderInfo Apple { get; set; } = new AppleProviderInfo();

        /// <summary>
        /// Google.
        /// </summary>
        public GoogleProviderInfo Google { get; set; } = new GoogleProviderInfo();

        /// <summary>
        /// Facebook.
        /// </summary>
        public FacebookProviderInfo Facebook { get; set; } = new FacebookProviderInfo();

        /// <summary>
        /// Stripe Checkout.
        /// </summary>
        public ProviderToggle Stripe { get; set; } = new ProviderToggle();

        /// <summary>
        /// App Store purchases.
        /// </summary>
        public StoreProductInfo AppleIap { get; set; } = new StoreProductInfo();

        /// <summary>
        /// Google Play purchases.
        /// </summary>
        public StoreProductInfo GooglePlay { get; set; } = new StoreProductInfo();

        #endregion
    }
}
