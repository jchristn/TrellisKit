namespace TrellisKit.Core.Enums
{
    /// <summary>
    /// State of a direct message (docs/PHASE5_CONTRACT.md section 2.2 and as-built note B).
    /// </summary>
    public enum MessageStateEnum
    {
        /// <summary>
        /// Shown to the members.
        /// </summary>
        Visible,

        /// <summary>
        /// Deleted for everyone by its author (tombstone; the content is kept internally for moderation).
        /// </summary>
        Deleted,

        /// <summary>
        /// Removed by a moderator after a report.
        /// </summary>
        Removed
    }
}
