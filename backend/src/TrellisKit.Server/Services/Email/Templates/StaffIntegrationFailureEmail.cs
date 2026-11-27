namespace TrellisKit.Server.Services.Email.Templates
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// Staff and platform administrators: an integration is failing (rate-limited). Template key staff_integration_failure.
    /// </summary>
    public sealed class StaffIntegrationFailureEmail : EmailTemplate
    {
        #region Public-Members

        /// <inheritdoc />
        public override string Key
        {
            get { return "staff_integration_failure"; }
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
            get { return "Staff and platform administrators: an integration is failing (rate-limited)."; }
        }

        /// <inheritdoc />
        public override string DefaultSubject
        {
            get { return "Integration problem on {name}: {integration}"; }
        }

        /// <inheritdoc />
        public override string DefaultPreheader
        {
            get { return "{integration} is failing."; }
        }

        /// <inheritdoc />
        public override string DefaultHeading
        {
            get { return "{integration} is failing"; }
        }

        /// <inheritdoc />
        public override string DefaultBody
        {
            get { return "{integration} reported a problem: {detail}\n\nThis alert is sent at most once per interval for the same integration."; }
        }

        /// <inheritdoc />
        public override string? ButtonLabel
        {
            get { return "Open the admin console"; }
        }

        /// <inheritdoc />
        public override string? StaffOrigin
        {
            get { return "Integration monitor"; }
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
            { StaffEmailDetails.Token, StaffEmailSamples.System("Integration monitor") },
            { "link", "https://example.test/admin" },
            { "integration", "Stripe webhook" },
            { "detail", "Signature verification failed." }
        };

        #endregion
    }
}
