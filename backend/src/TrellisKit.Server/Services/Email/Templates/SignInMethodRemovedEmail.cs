namespace TrellisKit.Server.Services.Email.Templates
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// A sign-in method was removed. Template key sign_in_method_removed.
    /// </summary>
    public sealed class SignInMethodRemovedEmail : EmailTemplate
    {
        #region Public-Members

        /// <inheritdoc />
        public override string Key
        {
            get { return "sign_in_method_removed"; }
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
            get { return "A sign-in method was removed."; }
        }

        /// <inheritdoc />
        public override string DefaultSubject
        {
            get { return "A sign-in method was removed from your {name} account"; }
        }

        /// <inheritdoc />
        public override string DefaultPreheader
        {
            get { return "{method} sign-in was removed."; }
        }

        /// <inheritdoc />
        public override string DefaultHeading
        {
            get { return "{method} sign-in was removed"; }
        }

        /// <inheritdoc />
        public override string DefaultBody
        {
            get { return "{method} can no longer be used to sign in to your {name} account.\n\nNot you? Tell us at {supportEmail}."; }
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
