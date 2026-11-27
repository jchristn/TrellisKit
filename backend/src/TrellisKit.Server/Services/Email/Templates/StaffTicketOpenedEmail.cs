namespace TrellisKit.Server.Services.Email.Templates
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// Support staff: a member opened a ticket of a type this address is a recipient of. Template key staff_ticket_opened.
    /// </summary>
    public sealed class StaffTicketOpenedEmail : EmailTemplate
    {
        #region Public-Members

        /// <inheritdoc />
        public override string Key
        {
            get { return "staff_ticket_opened"; }
        }

        /// <inheritdoc />
        public override EmailCategoryEnum Category
        {
            get { return EmailCategoryEnum.Staff; }
        }

        /// <inheritdoc />
        public override EmailRecipientEnum Recipient
        {
            get { return EmailRecipientEnum.Staff; }
        }

        /// <inheritdoc />
        public override string Description
        {
            get { return "Support staff: a member opened a ticket of a type this address is a recipient of."; }
        }

        /// <inheritdoc />
        public override string DefaultSubject
        {
            get { return "New {type} ticket {ticketNumber} on {name}"; }
        }

        /// <inheritdoc />
        public override string DefaultPreheader
        {
            get { return "{requesterName}: {title}"; }
        }

        /// <inheritdoc />
        public override string DefaultHeading
        {
            get { return "New {type} ticket {ticketNumber}"; }
        }

        /// <inheritdoc />
        public override string DefaultBody
        {
            get { return "{requesterName} opened \"{title}\"."; }
        }

        /// <inheritdoc />
        public override string? ButtonLabel
        {
            get { return "Open the ticket"; }
        }

        /// <inheritdoc />
        public override string ButtonToken
        {
            get { return "adminLink"; }
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
            { StaffEmailDetails.Token, StaffEmailSamples.TicketOpened },
            { "ticketNumber", "#1042" },
            { "type", "Billing" },
            { "title", "Charged twice" },
            { "requesterName", "Sam Rivera" },
            { "adminLink", "https://example.test/admin/support/tickets/tkt_example" },
            { "link", "https://example.test/admin/support/tickets/tkt_example" }
        };

        #endregion
    }
}
