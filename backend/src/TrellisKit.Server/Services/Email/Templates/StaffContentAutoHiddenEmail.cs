namespace TrellisKit.Server.Services.Email.Templates
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// Moderators: content hidden automatically after reports. Template key staff_content_auto_hidden.
    /// </summary>
    public sealed class StaffContentAutoHiddenEmail : EmailTemplate
    {
        #region Public-Members

        /// <inheritdoc />
        public override string Key
        {
            get { return "staff_content_auto_hidden"; }
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
            get { return "Moderators: content hidden automatically after reports."; }
        }

        /// <inheritdoc />
        public override string DefaultSubject
        {
            get { return "Content hidden on {name}: {targetType}"; }
        }

        /// <inheritdoc />
        public override string DefaultPreheader
        {
            get { return "Hidden after reports: {targetType}."; }
        }

        /// <inheritdoc />
        public override string DefaultHeading
        {
            get { return "Hidden automatically: {targetType}"; }
        }

        /// <inheritdoc />
        public override string DefaultBody
        {
            get { return "Content reached the report threshold and is hidden until a moderator reviews it. Type: {targetType}."; }
        }

        /// <inheritdoc />
        public override string? ButtonLabel
        {
            get { return "Review it"; }
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
            { "targetType", "feed post" }
        };

        #endregion
    }
}
