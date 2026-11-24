namespace TrellisKit.Core.Responses
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// A sign-up code as shown to admins.
    /// </summary>
    public class SignupCodeView : IMetadataPayload
    {
        #region Public-Members

        /// <summary>
        /// Identifier.
        /// </summary>
        public string Id { get; set; } = String.Empty;

        /// <summary>
        /// Code.
        /// </summary>
        public string Code { get; set; } = String.Empty;

        /// <summary>
        /// Name.
        /// </summary>
        public string Name { get; set; } = String.Empty;

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
        public GrantTypeEnum GrantType { get; set; } = GrantTypeEnum.DurationMonths;

        /// <summary>
        /// Months.
        /// </summary>
        public int? GrantMonths { get; set; } = null;

        /// <summary>
        /// Grant end.
        /// </summary>
        public DateTime? GrantEndUtc { get; set; } = null;

        /// <summary>
        /// Discount: percent off, or null.
        /// </summary>
        public int? DiscountPercent { get; set; } = null;

        /// <summary>
        /// Discount: amount off in cents (USD), or null.
        /// </summary>
        public int? DiscountAmountCents { get; set; } = null;

        /// <summary>
        /// Discount: how long it applies, or null for free-membership codes.
        /// </summary>
        public DiscountDurationEnum? DiscountDuration { get; set; } = null;

        /// <summary>
        /// Stripe coupon created for the discount on its first checkout, or null.
        /// </summary>
        public string? StripeCouponId { get; set; } = null;

        /// <summary>
        /// Grant in words.
        /// </summary>
        public string GrantSummary { get; set; } = String.Empty;

        /// <summary>
        /// Maximum, or null for unlimited.
        /// </summary>
        public int? MaxRedemptions { get; set; } = null;

        /// <summary>
        /// Redemptions.
        /// </summary>
        public int RedemptionCount { get; set; } = 0;

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
        public bool IsActive { get; set; } = true;

        /// <summary>
        /// Derived status.
        /// </summary>
        public SignupCodeStatusEnum Status { get; set; } = SignupCodeStatusEnum.Active;

        /// <summary>
        /// Batch.
        /// </summary>
        public string? BatchId { get; set; } = null;

        /// <summary>
        /// Shareable join link.
        /// </summary>
        public string JoinUrl { get; set; } = String.Empty;

        /// <summary>
        /// Last redemption.
        /// </summary>
        public DateTime? LastRedeemedUtc { get; set; } = null;

        /// <summary>
        /// Creator.
        /// </summary>
        public string? CreatedByUserId { get; set; } = null;

        /// <summary>
        /// Created.
        /// </summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Updated.
        /// </summary>
        public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Labels visible to the caller (staff: entries only for staff). Never null.
        /// </summary>
        public List<string> Labels
        {
            get { return _Labels; }
            set { _Labels = value ?? new List<string>(); }
        }

        /// <summary>
        /// Tags visible to the caller (staff: keys only for staff). Never null.
        /// </summary>
        public Dictionary<string, string> Tags
        {
            get { return _Tags; }
            set { _Tags = value ?? new Dictionary<string, string>(); }
        }

        #endregion

        #region Private-Members

        private List<string> _Labels = new List<string>();
        private Dictionary<string, string> _Tags = new Dictionary<string, string>();

        #endregion
    }
}
