namespace TrellisKit.Core.Enums
{
    /// <summary>
    /// Who may start a direct conversation with a member (docs/PHASE5_CONTRACT.md section 1.2).
    /// </summary>
    public enum DmPolicyEnum
    {
        /// <summary>
        /// Anyone may send; strangers land in Requests.
        /// </summary>
        Everyone,

        /// <summary>
        /// Only members the recipient messaged before or accepted.
        /// </summary>
        Known,

        /// <summary>
        /// Only staff.
        /// </summary>
        Nobody
    }
}
