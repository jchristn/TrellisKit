namespace TrellisKit.Core.Models
{
    using System;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// A batch of generated single-use sign-up codes.
    /// </summary>
    public class SignupCodeBatch
    {
        #region Public-Members

        /// <summary>
        /// Identifier (prefix scb_).
        /// </summary>
        public string Id { get; set; } = String.Empty;

        /// <summary>
        /// Batch name (also each code's name).
        /// </summary>
        public string Name { get; set; } = String.Empty;

        /// <summary>
        /// Codes generated. Minimum 0.
        /// </summary>
        public int Quantity
        {
            get { return _Quantity; }
            set { _Quantity = Math.Max(0, value); }
        }

        /// <summary>
        /// Grant type.
        /// </summary>
        public GrantTypeEnum GrantType { get; set; } = GrantTypeEnum.DurationMonths;

        /// <summary>
        /// Months for DurationMonths, otherwise null.
        /// </summary>
        public int? GrantMonths { get; set; } = null;

        /// <summary>
        /// End for FixedEndDate, otherwise null.
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
        /// Last moment the codes can be redeemed (UTC), or null.
        /// </summary>
        public DateTime? ExpiresUtc { get; set; } = null;

        /// <summary>
        /// Administrator who created it, or null.
        /// </summary>
        public string? CreatedByUserId { get; set; } = null;

        /// <summary>
        /// Created (UTC).
        /// </summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        #endregion

        #region Private-Members

        private int _Quantity = 0;

        #endregion
    }
}
