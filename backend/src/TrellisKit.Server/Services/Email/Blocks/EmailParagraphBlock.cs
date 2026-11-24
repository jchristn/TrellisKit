namespace TrellisKit.Server.Services.Email.Blocks
{
    using System;

    /// <summary>
    /// A paragraph of plain text (encoded; line breaks kept).
    /// </summary>
    public class EmailParagraphBlock : EmailBlock
    {
        #region Public-Members

        /// <summary>
        /// Text.
        /// </summary>
        public string Text { get; }

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="text">Text (null renders nothing).</param>
        public EmailParagraphBlock(string? text)
        {
            Text = (text ?? String.Empty).Trim();
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public override string RenderHtml(EmailStyle style)
        {
            ArgumentNullException.ThrowIfNull(style);
            if (Text.Length == 0) return String.Empty;
            return "<p class=\"text\" style=\"" + style.Text + "\">" + EmailStyle.EncodeLines(Text) + "</p>";
        }

        /// <inheritdoc />
        public override string RenderText()
        {
            return Text;
        }

        #endregion
    }
}
