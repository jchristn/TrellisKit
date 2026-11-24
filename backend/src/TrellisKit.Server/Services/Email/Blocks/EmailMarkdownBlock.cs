namespace TrellisKit.Server.Services.Email.Blocks
{
    using System;

    /// <summary>
    /// Markdown (the Phase 2 subset) rendered to safe HTML by <see cref="EmailMarkdown"/>, which encodes everything first.
    /// </summary>
    public class EmailMarkdownBlock : EmailBlock
    {
        #region Public-Members

        /// <summary>
        /// Markdown source.
        /// </summary>
        public string Markdown { get; }

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="markdown">Markdown (null renders nothing).</param>
        public EmailMarkdownBlock(string? markdown)
        {
            Markdown = markdown ?? String.Empty;
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public override string RenderHtml(EmailStyle style)
        {
            ArgumentNullException.ThrowIfNull(style);
            if (Markdown.Trim().Length == 0) return String.Empty;
            return EmailMarkdown.ToHtml(Markdown, style.Link);
        }

        /// <inheritdoc />
        public override string RenderText()
        {
            if (Markdown.Trim().Length == 0) return String.Empty;
            return EmailMarkdown.ToText(Markdown);
        }

        #endregion
    }
}
