namespace TrellisKit.Server.Services.Email.Blocks
{
    using System;

    /// <summary>
    /// A highlighted note with an accent bar (info, success, warning, or danger).
    /// </summary>
    public class EmailCalloutBlock : EmailBlock
    {
        #region Public-Members

        /// <summary>
        /// Text.
        /// </summary>
        public string Text { get; }

        /// <summary>
        /// Tone.
        /// </summary>
        public EmailCalloutToneEnum Tone { get; }

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="text">Text (null renders nothing).</param>
        /// <param name="tone">Tone.</param>
        public EmailCalloutBlock(string? text, EmailCalloutToneEnum tone = EmailCalloutToneEnum.Info)
        {
            Text = (text ?? String.Empty).Trim();
            Tone = tone;
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public override string RenderHtml(EmailStyle style)
        {
            ArgumentNullException.ThrowIfNull(style);
            if (Text.Length == 0) return String.Empty;
            string accent = Tone switch
            {
                EmailCalloutToneEnum.Success => "#1a7f37",
                EmailCalloutToneEnum.Warning => "#9a6700",
                EmailCalloutToneEnum.Danger => "#cf222e",
                _ => style.Primary
            };
            return "<div class=\"callout\" style=\"background:" + EmailStyle.SurfaceColor + ";border-left:4px solid " + accent + ";border-radius:4px;padding:12px 16px;margin:0 0 16px\">" +
                "<p class=\"text\" style=\"" + style.Text + ";margin:0\">" + EmailStyle.EncodeLines(Text) + "</p></div>";
        }

        /// <inheritdoc />
        public override string RenderText()
        {
            if (Text.Length == 0) return String.Empty;
            return "> " + Text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace("\n", "\n> ", StringComparison.Ordinal);
        }

        #endregion
    }
}
