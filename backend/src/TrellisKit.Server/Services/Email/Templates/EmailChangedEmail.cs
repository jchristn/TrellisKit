namespace TrellisKit.Server.Services.Email.Templates
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// The account email address changed (sent to the previous address). Template key email_changed.
    /// </summary>
    public sealed class EmailChangedEmail : EmailTemplate
    {
        #region Public-Members

        /// <inheritdoc />
        public override string Key
        {
            get { return "email_changed"; }
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
            get { return "The account email address changed (sent to the previous address)."; }
        }

        /// <inheritdoc />
        public override string DefaultSubject
        {
            get { return "The email of your {name} account changed"; }
        }

        /// <inheritdoc />
        public override string DefaultPreheader
        {
            get { return "Your sign-in email changed."; }
        }

        /// <inheritdoc />
        public override string DefaultHeading
        {
            get { return "Your email address changed"; }
        }

        /// <inheritdoc />
        public override string DefaultBody
        {
            get { return "The email address of your {name} account was changed to {newEmail}.\n\nNot you? Tell us at {supportEmail} right away."; }
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
            { "newEmail", "new@example.test" }
        };

        #endregion
    }
}
