namespace TrellisKit.Server.Services.Email.Templates
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// Unread direct messages notice (docs/PHASE5_CONTRACT.md sections 4.9 and 10): sender names and counts only, never
    /// message text. Optional (the Messages email preference, default off), at most one per member per
    /// messaging.emailMinIntervalHours, for messages unread longer than messaging.emailAfterMinutes. Template key
    /// messages_unread.
    /// </summary>
    public sealed class MessagesUnreadEmail : EmailTemplate
    {
        #region Public-Members

        /// <summary>
        /// Template key.
        /// </summary>
        public const string TemplateKey = "messages_unread";

        /// <inheritdoc />
        public override string Key
        {
            get { return TemplateKey; }
        }

        /// <inheritdoc />
        public override EmailCategoryEnum Category
        {
            get { return EmailCategoryEnum.Optional; }
        }

        /// <inheritdoc />
        public override EmailRecipientEnum Recipient
        {
            get { return EmailRecipientEnum.Member; }
        }

        /// <inheritdoc />
        public override string Description
        {
            get { return "Unread direct messages (members who turned the Messages email on). Sender names and counts only, never message text."; }
        }

        /// <inheritdoc />
        public override string DefaultSubject
        {
            get { return "You have {count} unread messages on {name}"; }
        }

        /// <inheritdoc />
        public override string DefaultPreheader
        {
            get { return "From {senderNames}."; }
        }

        /// <inheritdoc />
        public override string DefaultHeading
        {
            get { return "You have unread messages"; }
        }

        /// <inheritdoc />
        public override string DefaultBody
        {
            get { return "Hi {memberName}, you have {count} unread messages from {senderNames}.\n\nOpen your messages to read and reply.\n\nYou get this email because Messages emails are on in your notification settings. Turn them off: {unsubscribeLink}"; }
        }

        /// <inheritdoc />
        public override string? ButtonLabel
        {
            get { return "Open messages"; }
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
            { "link", "https://example.test/app/messages" },
            { "count", "4" },
            { "senderNames", "Alex Chen, Sam Rivera and 2 others" },
            { "unsubscribeLink", "https://example.test/api/v1/email/unsubscribe?token=sample" }
        };

        #endregion
    }
}
