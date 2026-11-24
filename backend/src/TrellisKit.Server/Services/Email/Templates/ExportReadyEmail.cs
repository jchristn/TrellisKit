namespace TrellisKit.Server.Services.Email.Templates
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// Account data export ready. Template key export_ready.
    /// </summary>
    public sealed class ExportReadyEmail : EmailTemplate
    {
        #region Public-Members

        /// <inheritdoc />
        public override string Key
        {
            get { return "export_ready"; }
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
            get { return "Account data export ready."; }
        }

        /// <inheritdoc />
        public override string DefaultSubject
        {
            get { return "Your {name} data export is ready"; }
        }

        /// <inheritdoc />
        public override string DefaultPreheader
        {
            get { return "Download your data."; }
        }

        /// <inheritdoc />
        public override string DefaultHeading
        {
            get { return "Your data export is ready"; }
        }

        /// <inheritdoc />
        public override string DefaultBody
        {
            get { return "The copy of your {name} data you asked for is ready. Download it from your account settings; each download link expires after {expiresMinutes} minutes."; }
        }

        /// <inheritdoc />
        public override string? ButtonLabel
        {
            get { return "Open account settings"; }
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
            { "link", "https://example.test/app/settings" },
            { "expiresMinutes", "15" }
        };

        #endregion
    }
}
