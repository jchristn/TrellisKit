namespace TrellisKit.Server.Services.Email.Templates
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// Content removed by moderators. Template key content_removed.
    /// </summary>
    public sealed class ContentRemovedEmail : EmailTemplate
    {
        #region Public-Members

        /// <inheritdoc />
        public override string Key
        {
            get { return "content_removed"; }
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
            get { return "Content removed by moderators."; }
        }

        /// <inheritdoc />
        public override string DefaultSubject
        {
            get { return "Your {contentType} on {name} was removed"; }
        }

        /// <inheritdoc />
        public override string DefaultPreheader
        {
            get { return "A moderator removed your {contentType}."; }
        }

        /// <inheritdoc />
        public override string DefaultHeading
        {
            get { return "Your {contentType} was removed"; }
        }

        /// <inheritdoc />
        public override string DefaultBody
        {
            get { return "A moderator removed your {contentType} \"{excerpt}\". Reason: {reason}\n\nReply to {supportEmail} if you think this is a mistake."; }
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
            { "contentType", "post" },
            { "excerpt", "First words of the post" },
            { "reason", "Spam" }
        };

        #endregion
    }
}
