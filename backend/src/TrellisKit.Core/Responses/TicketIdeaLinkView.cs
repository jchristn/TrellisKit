namespace TrellisKit.Core.Responses
{
    using System;

    /// <summary>
    /// POST /api/v1/admin/tickets/{id}/link-idea: { ticketId, ideaId }.
    /// </summary>
    public class TicketIdeaLinkView
    {
        #region Public-Members

        /// <summary>
        /// Ticket identifier.
        /// </summary>
        public string TicketId { get; set; } = String.Empty;

        /// <summary>
        /// Linked idea identifier.
        /// </summary>
        public string IdeaId { get; set; } = String.Empty;

        #endregion
    }
}
