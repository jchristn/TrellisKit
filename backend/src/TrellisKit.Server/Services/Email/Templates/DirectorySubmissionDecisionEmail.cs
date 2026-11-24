namespace TrellisKit.Server.Services.Email.Templates
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// directory_submission_decision (docs/PHASE4_CONTRACT.md section 9): Your directory submission was approved or rejected.
    /// </summary>
    public sealed class DirectorySubmissionDecisionEmail : EmailTemplate
    {
        #region Public-Members

        /// <summary>
        /// Template key.
        /// </summary>
        public const string TemplateKey = "directory_submission_decision";

        /// <inheritdoc />
        public override string Key
        {
            get { return TemplateKey; }
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
            get { return "Your directory submission was approved or rejected."; }
        }

        /// <inheritdoc />
        public override string DefaultSubject
        {
            get { return "Your suggestion for {targetName} was {decision}"; }
        }

        /// <inheritdoc />
        public override string DefaultPreheader
        {
            get { return "Directory staff reviewed your suggestion."; }
        }

        /// <inheritdoc />
        public override string DefaultHeading
        {
            get { return "Your suggestion was {decision}"; }
        }

        /// <inheritdoc />
        public override string DefaultBody
        {
            get { return "Hi {memberName}, directory staff reviewed your suggestion for {targetName}: it was {decision}.\n\n{reviewNote}"; }
        }

        /// <inheritdoc />
        public override string? ButtonLabel
        {
            get { return "Open"; }
        }

        /// <inheritdoc />
        public override string ButtonToken
        {
            get { return "link"; }
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
            { "targetName", "Example Club Downtown" },
            { "decision", "approved" },
            { "reviewNote", "Thanks, the address is updated." },
            { "link", "https://example.test/app/venues/ven_1" }
        };

        #endregion
    }
}
