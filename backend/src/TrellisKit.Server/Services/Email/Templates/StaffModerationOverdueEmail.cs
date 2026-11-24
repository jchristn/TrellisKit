namespace TrellisKit.Server.Services.Email.Templates
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// Moderators: reports past the review target. Template key staff_moderation_overdue.
    /// </summary>
    public sealed class StaffModerationOverdueEmail : EmailTemplate
    {
        #region Public-Members

        /// <inheritdoc />
        public override string Key
        {
            get { return "staff_moderation_overdue"; }
        }

        /// <inheritdoc />
        public override EmailCategoryEnum Category
        {
            get { return EmailCategoryEnum.Staff; }
        }

        /// <inheritdoc />
        public override EmailRecipientEnum Recipient
        {
            get { return EmailRecipientEnum.Moderators; }
        }

        /// <inheritdoc />
        public override string Description
        {
            get { return "Moderators: reports past the review target."; }
        }

        /// <inheritdoc />
        public override string DefaultSubject
        {
            get { return "Reports waiting on {name}"; }
        }

        /// <inheritdoc />
        public override string DefaultPreheader
        {
            get { return "{count} reports are overdue."; }
        }

        /// <inheritdoc />
        public override string DefaultHeading
        {
            get { return "{count} reports are past the review target"; }
        }

        /// <inheritdoc />
        public override string DefaultBody
        {
            get { return "{count} open reports have waited longer than the review target."; }
        }

        /// <inheritdoc />
        public override string? ButtonLabel
        {
            get { return "Open the moderation queue"; }
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
            { "link", "https://example.test/admin/moderation" },
            { "count", "3" }
        };

        #endregion
    }
}
