namespace TrellisKit.Core.Responses
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// A support ticket type (staffRecipients only for SystemAdmin).
    /// </summary>
    public class TicketTypeView : IMetadataPayload
    {
        #region Public-Members

        /// <summary>
        /// Identifier (tty_).
        /// </summary>
        public string Id { get; set; } = String.Empty;

        /// <summary>
        /// Key.
        /// </summary>
        public string Key { get; set; } = String.Empty;

        /// <summary>
        /// Label.
        /// </summary>
        public string Label { get; set; } = String.Empty;

        /// <summary>
        /// Description.
        /// </summary>
        public string? Description { get; set; } = null;

        /// <summary>
        /// Help text shown above the new ticket form.
        /// </summary>
        public string? HelpText { get; set; } = null;

        /// <summary>
        /// SystemAdmin only: staff_ticket_opened recipients (at most 10).
        /// </summary>
        public List<string>? StaffRecipients { get; set; } = null;

        /// <summary>
        /// Sort order.
        /// </summary>
        public int SortOrder { get; set; } = 0;

        /// <summary>
        /// Retired (hidden from the form; existing tickets keep it).
        /// </summary>
        public bool IsRetired { get; set; } = false;

        /// <summary>
        /// Labels.
        /// </summary>
        public List<string> Labels
        {
            get { return _Labels; }
            set { _Labels = value ?? new List<string>(); }
        }

        /// <summary>
        /// Tags.
        /// </summary>
        public Dictionary<string, string> Tags
        {
            get { return _Tags; }
            set { _Tags = value ?? new Dictionary<string, string>(StringComparer.Ordinal); }
        }

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

        private List<string> _Labels = new List<string>();
        private Dictionary<string, string> _Tags = new Dictionary<string, string>(StringComparer.Ordinal);

        #endregion
    }
}
