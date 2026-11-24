namespace TrellisKit.Core.Enums
{
    /// <summary>
    /// Thread state (docs/PHASE3_CONTRACT.md section 2.2); Deleted appears only in admin lists.
    /// </summary>
    public enum ThreadStateEnum
    {
        /// <summary>
        /// An attachment is still processing.
        /// </summary>
        Processing,

        /// <summary>
        /// Visible to its audience.
        /// </summary>
        Published,

        /// <summary>
        /// Hidden by a moderator or auto-hide.
        /// </summary>
        Hidden,

        /// <summary>
        /// Removed by a moderator.
        /// </summary>
        Removed,

        /// <summary>
        /// Deleted by the author or an administrator.
        /// </summary>
        Deleted
    }
}
