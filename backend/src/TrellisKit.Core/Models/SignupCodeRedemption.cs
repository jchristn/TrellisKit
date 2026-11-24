namespace TrellisKit.Core.Models
{
    using System;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// One member's redemption of a sign-up code.
    /// </summary>
    public class SignupCodeRedemption
    {
        #region Public-Members

        /// <summary>
        /// Identifier (prefix scr_).
        /// </summary>
        public string Id { get; set; } = String.Empty;

        /// <summary>
        /// Sign-up code identifier.
        /// </summary>
        public string SignupCodeId { get; set; } = String.Empty;

        /// <summary>
        /// Member identifier.
        /// </summary>
        public string UserId { get; set; } = String.Empty;

        /// <summary>
        /// Redeemed (UTC).
        /// </summary>
        public DateTime RedeemedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Start of the free membership (UTC); later than RedeemedUtc when a paid membership was already active.
        /// </summary>
        public DateTime MembershipStartsUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// End of the free membership (UTC), or null for lifetime.
        /// </summary>
        public DateTime? MembershipEndsUtc { get; set; } = null;

        /// <summary>
        /// Entitlement created by the redemption.
        /// </summary>
        public string EntitlementId { get; set; } = String.Empty;

        /// <summary>
        /// Source IP, or null.
        /// </summary>
        public string? SourceIp { get; set; } = null;

        /// <summary>
        /// User agent (truncated to 512 characters), or null.
        /// </summary>
        public string? UserAgent { get; set; } = null;

        /// <summary>
        /// Whether the code was redeemed during sign-up rather than by an existing member.
        /// </summary>
        public bool IsNewAccount { get; set; } = false;

        /// <summary>
        /// When revoked (UTC), or null.
        /// </summary>
        public DateTime? RevokedUtc { get; set; } = null;

        /// <summary>
        /// Administrator who revoked it, or null.
        /// </summary>
        public string? RevokedByUserId { get; set; } = null;

        /// <summary>
        /// Why it was revoked, or null.
        /// </summary>
        public string? RevokeReason { get; set; } = null;

        /// <summary>
        /// Expiry reminders sent: 0 none, 1 the first reminder, 2 the final reminder. Minimum 0, maximum 2.
        /// </summary>
        public int ReminderStage
        {
            get { return _ReminderStage; }
            set { _ReminderStage = Math.Clamp(value, 0, 2); }
        }

        /// <summary>
        /// Derived when read: whether the member started a paid subscription after redeeming.
        /// </summary>
        public bool ConvertedToPaid { get; set; } = false;

        #endregion

        #region Private-Members

        private int _ReminderStage = 0;

        #endregion

        #region Public-Methods

        /// <summary>
        /// Derived status at a point in time.
        /// </summary>
        /// <param name="nowUtc">Time (UTC).</param>
        /// <returns>Status.</returns>
        public RedemptionStatusEnum StatusAt(DateTime nowUtc)
        {
            if (RevokedUtc != null) return RedemptionStatusEnum.Revoked;
            if (MembershipEndsUtc != null && MembershipEndsUtc.Value <= nowUtc) return RedemptionStatusEnum.Expired;
            return RedemptionStatusEnum.Active;
        }

        #endregion
    }
}
