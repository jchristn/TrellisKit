namespace TrellisKit.Server.Services.Email.Templates
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// A sign-in method was linked. Template key sign_in_method_linked.
    /// </summary>
    public sealed class SignInMethodLinkedEmail : EmailTemplate
    {
        #region Public-Members

        /// <inheritdoc />
        public override string Key
        {
            get { return "sign_in_method_linked"; }
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
            get { return "A sign-in method was linked."; }
        }

        /// <inheritdoc />
        public override string DefaultSubject
        {
            get { return "A sign-in method was added to your {name} account"; }
        }

        /// <inheritdoc />
        public override string DefaultPreheader
        {
            get { return "{method} sign-in was added."; }
        }

        /// <inheritdoc />
        public override string DefaultHeading
        {
            get { return "{method} sign-in was added"; }
        }

        /// <inheritdoc />
        public override string DefaultBody
        {
            get { return "You can now sign in to {name} with {method}.\n\nNot you? Remove it in your account settings and tell us at {supportEmail}."; }
        }

        /// <inheritdoc />
        public override string? ButtonLabel
        {
            get { return "Review sign-in methods"; }
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
            { "link", "https://example.test/app/settings" },
            { "method", "Google" }
        };

        #endregion
    }
}
