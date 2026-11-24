namespace TrellisKit.Server.Services.Email.Templates
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// Free membership ending (14 and 3 days before). Template key code_expiry_reminder.
    /// </summary>
    public sealed class FreeMembershipEndingEmail : EmailTemplate
    {
        #region Public-Members

        /// <inheritdoc />
        public override string Key
        {
            get { return "code_expiry_reminder"; }
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
            get { return "Free membership ending (14 and 3 days before)."; }
        }

        /// <inheritdoc />
        public override string DefaultSubject
        {
            get { return "Your free {name} membership ends {endDate}"; }
        }

        /// <inheritdoc />
        public override string DefaultPreheader
        {
            get { return "Keep your access."; }
        }

        /// <inheritdoc />
        public override string DefaultHeading
        {
            get { return "Your free membership ends on {endDate}"; }
        }

        /// <inheritdoc />
        public override string DefaultBody
        {
            get { return "Your free {name} membership ends on {endDate}. To keep your access, subscribe before then.\n\nFree memberships never charge you automatically."; }
        }

        /// <inheritdoc />
        public override string? ButtonLabel
        {
            get { return "Keep my membership"; }
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
            { "endDate", "November 25, 2026" }
        };

        #endregion
    }
}
