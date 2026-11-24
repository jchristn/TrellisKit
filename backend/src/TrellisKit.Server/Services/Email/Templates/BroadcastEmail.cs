namespace TrellisKit.Server.Services.Email.Templates
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// A staff broadcast by email (docs/PHASE5_CONTRACT.md sections 8.3 and 10). Optional (the Announcements email
    /// preference, suppressions, and a one-click unsubscribe) unless the broadcast is Essential, which ignores the
    /// preference but never suppressions. The body token is the broadcast's Markdown subset, rendered to safe HTML.
    /// Template key broadcast.
    /// </summary>
    public sealed class BroadcastEmail : EmailTemplate
    {
        #region Public-Members

        /// <summary>
        /// Template key.
        /// </summary>
        public const string TemplateKey = "broadcast";

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
            get { return "Staff announcement to a broadcast segment (Announcements email preference, unless the broadcast is Essential)."; }
        }

        /// <inheritdoc />
        public override string DefaultSubject
        {
            get { return "{subject}"; }
        }

        /// <inheritdoc />
        public override string DefaultPreheader
        {
            get { return "{preheader}"; }
        }

        /// <inheritdoc />
        public override string DefaultHeading
        {
            get { return "{heading}"; }
        }

        /// <inheritdoc />
        public override string DefaultBody
        {
            get { return "{body}\n\n{footerNote}"; }
        }

        /// <inheritdoc />
        public override string? ButtonLabel
        {
            get { return "{ctaLabel}"; }
        }

        /// <inheritdoc />
        public override string ButtonToken
        {
            get { return "ctaLink"; }
        }

        /// <inheritdoc />
        public override IReadOnlyList<string> MarkdownTokens
        {
            get { return _Markdown; }
        }

        /// <inheritdoc />
        public override IReadOnlyDictionary<string, string> Samples
        {
            get { return _Samples; }
        }

        #endregion

        #region Private-Members

        private static readonly string[] _Markdown = new[] { "body" };

        private static readonly Dictionary<string, string> _Samples = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            { "subject", "Holiday schedule" },
            { "preheader", "What changes next week" },
            { "heading", "Holiday schedule" },
            { "body", "Opening hours change **next week**:\n\n- Monday: closed\n- Tuesday: 10:00 to 14:00\n\nDetails on [the schedule page](https://example.test/schedule)." },
            { "ctaLabel", "Read more" },
            { "ctaLink", "https://example.test/app/announcements/bct_sample" },
            { "unsubscribeLink", "https://example.test/api/v1/email/unsubscribe?token=sample" },
            { "footerNote", "You get announcements by email because Announcements emails are on in your notification settings. Turn them off: https://example.test/api/v1/email/unsubscribe?token=sample" }
        };

        #endregion
    }
}
