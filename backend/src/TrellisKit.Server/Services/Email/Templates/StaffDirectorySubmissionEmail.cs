namespace TrellisKit.Server.Services.Email.Templates
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// staff_directory_submission (docs/PHASE4_CONTRACT.md section 9): A member submitted a directory suggestion for review (directory staff).
    /// </summary>
    public sealed class StaffDirectorySubmissionEmail : EmailTemplate
    {
        #region Public-Members

        /// <summary>
        /// Template key.
        /// </summary>
        public const string TemplateKey = "staff_directory_submission";

        /// <inheritdoc />
        public override string Key
        {
            get { return TemplateKey; }
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
            get { return "A member submitted a directory suggestion for review (directory staff)."; }
        }

        /// <inheritdoc />
        public override string DefaultSubject
        {
            get { return "New directory suggestion: {submissionType} for {targetName}"; }
        }

        /// <inheritdoc />
        public override string DefaultPreheader
        {
            get { return "{submitterName} sent a suggestion for review."; }
        }

        /// <inheritdoc />
        public override string DefaultHeading
        {
            get { return "New directory suggestion"; }
        }

        /// <inheritdoc />
        public override string DefaultBody
        {
            get { return "{submitterName} sent a {submissionType} suggestion for {targetName}. Review it in the directory queue."; }
        }

        /// <inheritdoc />
        public override string? ButtonLabel
        {
            get { return "Review"; }
        }

        /// <inheritdoc />
        public override string ButtonToken
        {
            get { return "adminLink"; }
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
            { "submissionType", "EditVenue" },
            { "targetName", "Example Club Downtown" },
            { "submitterName", "Alex Chen" },
            { "adminLink", "https://example.test/admin/directory/submissions" }
        };

        #endregion
    }
}
