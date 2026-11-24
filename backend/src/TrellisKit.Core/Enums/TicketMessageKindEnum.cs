namespace TrellisKit.Core.Enums
{
    /// <summary>
    /// Kind of a ticket message (docs/PHASE5_CONTRACT.md section 2.3).
    /// </summary>
    public enum TicketMessageKindEnum
    {
        /// <summary>
        /// Visible to the requester.
        /// </summary>
        Reply,

        /// <summary>
        /// Staff-only internal note; never returned to members and never emailed.
        /// </summary>
        Note,

        /// <summary>
        /// Staff timeline line written by the server for a status, priority, type, or assignment change. Members see
        /// only the status lines.
        /// </summary>
        System
    }
}
