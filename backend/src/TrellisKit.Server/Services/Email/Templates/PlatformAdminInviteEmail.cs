namespace TrellisKit.Server.Services.Email.Templates
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// Platform administrator invitation. Template key platform_admin_invite.
    /// </summary>
    public sealed class PlatformAdminInviteEmail : EmailTemplate
    {
        #region Public-Members

        /// <inheritdoc />
        public override string Key
        {
            get { return "platform_admin_invite"; }
        }

        /// <inheritdoc />
        public override EmailCategoryEnum Category
        {
            get { return EmailCategoryEnum.Essential; }
        }

        /// <inheritdoc />
        public override EmailRecipientEnum Recipient
        {
            get { return EmailRecipientEnum.PlatformAdmins; }
        }

        /// <inheritdoc />
        public override string Description
        {
            get { return "Platform administrator invitation."; }
        }

        /// <inheritdoc />
        public override string DefaultSubject
        {
            get { return "You are invited to administer {name}"; }
        }

        /// <inheritdoc />
        public override string DefaultPreheader
        {
            get { return "Accept your invitation."; }
        }

        /// <inheritdoc />
        public override string DefaultHeading
        {
            get { return "You are invited to administer {name}"; }
        }

        /// <inheritdoc />
        public override string DefaultBody
        {
            get { return "{inviterName} invited you to administer {name}. Open the link to choose your password.\n\nThe invitation expires in {expiresDays} days. If you did not expect it, ignore this email."; }
        }

        /// <inheritdoc />
        public override string? ButtonLabel
        {
            get { return "Accept invitation"; }
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
            { "link", "https://platform.example.test/platform/accept-invite?token=sample" },
            { "inviterName", "A platform administrator" },
            { "expiresDays", "7" }
        };

        #endregion
    }
}
