namespace TrellisKit.Server.Services.Email.Templates
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// Account deletion requested. Template key account_deletion_requested.
    /// </summary>
    public sealed class AccountDeletionRequestedEmail : EmailTemplate
    {
        #region Public-Members

        /// <inheritdoc />
        public override string Key
        {
            get { return "account_deletion_requested"; }
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
            get { return "Account deletion requested."; }
        }

        /// <inheritdoc />
        public override string DefaultSubject
        {
            get { return "Your {name} account is being deleted"; }
        }

        /// <inheritdoc />
        public override string DefaultPreheader
        {
            get { return "We received your deletion request."; }
        }

        /// <inheritdoc />
        public override string DefaultHeading
        {
            get { return "Your account is being deleted"; }
        }

        /// <inheritdoc />
        public override string DefaultBody
        {
            get { return "We received your request to delete your {name} account. Your profile and personal data are being removed.\n\nNot you? Tell us at {supportEmail} right away."; }
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
        };

        #endregion
    }
}
