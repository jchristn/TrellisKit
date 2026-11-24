namespace TrellisKit.Core.Enums
{
    /// <summary>
    /// Result of the central authorization check.
    /// </summary>
    public enum AuthorizationOutcomeEnum
    {
        /// <summary>
        /// The request may proceed.
        /// </summary>
        Permitted,

        /// <summary>
        /// No valid credentials (401).
        /// </summary>
        Unauthorized,

        /// <summary>
        /// Signed in but lacks the role (403).
        /// </summary>
        Forbidden,

        /// <summary>
        /// The member has not attested their age (403).
        /// </summary>
        AgeAttestationRequired,

        /// <summary>
        /// The member has no active membership (402).
        /// </summary>
        MembershipRequired,

        /// <summary>
        /// The member must accept the current version of a property document (403 DocumentAcceptanceRequired).
        /// </summary>
        DocumentAcceptanceRequired
    }
}
