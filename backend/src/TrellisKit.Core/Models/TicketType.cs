namespace TrellisKit.Core.Models
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// A support ticket type (docs/PHASE5_CONTRACT.md section 6, table tickettypes).
    /// </summary>
    public class TicketType
    {
        #region Public-Members

        /// <summary>
        /// Identifier (tty_).
        /// </summary>
        public string Id { get; set; } = String.Empty;

        /// <summary>
        /// Key, unique per property (case-insensitive).
        /// </summary>
        public string Key { get; set; } = String.Empty;

        /// <summary>
        /// Label.
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
        /// Staff recipients of staff_ticket_opened (at most 10).
        /// </summary>
        public List<string> StaffRecipients
        {
            get { return _StaffRecipients; }
            set { _StaffRecipients = value ?? new List<string>(); }
        }

        /// <summary>
        /// Sort order (ascending).
        /// </summary>
        public int SortOrder { get; set; } = 0;

        /// <summary>
        /// Whether the type is retired (hidden from the new ticket form; existing tickets keep it).
        /// </summary>
        public bool IsRetired { get; set; } = false;

        /// <summary>
        /// Created (UTC).
        /// </summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Updated (UTC).
        /// </summary>
        public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;

        #endregion

        #region Private-Members

        private List<string> _StaffRecipients = new List<string>();

        #endregion
    }
}
