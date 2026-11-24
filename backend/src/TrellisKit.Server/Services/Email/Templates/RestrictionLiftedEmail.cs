namespace TrellisKit.Server.Services.Email.Templates
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// Ban or mute lifted. Template key restriction_lifted.
    /// </summary>
    public sealed class RestrictionLiftedEmail : EmailTemplate
    {
        #region Public-Members

        /// <inheritdoc />
        public override string Key
        {
            get { return "restriction_lifted"; }
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
            get { return "Ban or mute lifted."; }
        }

        /// <inheritdoc />
        public override string DefaultSubject
        {
            get { return "Your {name} account restriction was lifted"; }
        }

        /// <inheritdoc />
        public override string DefaultPreheader
        {
            get { return "Welcome back."; }
        }

        /// <inheritdoc />
        public override string DefaultHeading
        {
            get { return "Your account restriction was lifted"; }
        }

        /// <inheritdoc />
        public override string DefaultBody
        {
            get { return "The restriction on your {name} account was lifted. You can take part again."; }
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
            { "link", "https://example.test/app" }
        };

        #endregion
    }
}
