namespace TrellisKit.Server.Services.Email.Templates
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// Members: their support ticket was resolved or closed. Template key ticket_resolved.
    /// </summary>
    public sealed class TicketResolvedEmail : EmailTemplate
    {
        #region Public-Members

        /// <inheritdoc />
        public override string Key
        {
            get { return "ticket_resolved"; }
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
            get { return "Members: their support ticket was resolved or closed."; }
        }

        /// <inheritdoc />
        public override string DefaultSubject
        {
            get { return "Ticket {ticketNumber} is {status}"; }
        }

        /// <inheritdoc />
        public override string DefaultPreheader
        {
            get { return "\"{title}\" is {status}"; }
        }

        /// <inheritdoc />
        public override string DefaultHeading
        {
            get { return "Your ticket is {status}"; }
        }

        /// <inheritdoc />
        public override string DefaultBody
        {
            get { return "Your ticket {ticketNumber}, \"{title}\", is {status}.\n\nIf you still need help, reopen it in the app within {reopenDays} days."; }
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
            { "status", "resolved" },
            { "reopenDays", "14" },
            { "link", "https://example.test/support/tickets/tkt_example" }
        };

        #endregion
    }
}
