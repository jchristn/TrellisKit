namespace TrellisKit.Core.Models
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// An admin-managed code that grants a free membership.
    /// </summary>
    public class SignupCode
    {
        #region Public-Members

        /// <summary>
        /// Identifier (prefix suc_).
        /// </summary>
        public string Id { get; set; } = String.Empty;

        /// <summary>
        /// Code, upper case, unique case-insensitively.
        /// </summary>
        public string Code { get; set; } = String.Empty;

        /// <summary>
        /// Campaign label shown to admins and members.
        /// </summary>
        public string Name { get; set; } = String.Empty;

        /// <summary>
        /// Description shown to members, or null.
        /// </summary>
        public string? Description { get; set; } = null;

        /// <summary>
        /// Admin-only notes, or null.
        /// </summary>
        public string? Notes { get; set; } = null;

        /// <summary>
        /// Grant type.
        /// </summary>
        public GrantTypeEnum GrantType { get; set; } = GrantTypeEnum.DurationMonths;

        /// <summary>
        /// Months granted for DurationMonths, otherwise null.
        /// </summary>
        public int? GrantMonths { get; set; } = null;

        /// <summary>
        /// End of the free membership for FixedEndDate, otherwise null.
        /// </summary>
        public DateTime? GrantEndUtc { get; set; } = null;

        /// <summary>
        /// Discount: percent off (1 to 100), or null. Exactly one of this and <see cref="DiscountAmountCents"/> is set for
        /// <see cref="GrantTypeEnum.Discount"/>.
        /// </summary>
        public int? DiscountPercent { get; set; } = null;

        /// <summary>
        /// Discount: amount off in the smallest currency unit (cents), or null.
        /// </summary>
        public int? DiscountAmountCents { get; set; } = null;

        /// <summary>
        /// Discount: how long the discount applies, or null for other grant types.
        /// </summary>
        public DiscountDurationEnum? DiscountDuration { get; set; } = null;

        /// <summary>
        /// Stripe coupon created for this discount code on its first checkout (server managed), or null.
        /// </summary>
        public string? StripeCouponId { get; set; } = null;

        /// <summary>
        /// Maximum redemptions, or null for unlimited.
        /// </summary>
        public int? MaxRedemptions { get; set; } = null;

        /// <summary>
        /// Redemptions so far. Minimum 0.
        /// </summary>
        public int RedemptionCount
        {
            get { return _RedemptionCount; }
            set { _RedemptionCount = Math.Max(0, value); }
        }

        /// <summary>
        /// First moment the code can be redeemed (UTC), or null.
        /// </summary>
        public DateTime? StartsUtc { get; set; } = null;

        /// <summary>
        /// Last moment the code can be redeemed (UTC), or null.
        /// </summary>
        public DateTime? ExpiresUtc { get; set; } = null;

        /// <summary>
        /// Whether the code accepts redemptions.
        /// </summary>
        public bool IsActive { get; set; } = true;

        /// <summary>
        /// Batch identifier for generated single-use codes, or null.
        /// </summary>
        public string? BatchId { get; set; } = null;

        /// <summary>
        /// Administrator who created it, or null for the admin token.
        /// </summary>
        public string? CreatedByUserId { get; set; } = null;

        /// <summary>
        /// Created (UTC).
        /// </summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Last updated (UTC).
        /// </summary>
        public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Last redemption (UTC), or null.
        /// </summary>
        public DateTime? LastRedeemedUtc { get; set; } = null;

        #endregion

        #region Private-Members

        private int _RedemptionCount = 0;

        #endregion

        #region Public-Methods

        /// <summary>
        /// Derived status at a point in time: Inactive, Scheduled, Expired, Exhausted, then Active.
        /// </summary>
        /// <param name="nowUtc">Time (UTC).</param>
        /// <returns>Status.</returns>
        public SignupCodeStatusEnum StatusAt(DateTime nowUtc)
        {
            if (!IsActive) return SignupCodeStatusEnum.Inactive;
            if (StartsUtc != null && StartsUtc.Value > nowUtc) return SignupCodeStatusEnum.Scheduled;
            if (ExpiresUtc != null && ExpiresUtc.Value <= nowUtc) return SignupCodeStatusEnum.Expired;
            if (MaxRedemptions != null && RedemptionCount >= MaxRedemptions.Value) return SignupCodeStatusEnum.Exhausted;
            return SignupCodeStatusEnum.Active;
        }

        #endregion
    }
}
