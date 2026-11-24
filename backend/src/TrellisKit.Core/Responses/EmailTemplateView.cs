namespace TrellisKit.Core.Responses
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Models;

    /// <summary>
    /// An administrative email template with its engine defaults and the property's override.
    /// </summary>
    public class EmailTemplateView : CatalogEntryMetadata
    {
        #region Public-Members

        /// <summary>
        /// Template key.
        /// </summary>
        public string Key { get; set; } = String.Empty;

        /// <summary>
        /// Category.
        /// </summary>
        public EmailCategoryEnum Category { get; set; } = EmailCategoryEnum.Essential;

        /// <summary>
        /// Recipient.
        /// </summary>
        public EmailRecipientEnum Recipient { get; set; } = EmailRecipientEnum.Member;

        /// <summary>
        /// What the message is for.
        /// </summary>
        public string Description { get; set; } = String.Empty;

        /// <summary>
        /// Tokens the text may use, as {token}.
        /// </summary>
        public List<string> Tokens
        {
            get { return _Tokens; }
            set { _Tokens = value ?? new List<string>(); }
        }

        /// <summary>
        /// Engine default subject.
        /// </summary>
        public string DefaultSubject { get; set; } = String.Empty;

        /// <summary>
        /// Engine default preheader.
        /// </summary>
        public string DefaultPreheader { get; set; } = String.Empty;

        /// <summary>
        /// Engine default heading.
        /// </summary>
        public string DefaultHeading { get; set; } = String.Empty;

        /// <summary>
        /// Engine default body (paragraphs separated by blank lines).
        /// </summary>
        public string DefaultBody { get; set; } = String.Empty;

        /// <summary>
        /// The property's override, or null.
        /// </summary>
        public EmailTemplateOverride? Override { get; set; } = null;

        #endregion

        #region Private-Members

        private List<string> _Tokens = new List<string>();

        #endregion
    }
}
