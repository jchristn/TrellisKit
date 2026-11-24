namespace TrellisKit.Core.Enums
{
    /// <summary>
    /// Who may create threads in a forum (docs/PHASE3_CONTRACT.md section 2.1).
    /// </summary>
    public enum ThreadCreationEnum
    {
        /// <summary>
        /// Any member who can read the forum.
        /// </summary>
        Members,

        /// <summary>
        /// Moderators whose scope covers the forum.
        /// </summary>
        Moderators,

        /// <summary>
        /// SystemAdmin only.
        /// </summary>
        Admins
    }
}
