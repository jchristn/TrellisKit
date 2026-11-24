namespace TrellisKit.Core.Requests
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// POST /api/v1/tickets/{id}/messages body.
    /// </summary>
    public class TicketReplyRequest
    {
        #region Public-Members

        /// <summary>
        /// Reply (Markdown subset).
        /// </summary>
        public string? Body { get; set; } = null;

        /// <summary>
        /// Attachments uploaded with purpose Ticket, or null.
        /// </summary>
        public List<string>? MediaIds { get; set; } = null;

        #endregion
    }
}
