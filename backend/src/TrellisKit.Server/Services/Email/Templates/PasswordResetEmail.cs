namespace TrellisKit.Server.Services.Email.Templates
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// Password reset requested. Template key password_reset.
    /// </summary>
    public sealed class PasswordResetEmail : EmailTemplate
    {
        #region Public-Members

        /// <inheritdoc />
        public override string Key
        {
            get { return "password_reset"; }
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
            get { return "Password reset requested."; }
        }

        /// <inheritdoc />
        public override string DefaultSubject
        {
            get { return "Reset your {name} password"; }
        }

        /// <inheritdoc />
        public override string DefaultPreheader
        {
            get { return "Choose a new password."; }
        }

        /// <inheritdoc />
        public override string DefaultHeading
        {
            get { return "Reset your password"; }
        }

        /// <inheritdoc />
        public override string DefaultBody
        {
            get { return "Open the link below to choose a new password for your {name} account.\n\nThe link works once and expires in 1 hour. If you did not ask to reset your password, ignore this email."; }
        }

        /// <inheritdoc />
        public override string? ButtonLabel
        {
            get { return "Choose a new password"; }
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
            { "link", "https://example.test/reset-password?token=sample" }
        };

        #endregion
    }
}
