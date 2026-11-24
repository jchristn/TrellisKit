namespace TrellisKit.Core.Enums
{
    /// <summary>
    /// Admin member list membership filter.
    /// </summary>
    public enum MembershipFilterEnum
    {
        /// <summary>
        /// Any active membership.
        /// </summary>
        Active,

        /// <summary>
        /// No active membership.
        /// </summary>
        Inactive,

        /// <summary>
        /// An active paid subscription.
        /// </summary>
        Paid,

        /// <summary>
        /// Active, but only through codes or comps.
        /// </summary>
        Free
    }
}
