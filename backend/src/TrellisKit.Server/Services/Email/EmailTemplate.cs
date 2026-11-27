namespace TrellisKit.Server.Services.Email
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// One administrative email message type (docs/TRELLISKIT.md sections 12.2 and 12.3). A template defines its engine
    /// default text with {token} placeholders, the tokens it accepts, and sample values for previews. Property
    /// administrators can override the subject, preheader, heading, and body (paragraphs separated by blank lines) with
    /// text that uses the same tokens. No template names a property: brand values come from the property.
    /// To write your own template, subclass this (or <see cref="TypedEmailTemplate{TContext}"/> for a typed context),
    /// override <see cref="Compose"/> to build the body from blocks (key/value tables, code, callouts, lists), and register
    /// it with <see cref="EmailTemplateCatalog.Register"/>. A property can also add or replace templates without code
    /// (JSON files in its email/templates folder, see <see cref="PropertyEmailTemplateLoader"/>).
    /// </summary>
    public abstract class EmailTemplate
    {
        #region Public-Members

        /// <summary>
        /// Tokens every template accepts: name (property name), memberName, supportEmail, webUrl.
        /// </summary>
        public static readonly IReadOnlyList<string> CommonTokens = new[] { "name", "memberName", "supportEmail", "webUrl" };

        /// <summary>
        /// Template key (stable; used in records, overrides, and staff settings).
        /// </summary>
        public abstract string Key { get; }

        /// <summary>
        /// Category.
        /// </summary>
        public abstract EmailCategoryEnum Category { get; }

        /// <summary>
        /// Recipient.
        /// </summary>
        public abstract EmailRecipientEnum Recipient { get; }

        /// <summary>
        /// What the message is for (admin portal).
        /// </summary>
        public abstract string Description { get; }

        /// <summary>
        /// Default subject.
        /// </summary>
        public abstract string DefaultSubject { get; }

        /// <summary>
        /// Default preheader (inbox preview text).
        /// </summary>
        public abstract string DefaultPreheader { get; }

        /// <summary>
        /// Default heading.
        /// </summary>
        public abstract string DefaultHeading { get; }

        /// <summary>
        /// Default body: paragraphs separated by blank lines.
        /// </summary>
        public abstract string DefaultBody { get; }

        /// <summary>
        /// Button label, or null for no button.
        /// </summary>
        public virtual string? ButtonLabel
        {
            get { return null; }
        }

        /// <summary>
        /// Token whose value is the button URL (default link).
        /// </summary>
        public virtual string ButtonToken
        {
            get { return "link"; }
        }

        /// <summary>
        /// For a Staff template that a worker, job, or monitor sends (never a member request): where it comes from, for
        /// example "Scheduled job". When set, a message queued without a <see cref="StaffEmailDetails.Token"/> value gets an
        /// Origin section (source, site, server version, trace id; docs/EMAIL.md). Default null.
        /// </summary>
        public virtual string? StaffOrigin
        {
            get { return null; }
        }

        /// <summary>
        /// Tokens whose values are Markdown (the Phase 2 subset): a body paragraph that is exactly one of these tokens is
        /// rendered to safe HTML instead of encoded text. Default none.
        /// </summary>
        public virtual IReadOnlyList<string> MarkdownTokens
        {
            get { return Array.Empty<string>(); }
        }

        /// <summary>
        /// Template-specific tokens with sample values.
        /// </summary>
        public abstract IReadOnlyDictionary<string, string> Samples { get; }

        /// <summary>
        /// Layout that turns the composed message into HTML and text. Default <see cref="StandardEmailLayout"/>.
        /// </summary>
        public virtual EmailLayout Layout
        {
            get { return StandardEmailLayout.Instance; }
        }

        /// <summary>
        /// Every token the template accepts. Staff templates also accept <see cref="StaffEmailDetails.Token"/>.
        /// </summary>
        public IReadOnlyList<string> Tokens
        {
            get
            {
                IEnumerable<string> tokens = CommonTokens.Concat(Samples.Keys);
                if (Category == EmailCategoryEnum.Staff) tokens = tokens.Append(StaffEmailDetails.Token);
                return tokens.Distinct(StringComparer.Ordinal).ToList();
            }
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the message body. The default adds the body paragraphs (<see cref="EmailComposer.BodyParagraphs"/>), the
        /// staff details sections of a Staff template (<see cref="StaffEmailDetails.Render"/>, docs/EMAIL.md), and the
        /// template's button (<see cref="EmailComposer.DefaultButton"/>). Override to add tables, code blocks,
        /// callouts, lists, or blocks of your own; keep calling BodyParagraphs so administrators' body overrides still
        /// apply. Read values with <see cref="EmailComposer.Token"/>: anything a template needs must be in its tokens,
        /// because queued messages are rendered again from their stored tokens on every delivery attempt.
        /// </summary>
        /// <param name="email">Composer.</param>
        /// <exception cref="ArgumentNullException">email is null.</exception>
        public virtual void Compose(EmailComposer email)
        {
            ArgumentNullException.ThrowIfNull(email);
            email.BodyParagraphs();
            StaffEmailDetails.Render(email);
            email.DefaultButton();
        }

        #endregion
    }
}
