namespace TrellisKit.Core.Enums
{
    /// <summary>
    /// Derived status of a sign-up code.
    /// </summary>
    public enum SignupCodeStatusEnum
    {
        /// <summary>
        /// Redeemable now.
        /// </summary>
        Active,

        /// <summary>
        /// Not redeemable until startsUtc.
        /// </summary>
        Scheduled,

        /// <summary>
        /// Past expiresUtc.
        /// </summary>
        Expired,

        /// <summary>
        /// Every allowed redemption is used.
        /// </summary>
        Exhausted,

        /// <summary>
        /// Deactivated by an administrator.
        /// </summary>
        Inactive
    }
}
