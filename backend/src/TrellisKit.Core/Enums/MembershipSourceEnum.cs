namespace TrellisKit.Core.Enums
{
    /// <summary>
    /// Where a membership entitlement comes from.
    /// </summary>
    public enum MembershipSourceEnum
    {
        /// <summary>
        /// A paid store subscription (Apple, Google, or Stripe).
        /// </summary>
        Subscription,

        /// <summary>
        /// A redeemed sign-up code.
        /// </summary>
        SignupCode,

        /// <summary>
        /// A free membership granted by an administrator.
        /// </summary>
        AdminComp
    }
}
