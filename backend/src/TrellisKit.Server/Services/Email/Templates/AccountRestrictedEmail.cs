namespace TrellisKit.Server.Services.Email.Templates
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// Account banned or muted. Template key account_restricted.
    /// </summary>
    public sealed class AccountRestrictedEmail : EmailTemplate
    {
        #region Public-Members

        /// <inheritdoc />
        public override string Key
        {
            get { return "account_restricted"; }
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
            get { return "Account banned or muted."; }
        }

        /// <inheritdoc />
        public override string DefaultSubject
        {
            get { return "Your {name} account was {restriction}"; }
        }

        /// <inheritdoc />
        public override string DefaultPreheader
        {
            get { return "Your account was {restriction}."; }
        }

        /// <inheritdoc />
        public override string DefaultHeading
        {
            get { return "Your account was {restriction}"; }
        }

        /// <inheritdoc />
        public override string DefaultBody
        {
            get { return "Your {name} account was {restriction}{until}. Reason: {reason}\n\nTo appeal, reply to {supportEmail} and explain what happened."; }
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
            { "restriction", "muted" },
            { "until", " until November 18, 2026" },
            { "reason", "Repeated off-topic posts." }
        };

        #endregion
    }
}
