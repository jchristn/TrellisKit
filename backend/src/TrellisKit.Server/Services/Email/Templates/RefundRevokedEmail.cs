namespace TrellisKit.Server.Services.Email.Templates
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// Refund or revocation. Template key refund_revoked.
    /// </summary>
    public sealed class RefundRevokedEmail : EmailTemplate
    {
        #region Public-Members

        /// <inheritdoc />
        public override string Key
        {
            get { return "refund_revoked"; }
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
            get { return "Refund or revocation."; }
        }

        /// <inheritdoc />
        public override string DefaultSubject
        {
            get { return "Your {name} membership was refunded"; }
        }

        /// <inheritdoc />
        public override string DefaultPreheader
        {
            get { return "Your purchase was refunded."; }
        }

        /// <inheritdoc />
        public override string DefaultHeading
        {
            get { return "Your purchase was refunded"; }
        }

        /// <inheritdoc />
        public override string DefaultBody
        {
            get { return "Your {name} membership purchase through {store} was refunded or revoked, and its access has ended.\n\nQuestions? Write to {supportEmail}."; }
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
            { "store", "the web" }
        };

        #endregion
    }
}
