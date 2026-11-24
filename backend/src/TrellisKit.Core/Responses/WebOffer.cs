namespace TrellisKit.Core.Responses
{
    using System;

    /// <summary>
    /// The Stripe membership offer.
    /// </summary>
    public class WebOffer
    {
        #region Public-Members

        /// <summary>
        /// Price in US dollars.
        /// </summary>
        public decimal PriceUsd { get; set; } = 0m;

        /// <summary>
        /// Billing interval.
        /// </summary>
        public string Interval { get; set; } = "Year";

        /// <summary>
        /// Product name.
        /// </summary>
        public string ProductName { get; set; } = String.Empty;

        #endregion
    }
}
