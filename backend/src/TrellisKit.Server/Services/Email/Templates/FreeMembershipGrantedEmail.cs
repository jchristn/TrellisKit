namespace TrellisKit.Server.Services.Email.Templates
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// Free membership granted (sign-up code or staff comp). Template key free_membership_granted.
    /// </summary>
    public sealed class FreeMembershipGrantedEmail : EmailTemplate
    {
        #region Public-Members

        /// <inheritdoc />
        public override string Key
        {
            get { return "free_membership_granted"; }
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
            get { return "Free membership granted (sign-up code or staff comp)."; }
        }

        /// <inheritdoc />
        public override string DefaultSubject
        {
            get { return "You have a free {name} membership"; }
        }

        /// <inheritdoc />
        public override string DefaultPreheader
        {
            get { return "Your free membership is active."; }
        }

        /// <inheritdoc />
        public override string DefaultHeading
        {
            get { return "Your free membership is active"; }
        }

        /// <inheritdoc />
        public override string DefaultBody
        {
            get { return "You have been given a free {name} membership: {grantSummary}.\n\nFree memberships never charge you automatically."; }
        }

        /// <inheritdoc />
        public override string? ButtonLabel
        {
            get { return "Open {name}"; }
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
            { "link", "https://example.test/app" },
            { "grantSummary", "12 months, until November 11, 2027" }
        };

        #endregion
    }
}
