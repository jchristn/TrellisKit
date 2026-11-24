namespace TrellisKit.Core.Enums
{
    /// <summary>
    /// Role of a ticket message author.
    /// </summary>
    public enum TicketAuthorRoleEnum
    {
        /// <summary>
        /// The requester.
        /// </summary>
        Member,

        /// <summary>
        /// A SystemAdmin or SupportAgent.
        /// </summary>
        Staff,

        /// <summary>
        /// The server (timeline lines and automatic transitions).
        /// </summary>
        System
    }
}
