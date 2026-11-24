namespace TrellisKit.Server.Services.Email.Templates
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// Account deletion completed. Template key account_deleted.
    /// </summary>
    public sealed class AccountDeletedEmail : EmailTemplate
    {
        #region Public-Members

        /// <inheritdoc />
        public override string Key
        {
            get { return "account_deleted"; }
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
            get { return "Account deletion completed."; }
        }

        /// <inheritdoc />
        public override string DefaultSubject
        {
            get { return "Your {name} account was deleted"; }
        }

        /// <inheritdoc />
        public override string DefaultPreheader
        {
            get { return "Your account was deleted."; }
        }

        /// <inheritdoc />
        public override string DefaultHeading
        {
            get { return "Your account was deleted"; }
        }

        /// <inheritdoc />
        public override string DefaultBody
        {
            get { return "Your {name} account and its personal data were deleted. Thank you for being part of {name}."; }
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
