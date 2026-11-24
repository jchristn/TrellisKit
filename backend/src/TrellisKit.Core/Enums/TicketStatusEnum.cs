namespace TrellisKit.Core.Enums
{
    /// <summary>
    /// Status of a support ticket (docs/PHASE5_CONTRACT.md section 2.3).
    /// </summary>
    public enum TicketStatusEnum
    {
        /// <summary>
        /// New or reopened; waiting on support staff.
        /// </summary>
        Open,

        /// <summary>
        /// Support staff are working on it.
        /// </summary>
        InProgress,

        /// <summary>
        /// Support staff replied and wait for the requester.
        /// </summary>
        WaitingOnMember,

        /// <summary>
        /// Support staff consider it solved; it closes automatically after support.autoCloseDays without a member reply.
        /// </summary>
        Resolved,

        /// <summary>
        /// Closed; the requester may reopen it within support.reopenDays.
        /// </summary>
        Closed
    }
}
