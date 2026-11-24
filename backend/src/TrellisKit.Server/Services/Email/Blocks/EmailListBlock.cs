namespace TrellisKit.Server.Services.Email.Blocks
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text;

    /// <summary>
    /// A bulleted or numbered list. Empty items are skipped; a list with no items renders nothing.
    /// </summary>
    public class EmailListBlock : EmailBlock
    {
        #region Public-Members

        /// <summary>
        /// Items.
        /// </summary>
        public IReadOnlyList<string> Items { get; }

        /// <summary>
        /// Whether the list is numbered.
        /// </summary>
        public bool Ordered { get; }

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="items">Items.</param>
        /// <param name="ordered">Numbered instead of bulleted.</param>
        public EmailListBlock(IEnumerable<string>? items, bool ordered = false)
        {
            Items = (items ?? Enumerable.Empty<string>()).Where(i => !String.IsNullOrWhiteSpace(i)).Select(i => i.Trim()).ToList();
            Ordered = ordered;
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public override string RenderHtml(EmailStyle style)
        {
            ArgumentNullException.ThrowIfNull(style);
            if (Items.Count == 0) return String.Empty;
            string tag = Ordered ? "ol" : "ul";
            StringBuilder html = new StringBuilder();
            html.Append('<').Append(tag).Append(" style=\"margin:0 0 16px;padding-left:24px\">");
            foreach (string item in Items)
                html.Append("<li class=\"text\" style=\"").Append(style.Text).Append(";margin:0 0 6px\">").Append(EmailStyle.EncodeLines(item)).Append("</li>");
            html.Append("</").Append(tag).Append('>');
            return html.ToString();
        }

        /// <inheritdoc />
        public override string RenderText()
        {
            return String.Join("\n", Items.Select((item, i) => (Ordered ? (i + 1) + ". " : "- ") + item));
        }

        #endregion
    }
}
