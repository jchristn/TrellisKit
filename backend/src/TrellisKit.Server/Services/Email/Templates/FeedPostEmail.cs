namespace TrellisKit.Server.Services.Email.Templates
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// New feed post email (docs/PHASE2_CONTRACT.md section 8.9): sent by the notification fan-out to members who turned
    /// new post emails on. Optional: governed by notification preferences, with a one-click unsubscribe (List-Unsubscribe
    /// headers and an unsubscribe link in the body). The headline token is the post title, or "New post from" plus the
    /// author when the post has none. Template key feed_post.
    /// </summary>
    public sealed class FeedPostEmail : EmailTemplate
    {
        #region Public-Members

        /// <summary>
        /// Template key.
        /// </summary>
        public const string TemplateKey = "feed_post";

        /// <inheritdoc />
        public override string Key
        {
            get { return TemplateKey; }
        }

        /// <inheritdoc />
        public override EmailCategoryEnum Category
        {
            get { return EmailCategoryEnum.Optional; }
        }

        /// <inheritdoc />
        public override EmailRecipientEnum Recipient
        {
            get { return EmailRecipientEnum.Member; }
        }

        /// <inheritdoc />
        public override string Description
        {
            get { return "New feed post (members who turned new post emails on)."; }
        }

        /// <inheritdoc />
        public override string DefaultSubject
        {
            get { return "{headline}"; }
        }

        /// <inheritdoc />
        public override string DefaultPreheader
        {
            get { return "{authorName} posted in {name}."; }
        }

        /// <inheritdoc />
        public override string DefaultHeading
        {
            get { return "{headline}"; }
        }

        /// <inheritdoc />
        public override string DefaultBody
        {
            get { return "Hi {memberName}, {authorName} posted in {name}.\n\n{excerpt}"; }
        }

        /// <inheritdoc />
        public override string? ButtonLabel
        {
            get { return "Read the post"; }
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
            { "link", "https://example.test/app/feed/pst_sample" },
            { "headline", "Welcome to the community" },
            { "authorName", "Alex Chen" },
            { "postTitle", "Welcome to the community" },
            { "excerpt", "A short excerpt of the post." },
            { "unsubscribeLink", "https://example.test/api/v1/email/unsubscribe?token=sample" }
        };

        #endregion

        #region Public-Methods

        /// <summary>
        /// Body paragraphs, the button, then the unsubscribe notice (kept outside the editable body so an override cannot
        /// remove it).
        /// </summary>
        /// <param name="email">Composer.</param>
        public override void Compose(EmailComposer email)
        {
            ArgumentNullException.ThrowIfNull(email);
            email.BodyParagraphs().DefaultButton();
            email.Notice("You get these emails because new post emails are on in your notification settings.", "Unsubscribe from new post emails", email.Token("unsubscribeLink"));
        }

        #endregion
    }
}
