namespace TrellisKit.Server.Services.Email.Templates
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// Support staff: a ticket passed its first response target without a reply. Template key staff_ticket_overdue.
    /// </summary>
    public sealed class StaffTicketOverdueEmail : EmailTemplate
    {
        #region Public-Members

        /// <inheritdoc />
        public override string Key
        {
            get { return "staff_ticket_overdue"; }
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
            get { return "Support staff: a ticket passed its first response target without a reply."; }
        }

        /// <inheritdoc />
        public override string DefaultSubject
        {
            get { return "Ticket {ticketNumber} is overdue"; }
        }

        /// <inheritdoc />
        public override string DefaultPreheader
        {
            get { return "\"{title}\" is waiting for a first reply"; }
        }

        /// <inheritdoc />
        public override string DefaultHeading
        {
            get { return "Ticket {ticketNumber} is past the first response target"; }
        }

        /// <inheritdoc />
        public override string DefaultBody
        {
            get { return "\"{title}\" has waited past its first response target of {dueUtc} without a staff reply."; }
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
            { "ticketNumber", "#1042" },
            { "title", "Charged twice" },
            { "dueUtc", "November 13, 2026" },
            { "adminLink", "https://example.test/admin/support/tickets/tkt_example" },
            { "link", "https://example.test/admin/support/tickets/tkt_example" }
        };

        #endregion
    }
}
