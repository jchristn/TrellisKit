namespace TrellisKit.Core.Requests
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// POST and PUT /api/v1/admin/support/ticket-types body.
    /// </summary>
    public class TicketTypeRequest
    {
        #region Public-Members

        /// <summary>
        /// Key (1 to 64 letters, digits, underscores, or hyphens), unique per property.
        /// </summary>
        public string? Key { get; set; } = null;

        /// <summary>
        /// Label (1 to 80 characters).
        /// </summary>
        public string? Label { get; set; } = null;

        /// <summary>
        /// Description (at most 500 characters), or null.
        /// </summary>
        public string? Description { get; set; } = null;

        /// <summary>
        /// Help text shown above the new ticket form (at most 2000 characters), or null.
        /// </summary>
        public string? HelpText { get; set; } = null;

        /// <summary>
        /// Email addresses that receive staff_ticket_opened (at most 10), or null.
        /// </summary>
        public List<string>? StaffRecipients { get; set; } = null;

        /// <summary>
        /// Retire (true) or restore (false) the type.
        /// </summary>
        public bool? IsRetired { get; set; } = null;

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
