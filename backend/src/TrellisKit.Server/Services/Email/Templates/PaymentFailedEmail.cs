namespace TrellisKit.Server.Services.Email.Templates
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// Payment failed, billing retry, or grace period. Template key payment_failed.
    /// </summary>
    public sealed class PaymentFailedEmail : EmailTemplate
    {
        #region Public-Members

        /// <inheritdoc />
        public override string Key
        {
            get { return "payment_failed"; }
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
            get { return "Payment failed, billing retry, or grace period."; }
        }

        /// <inheritdoc />
        public override string DefaultSubject
        {
            get { return "Action needed: your {name} payment did not go through"; }
        }

        /// <inheritdoc />
        public override string DefaultPreheader
        {
            get { return "Update your payment method."; }
        }

        /// <inheritdoc />
        public override string DefaultHeading
        {
            get { return "Your payment did not go through"; }
        }

        /// <inheritdoc />
        public override string DefaultBody
        {
            get { return "We could not charge your payment method for your {name} membership through {store}.\n\n{fixInstructions}"; }
        }

        /// <inheritdoc />
        public override string? ButtonLabel
        {
            get { return "Manage membership"; }
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
            { "store", "the web" },
            { "fixInstructions", "Update your card in the membership page to keep your access." }
        };

        #endregion
    }
}
