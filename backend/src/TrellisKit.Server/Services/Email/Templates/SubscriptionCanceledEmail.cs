namespace TrellisKit.Server.Services.Email.Templates
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// Auto-renew turned off. Template key subscription_canceled.
    /// </summary>
    public sealed class SubscriptionCanceledEmail : EmailTemplate
    {
        #region Public-Members

        /// <inheritdoc />
        public override string Key
        {
            get { return "subscription_canceled"; }
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
            get { return "Auto-renew turned off."; }
        }

        /// <inheritdoc />
        public override string DefaultSubject
        {
            get { return "Your {name} membership will not renew"; }
        }

        /// <inheritdoc />
        public override string DefaultPreheader
        {
            get { return "Your access continues until {endDate}."; }
        }

        /// <inheritdoc />
        public override string DefaultHeading
        {
            get { return "Your membership will not renew"; }
        }

        /// <inheritdoc />
        public override string DefaultBody
        {
            get { return "Automatic renewal of your {name} membership is off. Your access continues until {endDate}.\n\nYou can turn renewal back on before then."; }
        }

        /// <inheritdoc />
        public override string? ButtonLabel
        {
            get { return "Manage membership"; }
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
            { "link", "https://example.test/app/membership" },
            { "endDate", "November 11, 2027" }
        };

        #endregion
    }
}
