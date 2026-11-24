namespace TrellisKit.Core.Enums
{
    /// <summary>
    /// The access tier of a caller, in ascending order of rights (docs/ACCESS_MODELS_CONTRACT.md section 3).
    /// </summary>
    public enum AccessTierEnum
    {
        /// <summary>
        /// Signed out.
        /// </summary>
        Anonymous,

        /// <summary>
        /// A signed-in account without an active membership.
        /// </summary>
        SignedIn,

        /// <summary>
        /// A signed-in account with an active membership.
        /// </summary>
        Member,

        /// <summary>
        /// A SystemAdmin (or the break-glass token or a delegated platform session): passes every access check.
        /// </summary>
        Staff
    }
}
