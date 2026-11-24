namespace TrellisKit.Core.Models
{
    using System;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// A ticket message: member-visible reply, staff note, or system timeline line (table ticketmessages).
    /// </summary>
    public class TicketMessage
    {
        #region Public-Members

        /// <summary>
        /// Identifier (tkm_).
        /// </summary>
        public string Id { get; set; } = String.Empty;

        /// <summary>
        /// Ticket.
        /// </summary>
        public string TicketId { get; set; } = String.Empty;

        /// <summary>
        /// Kind.
        /// </summary>
        public TicketMessageKindEnum Kind { get; set; } = TicketMessageKindEnum.Reply;

        /// <summary>
        /// Author, or null (system lines, deleted accounts).
        /// </summary>
        public string? AuthorUserId { get; set; } = null;

        /// <summary>
        /// Author role.
        /// </summary>
        public TicketAuthorRoleEnum AuthorRole { get; set; } = TicketAuthorRoleEnum.Member;

        /// <summary>
        /// Body (Markdown subset; plain text for system lines).
        /// </summary>
        public string Body { get; set; } = String.Empty;

        /// <summary>
        /// For system lines that change the status, the new status (members see these); otherwise null.
        /// </summary>
        public TicketStatusEnum? StatusChange { get; set; } = null;

        /// <summary>
        /// Created (UTC).
        /// </summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        #endregion
    }
}
