namespace TrellisKit.Core.Responses
{
    using System;

    /// <summary>
    /// POST /api/v1/signup-codes/redeem.
    /// </summary>
    public class SignupCodeRedeemResponse
    {
        #region Public-Members

        /// <summary>
        /// Effective membership after redeeming.
        /// </summary>
        public MembershipSummary Membership { get; set; } = new MembershipSummary();

        /// <summary>
        /// What the code granted.
        /// </summary>
        public string GrantSummary { get; set; } = String.Empty;

        /// <summary>
        /// When the free membership ends, or null for lifetime.
        /// </summary>
        public DateTime? MembershipEndsUtc { get; set; } = null;

        #endregion
    }
}
