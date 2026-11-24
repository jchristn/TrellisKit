namespace TrellisKit.Server.Services.Email.Templates
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// library_digest (docs/PHASE4_CONTRACT.md section 9): Weekly digest of new lessons in series you follow (Library email preference, opt in).
    /// </summary>
    public sealed class LibraryDigestEmail : EmailTemplate
    {
        #region Public-Members

        /// <summary>
        /// Template key.
        /// </summary>
        public const string TemplateKey = "library_digest";

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
            get { return "Weekly digest of new lessons in series you follow (Library email preference, opt in)."; }
        }

        /// <inheritdoc />
        public override string DefaultSubject
        {
            get { return "{count} new lessons on {name}"; }
        }

        /// <inheritdoc />
        public override string DefaultPreheader
        {
            get { return "New lessons in series you follow."; }
        }

        /// <inheritdoc />
        public override string DefaultHeading
        {
            get { return "New lessons for you"; }
        }

        /// <inheritdoc />
        public override string DefaultBody
        {
            get { return "Hi {memberName}, here are the new lessons in series you follow.\n\n{items}\n\nYou get this digest because Library emails are on in your notification settings. Turn them off: {unsubscribeLink}"; }
        }

        /// <inheritdoc />
        public override string? ButtonLabel
        {
            get { return "Open the library"; }
        }

        /// <inheritdoc />
        public override string ButtonToken
        {
            get { return "link"; }
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
            { "count", "2" },
            { "items", "Getting started: First steps https://example.test/app/lessons/les_1\nGetting started: Week two https://example.test/app/lessons/les_2" },
            { "link", "https://example.test/app/library" },
            { "unsubscribeLink", "https://example.test/api/v1/email/unsubscribe?token=sample" }
        };

        #endregion
    }
}
