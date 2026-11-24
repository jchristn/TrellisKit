namespace TrellisKit.Core.Integrations.Models
{
    using System;

    /// <summary>
    /// Parameters for creating a Stripe coupon (POST /v1/coupons) for a discount sign-up code.
    /// </summary>
    public class StripeCouponRequest
    {
        #region Public-Members

        /// <summary>
        /// Coupon name shown in Checkout and on invoices.
        /// </summary>
        public string Name { get; set; } = String.Empty;

        /// <summary>
        /// Percent off (1 to 100), or null when <see cref="AmountOffCents"/> is set.
        /// </summary>
        public int? PercentOff { get; set; } = null;

        /// <summary>
        /// Amount off in the smallest currency unit, or null when <see cref="PercentOff"/> is set.
        /// </summary>
        public int? AmountOffCents { get; set; } = null;

        /// <summary>
        /// Currency of <see cref="AmountOffCents"/> (ISO 4217, lowercase).
        /// </summary>
        public string Currency { get; set; } = "usd";

        /// <summary>
        /// True for duration forever; false for once (the first invoice).
        /// </summary>
        public bool Forever { get; set; } = false;

        /// <summary>
        /// Sign-up code identifier stored as coupon metadata (signupCodeId).
        /// </summary>
        public string SignupCodeId { get; set; } = String.Empty;

        #endregion
    }
}
