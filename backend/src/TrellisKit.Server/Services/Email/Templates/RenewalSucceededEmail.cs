namespace TrellisKit.Server.Services.Email.Templates
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// Membership renewed. Template key renewal_succeeded.
    /// </summary>
    public sealed class RenewalSucceededEmail : EmailTemplate
    {
        #region Public-Members

        /// <inheritdoc />
        public override string Key
        {
            get { return "renewal_succeeded"; }
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
            get { return "Membership renewed."; }
        }

        /// <inheritdoc />
        public override string DefaultSubject
        {
            get { return "Your {name} membership renewed"; }
        }

        /// <inheritdoc />
        public override string DefaultPreheader
        {
            get { return "Your membership renewed."; }
        }

        /// <inheritdoc />
        public override string DefaultHeading
        {
            get { return "Your membership renewed"; }
        }

        /// <inheritdoc />
        public override string DefaultBody
        {
            get { return "Your {name} membership through {store} renewed. The next renewal is on {renewalDate}."; }
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
            { "store", "the web" },
            { "renewalDate", "November 11, 2027" }
        };

        #endregion
    }
}
