namespace TrellisKit.Server.Services.Email.Templates
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// Membership expired or lapsed. Template key membership_expired.
    /// </summary>
    public sealed class MembershipExpiredEmail : EmailTemplate
    {
        #region Public-Members

        /// <inheritdoc />
        public override string Key
        {
            get { return "membership_expired"; }
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
            get { return "Membership expired or lapsed."; }
        }

        /// <inheritdoc />
        public override string DefaultSubject
        {
            get { return "Your {name} membership has ended"; }
        }

        /// <inheritdoc />
        public override string DefaultPreheader
        {
            get { return "Rejoin at any time."; }
        }

        /// <inheritdoc />
        public override string DefaultHeading
        {
            get { return "Your membership has ended"; }
        }

        /// <inheritdoc />
        public override string DefaultBody
        {
            get { return "Your {name} membership has ended. Your account and profile are kept.\n\nYou can rejoin on the website at any time."; }
        }

        /// <inheritdoc />
        public override string? ButtonLabel
        {
            get { return "Rejoin"; }
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
            { "link", "https://example.test/join" }
        };

        #endregion
    }
}
