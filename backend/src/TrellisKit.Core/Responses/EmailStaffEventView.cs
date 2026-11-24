namespace TrellisKit.Core.Responses
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// A staff notification event and its setting.
    /// </summary>
    public class EmailStaffEventView : CatalogEntryMetadata
    {
        #region Public-Members

        /// <summary>
        /// Staff template key.
        /// </summary>
        public string Template { get; set; } = String.Empty;

        /// <summary>
        /// Event.
        /// </summary>
        public string Description { get; set; } = String.Empty;

        /// <summary>
        /// Who receives it when recipients is empty (Staff: SystemAdmins; Moderators: SystemAdmins and feed moderators).
        /// </summary>
        public EmailRecipientEnum DefaultRecipients { get; set; } = EmailRecipientEnum.Staff;

        /// <summary>
        /// Whether it is sent.
        /// </summary>
        public bool Enabled { get; set; } = true;

        /// <summary>
        /// Configured addresses (at most 10); empty uses the default recipients.
        /// </summary>
        public List<string> Recipients
        {
            get { return _Recipients; }
            set { _Recipients = value ?? new List<string>(); }
        }

        #endregion

        #region Private-Members

        private List<string> _Recipients = new List<string>();

        #endregion
    }
}
