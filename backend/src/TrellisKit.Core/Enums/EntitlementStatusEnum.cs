namespace TrellisKit.Core.Enums
{
    /// <summary>
    /// Status of one membership entitlement in the member's history.
    /// </summary>
    public enum EntitlementStatusEnum
    {
        /// <summary>
        /// Currently granting membership.
        /// </summary>
        Active,

        /// <summary>
        /// Starts in the future.
        /// </summary>
        Scheduled,

        /// <summary>
        /// Ended.
        /// </summary>
        Expired,

        /// <summary>
        /// Revoked before it ended.
        /// </summary>
        Revoked
    }
}
