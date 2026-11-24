namespace TrellisKit.Server.Services.Email.Templates
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// Following or Forums email digest (docs/PHASE3_CONTRACT.md section 9.3): the member's unread notifications of one
    /// category since the last digest, sent every forums.emailDigestIntervalHours to members who turned email on for the
    /// category. Optional: governed by notification preferences, with a one-click unsubscribe. Template key forum_digest.
    /// </summary>
    public sealed class ForumDigestEmail : EmailTemplate
    {
        #region Public-Members

        /// <summary>
        /// Template key.
        /// </summary>
        public const string TemplateKey = "forum_digest";

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
            get { return "Digest of Following or Forums notifications (members who turned the category's email on)."; }
        }

        /// <inheritdoc />
        public override string DefaultSubject
        {
            get { return "{count} new in {digestTitle} on {name}"; }
        }

        /// <inheritdoc />
        public override string DefaultPreheader
        {
            get { return "Catch up on {digestTitle}."; }
        }

        /// <inheritdoc />
        public override string DefaultHeading
        {
            get { return "What's new in {digestTitle}"; }
        }

        /// <inheritdoc />
        public override string DefaultBody
        {
            get { return "Hi {memberName}, here is what you missed in {digestTitle} since your last digest.\n\n{items}\n\nYou get this digest because {digestTitle} emails are on in your notification settings. Turn them off: {unsubscribeLink}"; }
        }

        /// <inheritdoc />
        public override string? ButtonLabel
        {
            get { return "Open notifications"; }
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
            { "link", "https://example.test/app/notifications" },
            { "count", "3" },
            { "digestTitle", "threads you follow" },
            { "items", "Alex Chen and 2 others commented on Weekly open thread: https://example.test/app/threads/thr_1\n3 new threads in General: https://example.test/app/forums/frm_1" },
            { "unsubscribeLink", "https://example.test/api/v1/email/unsubscribe?token=sample" }
        };

        #endregion
    }
}
