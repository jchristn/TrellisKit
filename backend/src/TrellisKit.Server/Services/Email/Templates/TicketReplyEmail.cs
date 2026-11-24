namespace TrellisKit.Server.Services.Email.Templates
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// Members: a support staff reply on their ticket, with the reply text. Template key ticket_reply.
    /// </summary>
    public sealed class TicketReplyEmail : EmailTemplate
    {
        #region Public-Members

        /// <inheritdoc />
        public override string Key
        {
            get { return "ticket_reply"; }
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
            get { return "Members: a support staff reply on their ticket, with the reply text."; }
        }

        /// <inheritdoc />
        public override string DefaultSubject
        {
            get { return "New reply on ticket {ticketNumber}"; }
        }

        /// <inheritdoc />
        public override string DefaultPreheader
        {
            get { return "{agentName} replied to \"{title}\""; }
        }

        /// <inheritdoc />
        public override string DefaultHeading
        {
            get { return "{agentName} replied to your ticket"; }
        }

        /// <inheritdoc />
        public override string DefaultBody
        {
            get { return "{replyText}\n\nReply in the app to continue the conversation. Replies to this email are not read."; }
        }

        /// <inheritdoc />
        public override string? ButtonLabel
        {
            get { return "Open the ticket"; }
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
            { "replyText", "Thanks for the details. We refunded the second charge." },
            { "agentName", "Alex Chen, Example Community Support" },
            { "link", "https://example.test/support/tickets/tkt_example" }
        };

        #endregion
    }
}
