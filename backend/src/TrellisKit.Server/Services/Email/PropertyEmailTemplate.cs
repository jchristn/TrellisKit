namespace TrellisKit.Server.Services.Email
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using TrellisKit.Core.Enums;
    using TrellisKit.Server.Services.Email.Blocks;

    /// <summary>
    /// A template defined by a property's JSON file (<see cref="PropertyEmailTemplateDocument"/>), either replacing an
    /// engine or registered template's defaults or adding a new one. Built and validated by
    /// <see cref="PropertyEmailTemplateLoader"/>.
    /// Thread safety: immutable after construction.
    /// </summary>
    public sealed class PropertyEmailTemplate : EmailTemplate
    {
        #region Public-Members

        /// <summary>
        /// The template this one replaces, or null for a new template.
        /// </summary>
        public EmailTemplate? Replaces { get; }

        /// <inheritdoc />
        public override string Key { get; }

        /// <inheritdoc />
        public override EmailCategoryEnum Category { get; }

        /// <inheritdoc />
        public override EmailRecipientEnum Recipient { get; }

        /// <inheritdoc />
        public override string Description { get; }

        /// <inheritdoc />
        public override string DefaultSubject { get; }

        /// <inheritdoc />
        public override string DefaultPreheader { get; }

        /// <inheritdoc />
        public override string DefaultHeading { get; }

        /// <inheritdoc />
        public override string DefaultBody { get; }

        /// <inheritdoc />
        public override string? ButtonLabel { get; }

        /// <inheritdoc />
        public override string ButtonToken { get; }

        /// <inheritdoc />
        public override IReadOnlyList<string> MarkdownTokens { get; }

        /// <inheritdoc />
        public override IReadOnlyDictionary<string, string> Samples { get; }

        /// <inheritdoc />
        public override EmailLayout Layout
        {
            get { return Replaces?.Layout ?? StandardEmailLayout.Instance; }
        }

        #endregion

        #region Private-Members

        private readonly List<PropertyEmailBlockDocument>? _Blocks;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate (the loader validates the document first).
        /// </summary>
        /// <param name="key">Key.</param>
        /// <param name="document">Document.</param>
        /// <param name="replaces">Replaced template, or null for a new one.</param>
        /// <param name="category">Category of a new template (ignored when replacing).</param>
        /// <param name="recipient">Recipient of a new template (ignored when replacing).</param>
        /// <exception cref="ArgumentNullException">key or document is null.</exception>
        public PropertyEmailTemplate(string key, PropertyEmailTemplateDocument document, EmailTemplate? replaces, EmailCategoryEnum category, EmailRecipientEnum recipient)
        {
            ArgumentNullException.ThrowIfNull(key);
            ArgumentNullException.ThrowIfNull(document);
            Key = key;
            Replaces = replaces;
            Category = replaces?.Category ?? category;
            Recipient = replaces?.Recipient ?? recipient;
            Description = document.Description ?? replaces?.Description ?? String.Empty;
            DefaultSubject = document.Subject ?? replaces?.DefaultSubject ?? String.Empty;
            DefaultPreheader = document.Preheader ?? replaces?.DefaultPreheader ?? String.Empty;
            DefaultHeading = document.Heading ?? replaces?.DefaultHeading ?? String.Empty;
            DefaultBody = document.Body ?? replaces?.DefaultBody ?? String.Empty;
            ButtonLabel = document.ButtonLabel == null ? replaces?.ButtonLabel : (document.ButtonLabel.Trim().Length == 0 ? null : document.ButtonLabel);
            ButtonToken = document.ButtonToken ?? replaces?.ButtonToken ?? "link";
            MarkdownTokens = (document.MarkdownTokens ?? replaces?.MarkdownTokens?.ToList() ?? new List<string>()).ToList();
            Dictionary<string, string> samples = new Dictionary<string, string>(StringComparer.Ordinal);
            if (replaces != null) foreach (KeyValuePair<string, string> pair in replaces.Samples) samples[pair.Key] = pair.Value;
            if (document.Samples != null) foreach (KeyValuePair<string, string> pair in document.Samples) samples[pair.Key] = pair.Value ?? String.Empty;
            Samples = samples;
            _Blocks = document.Blocks?.Where(b => b != null).ToList();
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public override void Compose(EmailComposer email)
        {
            ArgumentNullException.ThrowIfNull(email);
            if (_Blocks == null)
            {
                if (Replaces != null) Replaces.Compose(email);
                else base.Compose(email);
                return;
            }
            foreach (PropertyEmailBlockDocument block in _Blocks)
            {
                switch (Normalize(block.Type))
                {
                    case "body": email.BodyParagraphs(); break;
                    case "button":
                        if (block.Label == null && block.UrlToken == null) email.DefaultButton();
                        else email.Button(block.Label ?? ButtonLabel, email.Token(block.UrlToken ?? ButtonToken));
                        break;
                    case "paragraph": email.Paragraph(block.Text); break;
                    case "subheading": email.Subheading(block.Text); break;
                    case "callout": email.Callout(block.Text, ParseTone(block.Tone) ?? EmailCalloutToneEnum.Info); break;
                    case "list": email.List(block.Items, block.Ordered); break;
                    case "fields": email.Fields((block.Fields ?? new List<EmailFieldDocument>()).Where(f => f != null).Select(f => new EmailField(f.Label, f.Value)).ToArray()); break;
                    case "code": email.Code(email.Fill(block.Text)); break;
                    case "markdown": email.Markdown(email.Token(block.Token ?? String.Empty)); break;
                    case "divider": email.Divider(); break;
                    case "notice": email.Notice(block.Text, block.Label, block.UrlToken == null ? null : email.Token(block.UrlToken)); break;
                }
            }
        }

        /// <summary>
        /// Normalize a block type name (lowercase, trimmed).
        /// </summary>
        /// <param name="type">Type.</param>
        /// <returns>Normalized name.</returns>
        public static string Normalize(string? type)
        {
            return (type ?? String.Empty).Trim().ToLowerInvariant();
        }

        /// <summary>
        /// Parse a callout tone name (case-insensitive).
        /// </summary>
        /// <param name="tone">Name, or null.</param>
        /// <returns>Tone, or null when the name is not a tone.</returns>
        public static EmailCalloutToneEnum? ParseTone(string? tone)
        {
            if (String.IsNullOrWhiteSpace(tone)) return EmailCalloutToneEnum.Info;
            return Enum.TryParse(tone.Trim(), true, out EmailCalloutToneEnum parsed) && Enum.IsDefined(parsed) ? parsed : null;
        }

        #endregion
    }
}
