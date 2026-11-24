namespace TrellisKit.Server.Services.Email.Templates
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// A test send from the admin portal. Template key test.
    /// </summary>
    public sealed class TestEmail : EmailTemplate
    {
        #region Public-Members

        /// <inheritdoc />
        public override string Key
        {
            get { return "test"; }
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
            get { return "A test send from the admin portal."; }
        }

        /// <inheritdoc />
        public override string DefaultSubject
        {
            get { return "Test email from {name}"; }
        }

        /// <inheritdoc />
        public override string DefaultPreheader
        {
            get { return "Your email settings work."; }
        }

        /// <inheritdoc />
        public override string DefaultHeading
        {
            get { return "Your email settings work"; }
        }

        /// <inheritdoc />
        public override string DefaultBody
        {
            get { return "This is a test email from {name}, sent through {provider}.\n\nIf you can read it, administrative email is configured."; }
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
            { "provider", "Mailgun" }
        };

        #endregion
    }
}
