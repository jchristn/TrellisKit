namespace TrellisKit.Server.Services.Email
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Net;
    using System.Text;
    using System.Text.RegularExpressions;

    /// <summary>
    /// Renders the Phase 2 Markdown subset (docs/PHASE2_CONTRACT.md section 3.1) to safe, inline-styled email HTML:
    /// paragraphs and line breaks, bold, italic, strike, one-level lists, quotes, level 2 and 3 headings, and http or
    /// https links (Markdown links and bare URLs). Every character of the source is HTML encoded first, so raw HTML,
    /// images, and other link schemes render as the literal text that was typed.
    /// Thread safety: stateless; safe to call concurrently.
    /// </summary>
    public static class EmailMarkdown
    {
        #region Private-Members

        private const string _EscapeCharacters = "*_~[]()#>-@\\";
        private const char _EscapeBase = '';
        private const string _Text = "font-family:-apple-system,'Segoe UI',Helvetica,Arial,sans-serif;font-size:16px;line-height:1.5;color:#1f2328;margin:0 0 16px";
        private const string _Heading = "font-family:-apple-system,'Segoe UI',Helvetica,Arial,sans-serif;line-height:1.3;color:#1f2328;margin:0 0 12px";

        private static readonly Regex _Escape = new Regex("\\\\([*_~\\[\\]()#>\\-@\\\\])", RegexOptions.Compiled | RegexOptions.CultureInvariant);
        private static readonly Regex _Links = new Regex("\\[([^\\]\\n]*)\\]\\((https?://[^)\\s]+)\\)|(https?://[^\\s<>()\\[\\]\"']+)", RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
        private static readonly Regex _Bold = new Regex("\\*\\*(?=\\S)(.+?)(?<=\\S)\\*\\*", RegexOptions.Compiled | RegexOptions.CultureInvariant);
        private static readonly Regex _Strike = new Regex("~~(?=\\S)(.+?)(?<=\\S)~~", RegexOptions.Compiled | RegexOptions.CultureInvariant);
        private static readonly Regex _ItalicStar = new Regex("(?<![*\\w])\\*(?=\\S)(.+?)(?<=\\S)\\*(?![*\\w])", RegexOptions.Compiled | RegexOptions.CultureInvariant);
        private static readonly Regex _ItalicUnderscore = new Regex("(?<![_\\w])_(?=\\S)(.+?)(?<=\\S)_(?![_\\w])", RegexOptions.Compiled | RegexOptions.CultureInvariant);
        private static readonly Regex _Bullet = new Regex("^\\s*[-*]\\s+", RegexOptions.Compiled | RegexOptions.CultureInvariant);
        private static readonly Regex _Numbered = new Regex("^\\s*\\d+\\.\\s+", RegexOptions.Compiled | RegexOptions.CultureInvariant);

        #endregion

        #region Public-Methods

        /// <summary>
        /// Render Markdown to an HTML fragment of block elements.
        /// </summary>
        /// <param name="markdown">Source, or null.</param>
        /// <param name="linkColor">Link color (a #hex color).</param>
        /// <returns>Safe HTML (empty for an empty source).</returns>
        public static string ToHtml(string? markdown, string linkColor)
        {
            string color = Regex.IsMatch(linkColor ?? String.Empty, "^#[0-9A-Fa-f]{3,8}$") ? linkColor! : "#111111";
            StringBuilder html = new StringBuilder();
            foreach (List<string> block in Blocks(markdown))
            {
                if (block.All(l => _Bullet.IsMatch(l)))
                {
                    html.Append("<ul style=\"" + _Text + ";padding-left:24px\">");
                    foreach (string line in block) html.Append("<li>").Append(Inline(_Bullet.Replace(line, String.Empty), color)).Append("</li>");
                    html.Append("</ul>");
                }
                else if (block.All(l => _Numbered.IsMatch(l)))
                {
                    html.Append("<ol style=\"" + _Text + ";padding-left:24px\">");
                    foreach (string line in block) html.Append("<li>").Append(Inline(_Numbered.Replace(line, String.Empty), color)).Append("</li>");
                    html.Append("</ol>");
                }
                else if (block.All(l => l.TrimStart().StartsWith(">", StringComparison.Ordinal)))
                {
                    html.Append("<blockquote style=\"" + _Text + ";border-left:4px solid #d0d7de;padding:0 0 0 12px;color:#59636e\">");
                    html.Append(String.Join("<br>", block.Select(l => Inline(l.TrimStart().Substring(1).TrimStart(), color))));
                    html.Append("</blockquote>");
                }
                else if (block.Count == 1 && block[0].StartsWith("### ", StringComparison.Ordinal))
                {
                    html.Append("<h3 style=\"" + _Heading + ";font-size:18px\">").Append(Inline(block[0].Substring(4), color)).Append("</h3>");
                }
                else if (block.Count == 1 && block[0].StartsWith("## ", StringComparison.Ordinal))
                {
                    html.Append("<h2 style=\"" + _Heading + ";font-size:20px\">").Append(Inline(block[0].Substring(3), color)).Append("</h2>");
                }
                else
                {
                    html.Append("<p class=\"text\" style=\"" + _Text + "\">").Append(String.Join("<br>", block.Select(l => Inline(l, color)))).Append("</p>");
                }
            }
            return html.ToString();
        }

        /// <summary>
        /// The plain-text form of the source for the text part of an email: the normalized source (Markdown reads well as
        /// text) without escapes.
        /// </summary>
        /// <param name="markdown">Source, or null.</param>
        /// <returns>Text.</returns>
        public static string ToText(string? markdown)
        {
            return String.Join("\n\n", Blocks(markdown).Select(b => _Escape.Replace(String.Join("\n", b), "$1")));
        }

        #endregion

        #region Private-Methods

        private static List<List<string>> Blocks(string? markdown)
        {
            string text = (markdown ?? String.Empty).Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
            List<List<string>> blocks = new List<List<string>>();
            foreach (string chunk in Regex.Split(text, "\\n\\s*\\n"))
            {
                List<string> lines = chunk.Split('\n').Select(l => l.TrimEnd()).Where(l => l.Length > 0).ToList();
                if (lines.Count > 0) blocks.Add(lines);
            }
            return blocks;
        }

        private static string Inline(string raw, string color)
        {
            string text = _Escape.Replace(raw, m => ((char)(_EscapeBase + _EscapeCharacters.IndexOf(m.Groups[1].Value[0]))).ToString());
            StringBuilder html = new StringBuilder();
            int position = 0;
            foreach (Match match in _Links.Matches(text))
            {
                html.Append(Emphasis(WebUtility.HtmlEncode(text.Substring(position, match.Index - position))));
                string url = match.Groups[2].Success ? match.Groups[2].Value : match.Groups[3].Value;
                string label = match.Groups[2].Success ? match.Groups[1].Value : url;
                if (label.Length == 0) label = url;
                html.Append("<a class=\"link\" href=\"").Append(WebUtility.HtmlEncode(Restore(url, false))).Append("\" style=\"color:").Append(color).Append("\">")
                    .Append(Emphasis(WebUtility.HtmlEncode(label))).Append("</a>");
                position = match.Index + match.Length;
            }
            html.Append(Emphasis(WebUtility.HtmlEncode(text.Substring(position))));
            return Restore(html.ToString(), true);
        }

        private static string Emphasis(string encoded)
        {
            string result = _Bold.Replace(encoded, "<strong>$1</strong>");
            result = _Strike.Replace(result, "<s>$1</s>");
            result = _ItalicStar.Replace(result, "<em>$1</em>");
            result = _ItalicUnderscore.Replace(result, "<em>$1</em>");
            return result;
        }

        private static string Restore(string text, bool encode)
        {
            StringBuilder builder = new StringBuilder(text.Length);
            foreach (char c in text)
            {
                int index = c - _EscapeBase;
                if (index >= 0 && index < _EscapeCharacters.Length)
                {
                    string literal = _EscapeCharacters[index].ToString();
                    builder.Append(encode ? WebUtility.HtmlEncode(literal) : literal);
                }
                else builder.Append(c);
            }
            return builder.ToString();
        }

        #endregion
    }
}
