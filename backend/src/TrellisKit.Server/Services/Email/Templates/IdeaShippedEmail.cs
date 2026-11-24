namespace TrellisKit.Server.Services.Email.Templates
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// An idea the member voted for shipped (docs/PHASE5_CONTRACT.md section 10). Optional (category Ideas): sent only
    /// to voters who turned Ideas email on, with a one-click unsubscribe. Template key idea_shipped.
    /// </summary>
    public sealed class IdeaShippedEmail : EmailTemplate
    {
        #region Public-Members

        /// <summary>
        /// Template key.
        /// </summary>
        public const string TemplateKey = "idea_shipped";

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
            get { return "An idea the member voted for shipped (voters who turned Ideas email on)."; }
        }

        /// <inheritdoc />
        public override string DefaultSubject
        {
            get { return "Shipped: {ideaTitle}"; }
        }

        /// <inheritdoc />
        public override string DefaultPreheader
        {
            get { return "An idea you voted for on {name} is now available."; }
        }

        /// <inheritdoc />
        public override string DefaultHeading
        {
            get { return "{ideaTitle} shipped"; }
        }

        /// <inheritdoc />
        public override string DefaultBody
        {
            get { return "Good news: an idea you voted for is now available.\n\n{statusNote}\n\nYou get this email because Ideas emails are on in your notification settings. Turn them off: {unsubscribeLink}"; }
        }

        /// <inheritdoc />
        public override string? ButtonLabel
        {
            get { return "See the idea"; }
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
            { "ideaTitle", "Dark mode for the website" },
            { "statusNote", "Available in the latest release." },
            { "link", "https://example.test/app/ideas/ida_1" },
            { "unsubscribeLink", "https://example.test/api/v1/email/unsubscribe?token=sample" }
        };

        #endregion
    }
}
