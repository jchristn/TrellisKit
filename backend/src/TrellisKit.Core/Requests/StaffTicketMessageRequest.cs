namespace TrellisKit.Core.Requests
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// POST /api/v1/admin/tickets/{id}/messages body.
    /// </summary>
    public class StaffTicketMessageRequest
    {
        #region Public-Members

        /// <summary>
        /// Reply (default; visible to the requester) or Note (staff only).
        /// </summary>
        public string? Kind { get; set; } = null;

        /// <summary>
        /// Text (Markdown subset).
        /// </summary>
        public string? Body { get; set; } = null;

        /// <summary>
        /// Attachments uploaded with purpose Ticket, or null.
        /// </summary>
        public List<string>? MediaIds { get; set; } = null;

        /// <summary>
        /// Status after a Reply (default WaitingOnMember); ignored for notes.
        /// </summary>
        public string? SetStatus { get; set; } = null;

        #endregion
    }
}
