namespace TrellisKit.Core.Enums
{
    /// <summary>
    /// Who may read a piece of content (forward-compatible with the property access models).
    /// </summary>
    public enum ContentAudienceEnum
    {
        /// <summary>
        /// Anyone, including anonymous visitors.
        /// </summary>
        Public,

        /// <summary>
        /// Any signed-in account.
        /// </summary>
        SignedIn,

        /// <summary>
        /// Members with an active membership.
        /// </summary>
        Members
    }
}
