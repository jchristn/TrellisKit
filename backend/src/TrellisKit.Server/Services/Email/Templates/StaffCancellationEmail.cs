namespace TrellisKit.Server.Services.Email.Templates
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// Staff: auto-renew turned off. Template key staff_cancellation.
    /// </summary>
    public sealed class StaffCancellationEmail : EmailTemplate
    {
        #region Public-Members

        /// <inheritdoc />
        public override string Key
        {
            get { return "staff_cancellation"; }
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
            get { return "Staff: auto-renew turned off."; }
        }

        /// <inheritdoc />
        public override string DefaultSubject
        {
            get { return "Membership canceled on {name}: {subjectName}"; }
        }

        /// <inheritdoc />
        public override string DefaultPreheader
        {
            get { return "{subjectName} turned off renewal."; }
        }

        /// <inheritdoc />
        public override string DefaultHeading
        {
            get { return "{subjectName} turned off renewal"; }
        }

        /// <inheritdoc />
        public override string DefaultBody
        {
            get { return "{subjectName} turned off automatic renewal ({store}). Access continues until {endDate}."; }
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
            { "store", "the web" },
            { "endDate", "November 11, 2027" }
        };

        #endregion
    }
}
