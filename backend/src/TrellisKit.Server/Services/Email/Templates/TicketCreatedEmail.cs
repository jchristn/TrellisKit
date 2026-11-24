namespace TrellisKit.Server.Services.Email.Templates
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// Members: confirmation that a support ticket was opened, with its number. Template key ticket_created.
    /// </summary>
    public sealed class TicketCreatedEmail : EmailTemplate
    {
        #region Public-Members

        /// <inheritdoc />
        public override string Key
        {
            get { return "ticket_created"; }
        }

        /// <inheritdoc />
        public override EmailCategoryEnum Category
        {
            get { return EmailCategoryEnum.Essential; }
        }

        /// <inheritdoc />
        public override EmailRecipientEnum Recipient
        {
            get { return EmailRecipientEnum.Member; }
        }

        /// <inheritdoc />
        public override string Description
        {
            get { return "Members: confirmation that a support ticket was opened, with its number."; }
        }

        /// <inheritdoc />
        public override string DefaultSubject
        {
            get { return "We received your request {ticketNumber}"; }
        }

        /// <inheritdoc />
        public override string DefaultPreheader
        {
            get { return "Ticket {ticketNumber}: {title}"; }
        }

        /// <inheritdoc />
        public override string DefaultHeading
        {
            get { return "We received your request"; }
        }

        /// <inheritdoc />
        public override string DefaultBody
        {
            get { return "Thanks for contacting {name} support. Your ticket {ticketNumber}, \"{title}\", is open and our team will reply soon.\n\nYou can follow the conversation and add details in the app. Replies to this email are not read."; }
        }

        /// <inheritdoc />
        public override string? ButtonLabel
        {
            get { return "View your ticket"; }
        }

        /// <inheritdoc />
        public override IReadOnlyDictionary<string, string> Samples
        {
            get { return _Samples; }
        }

        #endregion

        #region Private-Members

        private static readonly Dictionary<string, string> _Samples = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            { "ticketNumber", "#1042" },
            { "title", "Charged twice" },
            { "link", "https://example.test/support/tickets/tkt_example" }
        };

        #endregion
    }
}
