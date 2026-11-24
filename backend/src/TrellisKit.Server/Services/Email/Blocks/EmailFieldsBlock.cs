namespace TrellisKit.Server.Services.Email.Blocks
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text;

    /// <summary>
    /// A two-column key/value table (for example a staff alert's details). Rows with an empty value are skipped unless
    /// <see cref="SkipEmpty"/> is false; a table with no rows renders nothing.
    /// </summary>
    public class EmailFieldsBlock : EmailBlock
    {
        #region Public-Members

        /// <summary>
        /// Rows.
        /// </summary>
        public IReadOnlyList<EmailField> Fields { get; }

        /// <summary>
        /// Whether rows with an empty value are left out. Default true.
        /// </summary>
        public bool SkipEmpty { get; set; } = true;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="fields">Rows (null entries are ignored).</param>
        public EmailFieldsBlock(IEnumerable<EmailField>? fields)
        {
            Fields = (fields ?? Enumerable.Empty<EmailField>()).Where(f => f != null).ToList();
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public override string RenderHtml(EmailStyle style)
        {
            ArgumentNullException.ThrowIfNull(style);
            List<EmailField> rows = Rows();
            if (rows.Count == 0) return String.Empty;
            string cell = "font-family:" + EmailStyle.FontFamily + ";font-size:15px;line-height:1.5;vertical-align:top;padding:8px 0;border-bottom:1px solid " + EmailStyle.BorderColor;
            StringBuilder html = new StringBuilder();
            html.Append("<table role=\"presentation\" class=\"fields\" width=\"100%\" cellpadding=\"0\" cellspacing=\"0\" border=\"0\" style=\"width:100%;border-collapse:collapse;margin:0 0 16px\">");
            foreach (EmailField row in rows)
            {
                html.Append("<tr><td class=\"muted\" style=\"").Append(cell).Append(";padding-right:16px;color:").Append(EmailStyle.MutedColor).Append(";font-weight:600;white-space:nowrap\">")
                    .Append(EmailStyle.Encode(row.Label.Trim())).Append("</td>")
                    .Append("<td class=\"text\" style=\"").Append(cell).Append(";color:").Append(EmailStyle.TextColor).Append(";word-break:break-word\">")
                    .Append(EmailStyle.EncodeLines(row.Value.Trim())).Append("</td></tr>");
            }
            html.Append("</table>");
            return html.ToString();
        }

        /// <inheritdoc />
        public override string RenderText()
        {
            return String.Join("\n", Rows().Select(r => r.Label.Trim() + ": " + r.Value.Trim()));
        }

        #endregion

        #region Private-Methods

        private List<EmailField> Rows()
        {
            return Fields.Where(f => f.Label.Trim().Length > 0 && (!SkipEmpty || f.Value.Trim().Length > 0)).ToList();
        }

        #endregion
    }
}
