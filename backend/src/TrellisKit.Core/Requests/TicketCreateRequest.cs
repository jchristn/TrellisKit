namespace TrellisKit.Core.Requests
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// POST /api/v1/tickets body (docs/PHASE5_CONTRACT.md section 6.1).
    /// </summary>
    public class TicketCreateRequest
    {
        #region Public-Members

        /// <summary>
        /// Ticket type key.
        /// </summary>
        public string? Type { get; set; } = null;

        /// <summary>
        /// Title (3 to 140 characters).
        /// </summary>
        public string? Title { get; set; } = null;

        /// <summary>
        /// Body (10 to support.maxBodyLength characters, Markdown subset).
        /// </summary>
        public string? Body { get; set; } = null;

        /// <summary>
        /// Attachments uploaded with purpose Ticket (at most support.maxAttachments), or null.
        /// </summary>
        public List<string>? MediaIds { get; set; } = null;

        /// <summary>
        /// Attach the device block from the X-Client headers (default false).
        /// </summary>
        public bool? IncludeDiagnostics { get; set; } = null;

        /// <summary>
        /// Optional labels.
        /// </summary>
        public List<string>? Labels { get; set; } = null;

        /// <summary>
        /// Optional tags.
        /// </summary>
        public Dictionary<string, string>? Tags { get; set; } = null;

        #endregion
    }
}
