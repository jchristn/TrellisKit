namespace TrellisKit.Server.Services.Email.Templates
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// Moderators: a new report. Template key staff_new_report.
    /// </summary>
    public sealed class StaffNewReportEmail : EmailTemplate
    {
        #region Public-Members

        /// <inheritdoc />
        public override string Key
        {
            get { return "staff_new_report"; }
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
            get { return "Moderators: a new report."; }
        }

        /// <inheritdoc />
        public override string DefaultSubject
        {
            get { return "New report on {name}: {targetType}"; }
        }

        /// <inheritdoc />
        public override string DefaultPreheader
        {
            get { return "New report: {targetType}. Reason: {reason}."; }
        }

        /// <inheritdoc />
        public override string DefaultHeading
        {
            get { return "New report: {targetType}"; }
        }

        /// <inheritdoc />
        public override string DefaultBody
        {
            get { return "A member reported content. Type: {targetType}. Reason: {reason}."; }
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
            { "targetType", "feed post" },
            { "reason", "Spam" }
        };

        #endregion
    }
}
