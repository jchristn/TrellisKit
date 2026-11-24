namespace TrellisKit.Core.Requests
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Body of POST /api/v1/admin/signup-codes.
    /// </summary>
    public class SignupCodeCreateRequest
    {
        #region Public-Members

        /// <summary>
        /// Code, 6 to 40 of A-Z 0-9 and hyphen; omit to generate.
        /// </summary>
        public string? Code { get; set; } = null;

        /// <summary>
        /// Campaign name (required).
        /// </summary>
        public string? Name { get; set; } = null;

        /// <summary>
        /// Shown to members, up to 500 characters.
        /// </summary>
        public string? Description { get; set; } = null;

        /// <summary>
        /// Admin notes, up to 2000 characters.
        /// </summary>
        public string? Notes { get; set; } = null;

        /// <summary>
        /// DurationMonths, FixedEndDate, or Lifetime.
        /// </summary>
        public string? GrantType { get; set; } = null;

        /// <summary>
        /// Months for DurationMonths, 1 to 120.
        /// </summary>
        public int? GrantMonths { get; set; } = null;

        /// <summary>
        /// End for FixedEndDate.
        /// </summary>
        public DateTime? GrantEndUtc { get; set; } = null;

        /// <summary>
        /// Discount grant: percent off (1 to 100). Set this or <see cref="DiscountAmountCents"/>.
        /// </summary>
        public int? DiscountPercent { get; set; } = null;

        /// <summary>
        /// Discount grant: amount off in cents (USD).
        /// </summary>
        public int? DiscountAmountCents { get; set; } = null;

        /// <summary>
        /// Discount grant: Once (first billing period) or Forever (every renewal).
        /// </summary>
        public string? DiscountDuration { get; set; } = null;

        /// <summary>
        /// Maximum redemptions, or null for unlimited.
        /// </summary>
        public int? MaxRedemptions { get; set; } = null;

        /// <summary>
        /// First redeemable moment, or null.
        /// </summary>
        public DateTime? StartsUtc { get; set; } = null;

        /// <summary>
        /// Last redeemable moment, or null.
        /// </summary>
        public DateTime? ExpiresUtc { get; set; } = null;

        /// <summary>
        /// Labels (replace the stored list when present; omitted means unchanged). At most 50.
        /// </summary>
        public List<string>? Labels { get; set; } = null;

        /// <summary>
        /// Tags (replace the stored dictionary when present; omitted means unchanged). At most 50 keys.
        /// </summary>
        public Dictionary<string, string>? Tags { get; set; } = null;

        #endregion
    }
}
