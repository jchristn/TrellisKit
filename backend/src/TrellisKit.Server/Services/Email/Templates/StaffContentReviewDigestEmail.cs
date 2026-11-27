namespace TrellisKit.Server.Services.Email.Templates
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// Administrators: the daily digest of member contributions the content reviewer flagged (docs/INTEGRATION.md owner
    /// requirement R3). Template key staff_content_review_digest.
    /// </summary>
    public sealed class StaffContentReviewDigestEmail : EmailTemplate
    {
        #region Public-Members

        /// <summary>
        /// Template key.
        /// </summary>
        public const string TemplateKey = "staff_content_review_digest";

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
            get { return EmailRecipientEnum.Staff; }
        }

        /// <inheritdoc />
        public override string Description
        {
            get { return "Administrators: daily digest of member contributions the content reviewer flagged."; }
        }

        /// <inheritdoc />
        public override string DefaultSubject
        {
            get { return "{count} flagged contributions on {name}"; }
        }

        /// <inheritdoc />
        public override string DefaultPreheader
        {
            get { return "{count} contributions were flagged in the last day; {open} wait for review."; }
        }

        /// <inheritdoc />
        public override string DefaultHeading
        {
            get { return "{count} contributions were flagged in the last day"; }
        }

        /// <inheritdoc />
        public override string DefaultBody
        {
            get { return "The content reviewer flagged {count} member contributions in the last 24 hours ({highest} at the highest severity). {open} flagged contributions wait for an administrator, and {pending} are waiting for review."; }
        }

        /// <inheritdoc />
        public override string? ButtonLabel
        {
            get { return "Open the content review queue"; }
        }

        /// <inheritdoc />
        public override string? StaffOrigin
        {
            get { return "Content review digest (scheduled)"; }
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
            { StaffEmailDetails.Token, StaffEmailSamples.System("Content review digest (scheduled)") },
            { "link", "https://example.test/admin/content-review?status=Flagged" },
            { "count", "4" },
            { "highest", "1 High" },
            { "open", "6" },
            { "pending", "12" }
        };

        #endregion
    }
}
