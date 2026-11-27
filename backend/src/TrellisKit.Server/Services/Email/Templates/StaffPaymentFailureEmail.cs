namespace TrellisKit.Server.Services.Email.Templates
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// Staff: a payment failed. Template key staff_payment_failure.
    /// </summary>
    public sealed class StaffPaymentFailureEmail : EmailTemplate
    {
        #region Public-Members

        /// <inheritdoc />
        public override string Key
        {
            get { return "staff_payment_failure"; }
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
            get { return "Staff: a payment failed."; }
        }

        /// <inheritdoc />
        public override string DefaultSubject
        {
            get { return "Payment failed on {name}: {subjectName}"; }
        }

        /// <inheritdoc />
        public override string DefaultPreheader
        {
            get { return "A renewal payment failed."; }
        }

        /// <inheritdoc />
        public override string DefaultHeading
        {
            get { return "A payment failed"; }
        }

        /// <inheritdoc />
        public override string DefaultBody
        {
            get { return "The renewal payment of {subjectName} ({store}) failed."; }
        }

        /// <inheritdoc />
        public override string? ButtonLabel
        {
            get { return "Open member"; }
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
            { StaffEmailDetails.Token, StaffEmailSamples.Subscription },
            { "link", "https://example.test/admin/members/usr_sample" },
            { "subjectName", "Pat Example" },
            { "store", "the web" }
        };

        #endregion
    }
}
