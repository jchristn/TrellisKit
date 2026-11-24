namespace TrellisKit.Server.Services.Email.Blocks
{
    using System;

    /// <summary>
    /// A section heading inside the body (below the message heading).
    /// </summary>
    public class EmailSubheadingBlock : EmailBlock
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
        public EmailSubheadingBlock(string? text)
        {
            Text = (text ?? String.Empty).Replace('\r', ' ').Replace('\n', ' ').Trim();
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public override string RenderHtml(EmailStyle style)
        {
            ArgumentNullException.ThrowIfNull(style);
            if (Text.Length == 0) return String.Empty;
            return "<h2 class=\"text\" style=\"font-family:" + EmailStyle.FontFamily + ";font-size:18px;line-height:1.3;font-weight:600;color:" + style.Heading +
                ";margin:24px 0 12px\">" + EmailStyle.Encode(Text) + "</h2>";
        }

        /// <inheritdoc />
        public override string RenderText()
        {
            return Text.Length == 0 ? String.Empty : Text.ToUpperInvariant();
        }

        #endregion
    }
}
