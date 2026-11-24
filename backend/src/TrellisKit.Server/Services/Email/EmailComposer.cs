namespace TrellisKit.Server.Services.Email
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text.RegularExpressions;
    using TrellisKit.Server.Services.Email.Blocks;

    /// <summary>
    /// Builds the body of one email from code (docs/TRELLISKIT.md section 12.2). <see cref="EmailTemplate.Compose"/>
    /// receives a composer with the subject, preheader, heading, and body text already chosen (the property override when
    /// one is saved, otherwise the template default) and adds blocks in order.
    /// Methods that take template text (<see cref="Paragraph"/>, <see cref="Subheading"/>, <see cref="Callout"/>, <see cref="Notice"/>,
    /// <see cref="List"/>, <see cref="Fields"/>, <see cref="Button"/>) fill {token} placeholders first; <see cref="Code"/>
    /// and <see cref="Markdown"/> take literal values (pass <see cref="Token"/> for a token's value). Every value is
    /// encoded when the blocks render.
    /// Thread safety: not thread-safe; one composer per message.
    /// </summary>
    public sealed class EmailComposer
    {
        #region Public-Members

        /// <summary>
        /// Template being composed.
        /// </summary>
        public EmailTemplate Template { get; }

        /// <summary>
        /// Token values (missing tokens read as empty).
        /// </summary>
        public IReadOnlyDictionary<string, string> Tokens { get; }

        /// <summary>
        /// Brand of the property sending the message.
        /// </summary>
        public EmailBrand Brand { get; }

        /// <summary>
        /// Subject (tokens filled). A template may change it.
        /// </summary>
        public string Subject { get; set; }

        /// <summary>
        /// Preheader (tokens filled). A template may change it.
        /// </summary>
        public string Preheader { get; set; }

        /// <summary>
        /// Heading (tokens filled). A template may change it.
        /// </summary>
        public string Heading { get; set; }

        /// <summary>
        /// Body text with placeholders still in place: the property override when one is saved, otherwise the template
        /// default. <see cref="BodyParagraphs"/> adds it.
        /// </summary>
        public string BodyText { get; }

        /// <summary>
        /// Blocks added so far.
        /// </summary>
        public IReadOnlyList<EmailBlock> Blocks
        {
            get { return _Blocks; }
        }

        #endregion

        #region Private-Members

        private static readonly Regex _SoleToken = new Regex("^\\{([A-Za-z][A-Za-z0-9]*)\\}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
        private readonly List<EmailBlock> _Blocks = new List<EmailBlock>();

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="template">Template.</param>
        /// <param name="tokens">Token values.</param>
        /// <param name="brand">Brand.</param>
        /// <param name="subject">Subject (tokens filled).</param>
        /// <param name="preheader">Preheader (tokens filled).</param>
        /// <param name="heading">Heading (tokens filled).</param>
        /// <param name="bodyText">Body text (placeholders in place).</param>
        /// <exception cref="ArgumentNullException">template, tokens, or brand is null.</exception>
        public EmailComposer(EmailTemplate template, IReadOnlyDictionary<string, string> tokens, EmailBrand brand, string subject, string preheader, string heading, string bodyText)
        {
            Template = template ?? throw new ArgumentNullException(nameof(template));
            Tokens = tokens ?? throw new ArgumentNullException(nameof(tokens));
            Brand = brand ?? throw new ArgumentNullException(nameof(brand));
            Subject = subject ?? String.Empty;
            Preheader = preheader ?? String.Empty;
            Heading = heading ?? String.Empty;
            BodyText = bodyText ?? String.Empty;
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// A token's value, or empty.
        /// </summary>
        /// <param name="key">Token.</param>
        /// <returns>Value.</returns>
        public string Token(string key)
        {
            if (String.IsNullOrEmpty(key)) return String.Empty;
            return Tokens.TryGetValue(key, out string? value) ? value ?? String.Empty : String.Empty;
        }

        /// <summary>
        /// Whether a token has a non-blank value.
        /// </summary>
        /// <param name="key">Token.</param>
        /// <returns>True when set.</returns>
        public bool Has(string key)
        {
            return Token(key).Trim().Length > 0;
        }

        /// <summary>
        /// Fill {token} placeholders in text.
        /// </summary>
        /// <param name="text">Text.</param>
        /// <returns>Filled text.</returns>
        public string Fill(string? text)
        {
            return EmailRenderer.Fill(text ?? String.Empty, Tokens);
        }

        /// <summary>
        /// Add a block (including your own <see cref="EmailBlock"/> subclasses).
        /// </summary>
        /// <param name="block">Block.</param>
        /// <returns>This composer.</returns>
        /// <exception cref="ArgumentNullException">block is null.</exception>
        public EmailComposer Add(EmailBlock block)
        {
            ArgumentNullException.ThrowIfNull(block);
            _Blocks.Add(block);
            return this;
        }

        /// <summary>
        /// Add the body text (<see cref="BodyText"/>) as paragraphs split on blank lines. A paragraph that is exactly one
        /// of the template's Markdown tokens renders that value as Markdown.
        /// </summary>
        /// <returns>This composer.</returns>
        public EmailComposer BodyParagraphs()
        {
            foreach (string raw in BodyText.Replace("\r\n", "\n", StringComparison.Ordinal).Split("\n\n", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                Match sole = _SoleToken.Match(raw);
                if (sole.Success && Template.MarkdownTokens.Contains(sole.Groups[1].Value))
                {
                    Markdown(Token(sole.Groups[1].Value));
                    continue;
                }
                foreach (string paragraph in Fill(raw).Split("\n\n", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                    _Blocks.Add(new EmailParagraphBlock(paragraph));
            }
            return this;
        }

        /// <summary>
        /// Add a paragraph (tokens filled).
        /// </summary>
        /// <param name="text">Text.</param>
        /// <returns>This composer.</returns>
        public EmailComposer Paragraph(string? text)
        {
            _Blocks.Add(new EmailParagraphBlock(Fill(text)));
            return this;
        }

        /// <summary>
        /// Add a section heading (tokens filled).
        /// </summary>
        /// <param name="text">Text.</param>
        /// <returns>This composer.</returns>
        public EmailComposer Subheading(string? text)
        {
            _Blocks.Add(new EmailSubheadingBlock(Fill(text)));
            return this;
        }

        /// <summary>
        /// Add Markdown (literal value; use <see cref="Token"/> for a token).
        /// </summary>
        /// <param name="markdown">Markdown.</param>
        /// <returns>This composer.</returns>
        public EmailComposer Markdown(string? markdown)
        {
            _Blocks.Add(new EmailMarkdownBlock(markdown));
            return this;
        }

        /// <summary>
        /// Add a key/value table (labels and values filled; empty values skipped).
        /// </summary>
        /// <param name="fields">Rows.</param>
        /// <returns>This composer.</returns>
        public EmailComposer Fields(params EmailField[] fields)
        {
            _Blocks.Add(new EmailFieldsBlock((fields ?? Array.Empty<EmailField>()).Where(f => f != null).Select(f => new EmailField(Fill(f.Label), Fill(f.Value)))));
            return this;
        }

        /// <summary>
        /// Add a code block (literal value; use <see cref="Token"/> for a token).
        /// </summary>
        /// <param name="text">Text.</param>
        /// <returns>This composer.</returns>
        public EmailComposer Code(string? text)
        {
            _Blocks.Add(new EmailCodeBlock(text));
            return this;
        }

        /// <summary>
        /// Add a callout (tokens filled).
        /// </summary>
        /// <param name="text">Text.</param>
        /// <param name="tone">Tone.</param>
        /// <returns>This composer.</returns>
        public EmailComposer Callout(string? text, EmailCalloutToneEnum tone = EmailCalloutToneEnum.Info)
        {
            _Blocks.Add(new EmailCalloutBlock(Fill(text), tone));
            return this;
        }

        /// <summary>
        /// Add a small, muted notice with an optional link (text and label filled).
        /// </summary>
        /// <param name="text">Text.</param>
        /// <param name="linkLabel">Link label, or null.</param>
        /// <param name="url">URL (literal; use <see cref="Token"/> for a token), or null.</param>
        /// <returns>This composer.</returns>
        public EmailComposer Notice(string? text, string? linkLabel = null, string? url = null)
        {
            _Blocks.Add(new EmailNoticeBlock(Fill(text), linkLabel == null ? null : Fill(linkLabel), url));
            return this;
        }

        /// <summary>
        /// Add a divider.
        /// </summary>
        /// <returns>This composer.</returns>
        public EmailComposer Divider()
        {
            _Blocks.Add(new EmailDividerBlock());
            return this;
        }

        /// <summary>
        /// Add a list (items filled; empty items skipped).
        /// </summary>
        /// <param name="items">Items.</param>
        /// <param name="ordered">Numbered instead of bulleted.</param>
        /// <returns>This composer.</returns>
        public EmailComposer List(IEnumerable<string>? items, bool ordered = false)
        {
            _Blocks.Add(new EmailListBlock((items ?? Enumerable.Empty<string>()).Select(Fill), ordered));
            return this;
        }

        /// <summary>
        /// Add a button (label filled). Renders only for an absolute http(s) URL.
        /// </summary>
        /// <param name="label">Label.</param>
        /// <param name="url">URL (literal; use <see cref="Token"/> for a token).</param>
        /// <returns>This composer.</returns>
        public EmailComposer Button(string? label, string? url)
        {
            _Blocks.Add(new EmailButtonBlock(Fill(label), url));
            return this;
        }

        /// <summary>
        /// Add the template's own button (<see cref="EmailTemplate.ButtonLabel"/> linking to the value of
        /// <see cref="EmailTemplate.ButtonToken"/>), when it has one.
        /// </summary>
        /// <returns>This composer.</returns>
        public EmailComposer DefaultButton()
        {
            if (Template.ButtonLabel != null) Button(Template.ButtonLabel, Token(Template.ButtonToken));
            return this;
        }

        /// <summary>
        /// The composed document.
        /// </summary>
        /// <returns>Document.</returns>
        public EmailDocument ToDocument()
        {
            return new EmailDocument { Subject = Subject, Preheader = Preheader, Heading = Heading, Blocks = new List<EmailBlock>(_Blocks) };
        }

        #endregion
    }
}
