namespace TrellisKit.Server.Services.Email.Templates
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// Password changed notice. Template key password_changed.
    /// </summary>
    public sealed class PasswordChangedEmail : EmailTemplate
    {
        #region Public-Members

        /// <inheritdoc />
        public override string Key
        {
            get { return "password_changed"; }
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
            get { return "Password changed notice."; }
        }

        /// <inheritdoc />
        public override string DefaultSubject
        {
            get { return "Your {name} password was changed"; }
        }

        /// <inheritdoc />
        public override string DefaultPreheader
        {
            get { return "Your password was changed."; }
        }

        /// <inheritdoc />
        public override string DefaultHeading
        {
            get { return "Your password was changed"; }
        }

        /// <inheritdoc />
        public override string DefaultBody
        {
            get { return "The password of your {name} account was changed on {changedDate}.\n\nNot you? Reset your password now and tell us at {supportEmail}."; }
        }

        /// <inheritdoc />
        public override string? ButtonLabel
        {
            get { return "Reset password"; }
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
            { "link", "https://example.test/forgot-password" },
            { "changedDate", "November 11, 2026" }
        };

        #endregion
    }
}
