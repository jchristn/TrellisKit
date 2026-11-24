namespace TrellisKit.Server.Services.Email.Templates
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// A free code membership was ended by staff. Template key code_revoked.
    /// </summary>
    public sealed class FreeMembershipRevokedEmail : EmailTemplate
    {
        #region Public-Members

        /// <inheritdoc />
        public override string Key
        {
            get { return "code_revoked"; }
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
            get { return "A free code membership was ended by staff."; }
        }

        /// <inheritdoc />
        public override string DefaultSubject
        {
            get { return "Your free {name} membership has ended"; }
        }

        /// <inheritdoc />
        public override string DefaultPreheader
        {
            get { return "Your free membership has ended."; }
        }

        /// <inheritdoc />
        public override string DefaultHeading
        {
            get { return "Your free membership has ended"; }
        }

        /// <inheritdoc />
        public override string DefaultBody
        {
            get { return "Your free membership from a sign-up code has been ended by an administrator. You can subscribe at any time.\n\nReply to {supportEmail} if you think this is a mistake."; }
        }

        /// <inheritdoc />
        public override string? ButtonLabel
        {
            get { return "See membership options"; }
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
            { "link", "https://example.test/app/membership" }
        };

        #endregion
    }
}
