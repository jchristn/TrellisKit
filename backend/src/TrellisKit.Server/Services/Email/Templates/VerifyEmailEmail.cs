namespace TrellisKit.Server.Services.Email.Templates
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// Confirm an email address. Template key verify_email.
    /// </summary>
    public sealed class VerifyEmailEmail : EmailTemplate
    {
        #region Public-Members

        /// <inheritdoc />
        public override string Key
        {
            get { return "verify_email"; }
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
            get { return "Confirm an email address."; }
        }

        /// <inheritdoc />
        public override string DefaultSubject
        {
            get { return "Confirm your email for {name}"; }
        }

        /// <inheritdoc />
        public override string DefaultPreheader
        {
            get { return "Confirm your email address."; }
        }

        /// <inheritdoc />
        public override string DefaultHeading
        {
            get { return "Confirm your email address"; }
        }

        /// <inheritdoc />
        public override string DefaultBody
        {
            get { return "Open the link below to confirm the email address of your {name} account.\n\nThe link works once and expires in 48 hours. If you did not create an account, ignore this email."; }
        }

        /// <inheritdoc />
        public override string? ButtonLabel
        {
            get { return "Confirm email"; }
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
            { "link", "https://example.test/verify-email?token=sample" }
        };

        #endregion
    }
}
