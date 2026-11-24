namespace TrellisKit.Server.Services.Email.Templates
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// Membership started (subscription purchased). Template key membership_started.
    /// </summary>
    public sealed class MembershipStartedEmail : EmailTemplate
    {
        #region Public-Members

        /// <inheritdoc />
        public override string Key
        {
            get { return "membership_started"; }
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
            get { return "Membership started (subscription purchased)."; }
        }

        /// <inheritdoc />
        public override string DefaultSubject
        {
            get { return "Your {name} membership has started"; }
        }

        /// <inheritdoc />
        public override string DefaultPreheader
        {
            get { return "Thank you for joining."; }
        }

        /// <inheritdoc />
        public override string DefaultHeading
        {
            get { return "Your membership has started"; }
        }

        /// <inheritdoc />
        public override string DefaultBody
        {
            get { return "Thank you for joining {name}. Your {plan} membership through {store} is active.\n\nIt renews on {renewalDate}. You can manage it at any time."; }
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
            { "plan", "Annual" },
            { "store", "the web" },
            { "renewalDate", "November 11, 2027" }
        };

        #endregion
    }
}
