namespace TrellisKit.Server.Services.Email.Templates
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// Welcome after sign-up (after email verification, or at once for verified social sign-ups). Template key welcome.
    /// </summary>
    public sealed class WelcomeEmail : EmailTemplate
    {
        #region Public-Members

        /// <inheritdoc />
        public override string Key
        {
            get { return "welcome"; }
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
            get { return "Welcome after sign-up (after email verification, or at once for verified social sign-ups)."; }
        }

        /// <inheritdoc />
        public override string DefaultSubject
        {
            get { return "Welcome to {name}"; }
        }

        /// <inheritdoc />
        public override string DefaultPreheader
        {
            get { return "Your account is ready."; }
        }

        /// <inheritdoc />
        public override string DefaultHeading
        {
            get { return "Welcome, {memberName}"; }
        }

        /// <inheritdoc />
        public override string DefaultBody
        {
            get { return "Your {name} account is ready.\n\nComplete your profile so other members can find you, then say hello in the feed."; }
        }

        /// <inheritdoc />
        public override string? ButtonLabel
        {
            get { return "Open {name}"; }
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
            { "link", "https://example.test/app" }
        };

        #endregion
    }
}
