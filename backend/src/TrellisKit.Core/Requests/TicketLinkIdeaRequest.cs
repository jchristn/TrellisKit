namespace TrellisKit.Core.Requests
{
    using System;

    /// <summary>
    /// POST /api/v1/admin/tickets/{id}/link-idea: link a ticket to an existing idea.
    /// </summary>
    public class TicketLinkIdeaRequest
    {
        #region Public-Members

        /// <summary>
        /// Idea identifier (required).
        /// </summary>
        public string? IdeaId { get; set; } = null;

        #endregion
    }
}
