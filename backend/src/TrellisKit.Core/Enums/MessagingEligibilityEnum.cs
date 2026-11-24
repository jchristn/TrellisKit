namespace TrellisKit.Core.Enums
{
    /// <summary>
    /// Outcome of the direct message eligibility check (docs/PHASE5_CONTRACT.md section 3.2).
    /// </summary>
    public enum MessagingEligibilityEnum
    {
        /// <summary>
        /// The sender may send.
        /// </summary>
        Allowed,

        /// <summary>
        /// 403 MessagingNotAllowed (blocks, policy, declined request, canSendDirectMessages).
        /// </summary>
        NotAllowed,

        /// <summary>
        /// 403 RecipientUnavailable.
        /// </summary>
        RecipientUnavailable,

        /// <summary>
        /// 403 MessagingRestricted (muted, new account limits).
        /// </summary>
        Restricted,

        /// <summary>
        /// The sender has no messaging access (402 MembershipRequired).
        /// </summary>
        SenderNoAccess
    }
}
