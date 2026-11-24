namespace TrellisKit.Server.Services.Email.Blocks
{
    using System;

    /// <summary>
    /// A small, muted line with an optional link at the end (for example why the member gets the email and how to
    /// unsubscribe). The link renders only for an absolute http(s) URL.
    /// </summary>
    public class EmailNoticeBlock : EmailBlock
    {
        #region Public-Members

        /// <summary>
        /// Text.
        /// </summary>
        public string Text { get; }

        /// <summary>
        /// Link label, or empty.
        /// </summary>
        public string LinkLabel { get; }

        /// <summary>
        /// Link URL, or empty.
        /// </summary>
        public string Url { get; }

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="text">Text.</param>
        /// <param name="linkLabel">Link label, or null for no link.</param>
        /// <param name="url">Absolute http(s) URL, or null.</param>
        public EmailNoticeBlock(string? text, string? linkLabel = null, string? url = null)
        {
            Text = (text ?? String.Empty).Trim();
            LinkLabel = (linkLabel ?? String.Empty).Trim();
            Url = (url ?? String.Empty).Trim();
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public override string RenderHtml(EmailStyle style)
        {
            ArgumentNullException.ThrowIfNull(style);
            if (Text.Length == 0 && !HasLink) return String.Empty;
            string html = EmailStyle.EncodeLines(Text);
            if (HasLink)
                html += (Text.Length > 0 ? " " : String.Empty) + "<a class=\"link\" href=\"" + EmailStyle.Encode(Url) + "\" style=\"color:" + style.Link + "\">" + EmailStyle.Encode(LinkLabel) + "</a>";
            return "<p class=\"muted\" style=\"" + style.Muted + ";margin:16px 0 8px\">" + html + "</p>";
        }

        /// <inheritdoc />
        public override string RenderText()
        {
            if (!HasLink) return Text;
            return (Text.Length > 0 ? Text + " " : String.Empty) + LinkLabel + ": " + Url;
        }

        #endregion

        #region Private-Members

        private bool HasLink
        {
            get { return LinkLabel.Length > 0 && EmailStyle.IsHttpUrl(Url); }
        }

        #endregion
    }
}
