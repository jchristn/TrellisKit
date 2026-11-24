namespace TrellisKit.Core.Models
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Per-event staff notification setting of a property.
    /// </summary>
    public class EmailStaffEvent
    {
        #region Public-Members

        /// <summary>
        /// Staff template key.
        /// </summary>
        public string Template { get; set; } = String.Empty;

        /// <summary>
        /// Whether the notification is sent.
        /// </summary>
        public bool Enabled { get; set; } = true;

        /// <summary>
        /// Recipient addresses (at most 10); empty sends to the default recipients.
        /// </summary>
        public List<string> Recipients
        {
            get { return _Recipients; }
            set { _Recipients = value ?? new List<string>(); }
        }

        /// <summary>
        /// When it changed (UTC).
        /// </summary>
        public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;

        #endregion

        #region Private-Members

        private List<string> _Recipients = new List<string>();

        #endregion
    }
}
