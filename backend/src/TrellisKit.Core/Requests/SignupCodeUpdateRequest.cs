namespace TrellisKit.Core.Requests
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Body of PUT /api/v1/admin/signup-codes/{id}. Only present properties change; code and grant fields only while no redemptions exist.
    /// </summary>
    public class SignupCodeUpdateRequest
    {
        #region Public-Members

        /// <summary>
        /// Code.
        /// </summary>
        public string? Code { get; set; } = null;

        /// <summary>
        /// Name.
        /// </summary>
        public string? Name { get; set; } = null;

        /// <summary>
        /// Description.
        /// </summary>
        public string? Description { get; set; } = null;

        /// <summary>
        /// Notes.
        /// </summary>
        public string? Notes { get; set; } = null;

        /// <summary>
        /// Grant type.
        /// </summary>
        public string? GrantType { get; set; } = null;

        /// <summary>
        /// Months.
        /// </summary>
        public int? GrantMonths { get; set; } = null;

        /// <summary>
        /// Grant end.
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
        /// Maximum redemptions (null for unlimited).
        /// </summary>
        public int? MaxRedemptions { get; set; } = null;

        /// <summary>
        /// Start.
        /// </summary>
        public DateTime? StartsUtc { get; set; } = null;

        /// <summary>
        /// Expiry.
        /// </summary>
        public DateTime? ExpiresUtc { get; set; } = null;

        /// <summary>
        /// Active flag.
        /// </summary>
        public bool? IsActive { get; set; } = null;

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
