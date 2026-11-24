namespace TrellisKit.Core.PropertyConfig
{
    using System;

    /// <summary>
    /// The membership product offered by the property.
    /// </summary>
    public class PropertyMembership
    {
        #region Public-Members

        /// <summary>
        /// Membership product display name. Required.
        /// </summary>
        public string ProductName { get; set; } = String.Empty;

        /// <summary>
        /// Price per interval in US cents, shown as the web offer and used for estimated ARR. Must be positive. The charged price is set in each store.
        /// </summary>
        public int PriceUsdCents { get; set; } = 0;

        /// <summary>
        /// Billing interval: Year or Month. Default Year.
        /// </summary>
        public string Interval { get; set; } = "Year";

        /// <summary>
        /// Default App Store and Google Play subscription product id. Required. The appStore.productId and googlePlay.productId settings override it.
        /// </summary>
        public string StoreProductId { get; set; } = String.Empty;

        /// <summary>
        /// Minimum member age in years. Default 18. Minimum 13, maximum 120.
        /// </summary>
        public int MinimumAge { get; set; } = 18;

        /// <summary>
        /// Free trial days. Default 0. Minimum 0, maximum 365.
        /// </summary>
        public int TrialDays { get; set; } = 0;

        #endregion
    }
}
