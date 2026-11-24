namespace TrellisKit.Server.Services.Email.Blocks
{
    using System;

    /// <summary>
    /// Preformatted monospace text (for example an error detail in a staff alert). Wraps long lines; text longer than
    /// <see cref="MaxLength"/> is truncated.
    /// </summary>
    public class EmailCodeBlock : EmailBlock
    {
        #region Public-Members

        /// <summary>
        /// Longest text kept.
        /// </summary>
        public const int MaxLength = 20000;

        /// <summary>
        /// Text.
        /// </summary>
        public string Text { get; }

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="text">Text (null renders nothing; longer than <see cref="MaxLength"/> is truncated).</param>
        public EmailCodeBlock(string? text)
        {
            string value = (text ?? String.Empty).Replace("\r\n", "\n", StringComparison.Ordinal).TrimEnd();
            Text = value.Length > MaxLength ? value.Substring(0, MaxLength) + "\n(truncated)" : value;
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public override string RenderHtml(EmailStyle style)
        {
            ArgumentNullException.ThrowIfNull(style);
            if (Text.Trim().Length == 0) return String.Empty;
            return "<pre class=\"code\" style=\"font-family:" + EmailStyle.MonospaceFamily + ";font-size:13px;line-height:1.45;color:" + EmailStyle.TextColor +
                ";background:" + EmailStyle.SurfaceColor + ";border:1px solid " + EmailStyle.BorderColor +
                ";border-radius:6px;padding:12px;margin:0 0 16px;white-space:pre-wrap;word-break:break-word\">" + EmailStyle.Encode(Text) + "</pre>";
        }

        /// <inheritdoc />
        public override string RenderText()
        {
            if (Text.Trim().Length == 0) return String.Empty;
            return "    " + Text.Replace("\n", "\n    ", StringComparison.Ordinal);
        }

        #endregion
    }
}
