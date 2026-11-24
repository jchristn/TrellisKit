namespace TrellisKit.Server.Services.Email.Blocks
{
    using System;

    /// <summary>
    /// A horizontal rule between sections.
    /// </summary>
    public class EmailDividerBlock : EmailBlock
    {
        #region Public-Methods

        /// <inheritdoc />
        public override string RenderHtml(EmailStyle style)
        {
            ArgumentNullException.ThrowIfNull(style);
            return "<hr style=\"border:0;border-top:1px solid " + EmailStyle.BorderColor + ";margin:24px 0\">";
        }

        /// <inheritdoc />
        public override string RenderText()
        {
            return "----";
        }

        #endregion
    }
}
