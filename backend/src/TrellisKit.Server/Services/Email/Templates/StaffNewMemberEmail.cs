namespace TrellisKit.Server.Services.Email.Templates
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// Staff: a new member signed up. Template key staff_new_member.
    /// </summary>
    public sealed class StaffNewMemberEmail : EmailTemplate
    {
        #region Public-Members

        /// <inheritdoc />
        public override string Key
        {
            get { return "staff_new_member"; }
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
            get { return "Staff: a new member signed up."; }
        }

        /// <inheritdoc />
        public override string DefaultSubject
        {
            get { return "New member on {name}: {subjectName}"; }
        }

        /// <inheritdoc />
        public override string DefaultPreheader
        {
            get { return "{subjectName} joined."; }
        }

        /// <inheritdoc />
        public override string DefaultHeading
        {
            get { return "{subjectName} joined {name}"; }
        }

        /// <inheritdoc />
        public override string DefaultBody
        {
            get { return "{subjectName} ({subjectEmail}) created an account."; }
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
            { StaffEmailDetails.Token, StaffEmailSamples.NewMember },
            { "link", "https://example.test/admin/members/usr_sample" },
            { "subjectName", "Pat Example" },
            { "subjectEmail", "pat@example.test" }
        };

        #endregion
    }
}
