namespace TrellisKit.Core.Enums
{
    /// <summary>
    /// Derived status of a sign-up code redemption.
    /// </summary>
    public enum RedemptionStatusEnum
    {
        /// <summary>
        /// The free membership has not ended.
        /// </summary>
        Active,

        /// <summary>
        /// The free membership ended.
        /// </summary>
        Expired,

        /// <summary>
        /// An administrator revoked it.
        /// </summary>
        Revoked
    }
}
