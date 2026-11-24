namespace TrellisKit.Core.PropertyConfig
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// One support ticket type in property.json support.ticketTypes (docs/PHASE5_CONTRACT.md section 2.3), seeded once
    /// when the property has no ticket types yet.
    /// </summary>
    public class PropertyTicketTypeSeed
    {
        #region Public-Members

        /// <summary>
        /// Key (1 to 64 letters, digits, underscores, or hyphens), for example Billing.
        /// </summary>
        public string Key { get; set; } = String.Empty;

        /// <summary>
        /// Label shown to members.
        /// </summary>
        public string Label { get; set; } = String.Empty;

        /// <summary>
        /// Description, or null.
        /// </summary>
        public string? Description { get; set; } = null;

        /// <summary>
        /// Help text shown above the new ticket form, or null.
        /// </summary>
        public string? HelpText { get; set; } = null;

        /// <summary>
        /// Staff email addresses that receive staff_ticket_opened for this type (at most 10), or null for none.
        /// </summary>
        public List<string>? StaffRecipients { get; set; } = null;

        #endregion
    }
}
