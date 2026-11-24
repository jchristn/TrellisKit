namespace TrellisKit.Core.Enums
{
    /// <summary>
    /// State of a report.
    /// </summary>
    public enum ReportStatusEnum
    {
        /// <summary>
        /// Waiting for a moderator.
        /// </summary>
        Open,

        /// <summary>
        /// Resolved with a moderation action.
        /// </summary>
        ActionTaken,

        /// <summary>
        /// Dismissed without changing the content.
        /// </summary>
        Dismissed
    }
}
