namespace TrellisKit.Core.Requests
{
    using System;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// Sign-up code redemption list filter.
    /// </summary>
    public class RedemptionFilter
    {
        #region Public-Members

        /// <summary>
        /// Sign-up code, or null.
        /// </summary>
        public string? SignupCodeId { get; set; } = null;

        /// <summary>
        /// Member, or null.
        /// </summary>
        public string? UserId { get; set; } = null;

        /// <summary>
        /// Derived status, or null.
        /// </summary>
        public RedemptionStatusEnum? Status { get; set; } = null;

        #endregion
    }
}
