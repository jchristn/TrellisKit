namespace TrellisKit.Core.Enums
{
    /// <summary>
    /// Purpose of a single-use emailed token.
    /// </summary>
    public enum OneTimeTokenPurposeEnum
    {
        /// <summary>
        /// Email address verification (48 hours).
        /// </summary>
        VerifyEmail,

        /// <summary>
        /// Password reset (1 hour).
        /// </summary>
        ResetPassword
    }
}
