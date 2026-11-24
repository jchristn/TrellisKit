namespace TrellisKit.Core.Enums
{
    /// <summary>
    /// Who is looking at a profile, for visibility filtering.
    /// </summary>
    public enum ProfileViewerEnum
    {
        /// <summary>
        /// Signed out.
        /// </summary>
        Anonymous,

        /// <summary>
        /// Signed in without a membership.
        /// </summary>
        SignedIn,

        /// <summary>
        /// Signed in with a membership.
        /// </summary>
        Member,

        /// <summary>
        /// The member themself.
        /// </summary>
        Self,

        /// <summary>
        /// Staff (SystemAdmin, moderators, delegated platform admins, the admin token).
        /// </summary>
        Staff
    }
}
