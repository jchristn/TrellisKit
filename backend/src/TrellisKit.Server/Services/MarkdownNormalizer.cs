namespace TrellisKit.Server.Services
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;
    using System.Text;
    using System.Text.RegularExpressions;

    /// <summary>
    /// Server half of the Phase 2 body format (contract section 3): Unicode normalization and cleanup on write,
    /// lengths in Unicode scalar values, plain-text excerpts, mention handle extraction, and the first link. The
    /// stored body stays Markdown source; the server never renders HTML.
    /// Thread safety: stateless; safe to call concurrently.
    /// </summary>
    public static class MarkdownNormalizer
    {
        #region Public-Members

        /// <summary>
        /// Excerpt length in characters (before the ellipsis).
        /// </summary>
        public const int ExcerptLength = 200;

        /// <summary>
        /// Shortest mention handle.
        /// </summary>
        public const int MinHandleLength = 3;

        /// <summary>
        /// Longest mention handle.
        /// </summary>
        public const int MaxHandleLength = 30;

        #endregion

        #region Private-Members

        private static readonly Regex _Mention = new Regex("(?<![A-Za-z0-9_.\\\\])@([A-Za-z0-9_.]+)", RegexOptions.Compiled | RegexOptions.CultureInvariant);
        private static readonly Regex _Url = new Regex("https?://[^\\s<>()\\[\\]\"']+", RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
        private static readonly Regex _Link = new Regex("\\[([^\\]\\n]*)\\]\\(([^)\\s]*)\\)", RegexOptions.Compiled | RegexOptions.CultureInvariant);
        private static readonly Regex _BlockPrefix = new Regex("^\\s*(#{1,6}\\s+|>\\s?|[-*]\\s+|\\d+\\.\\s+)+", RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.Multiline);
        private static readonly Regex _Escape = new Regex("\\\\([*_~\\[\\]()#>\\-@\\\\`|])", RegexOptions.Compiled | RegexOptions.CultureInvariant);
        private static readonly Regex _FencedCode = new Regex("^ {0,3}(`{3,}|~{3,})[^\\n]*\\n?(.*?)(?:^ {0,3}\\1[`~]*[ \\t]*$|\\z)", RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.Multiline | RegexOptions.Singleline);
        private static readonly Regex _InlineCode = new Regex("(?<![`\\\\])(`+)(?!`)(.+?)(?<!`)\\1(?!`)", RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.Singleline);
        private static readonly Regex _Rule = new Regex("^ {0,3}([-*_])(?: *\\1){2,} *$", RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.Multiline);
        private static readonly Regex _TableSeparator = new Regex("^ *\\|? *:?-+:? *(?:\\| *:?-+:? *)*\\|? *$", RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.Multiline);
        private static readonly Regex _TablePipe = new Regex("(?<!\\\\)\\|", RegexOptions.Compiled | RegexOptions.CultureInvariant);
        private static readonly Regex _Underscore = new Regex("(?<![A-Za-z0-9\\\\])_+|_+(?![A-Za-z0-9])", RegexOptions.Compiled | RegexOptions.CultureInvariant);
        private static readonly Regex _ManyNewlines = new Regex("\\n{3,}", RegexOptions.Compiled | RegexOptions.CultureInvariant);
        private static readonly Regex _Whitespace = new Regex("\\s+", RegexOptions.Compiled | RegexOptions.CultureInvariant);

        #endregion

        #region Public-Methods

        /// <summary>
        /// Whether a string is well-formed UTF-16 (no unpaired surrogates), so it encodes as valid UTF-8.
        /// </summary>
        /// <param name="text">Text, or null.</param>
        /// <returns>True when well-formed (null counts as well-formed).</returns>
        public static bool IsWellFormed(string? text)
        {
            if (text == null) return true;
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (Char.IsHighSurrogate(c))
                {
                    if (i + 1 >= text.Length || !Char.IsLowSurrogate(text[i + 1])) return false;
                    i++;
                }
                else if (Char.IsLowSurrogate(c))
                {
                    return false;
                }
            }
            return true;
        }

        /// <summary>
        /// Normalize a body (section 3.2 steps 2 and 3): NFC, newlines to \n, control, zero-width, and bidirectional
        /// override characters removed, trimmed, and runs of three or more newlines collapsed to two.
        /// </summary>
        /// <param name="text">Text, or null.</param>
        /// <returns>Normalized text (empty for null).</returns>
        public static string Normalize(string? text)
        {
            if (String.IsNullOrEmpty(text)) return String.Empty;
            string value = IsWellFormed(text) ? text.Normalize(NormalizationForm.FormC) : text;
            value = value.Replace("\r\n", "\n").Replace('\r', '\n');
            StringBuilder sb = new StringBuilder(value.Length);
            foreach (char c in value)
            {
                if (c == '\n' || c == '\t') { sb.Append(c); continue; }
                if (Char.IsControl(c) || IsInvisible(c)) continue;
                sb.Append(c);
            }
            value = sb.ToString().Trim();
            return _ManyNewlines.Replace(value, "\n\n");
        }

        /// <summary>
        /// Normalize a single-line field (titles, alt text): <see cref="Normalize"/>, then every run of whitespace
        /// (including newlines and tabs) becomes one space.
        /// </summary>
        /// <param name="text">Text, or null.</param>
        /// <returns>Normalized text (empty for null).</returns>
        public static string NormalizeSingleLine(string? text)
        {
            return _Whitespace.Replace(Normalize(text), " ").Trim();
        }

        /// <summary>
        /// Length in Unicode scalar values (how every Phase 2 limit is counted).
        /// </summary>
        /// <param name="text">Text, or null.</param>
        /// <returns>Length.</returns>
        public static int Length(string? text)
        {
            if (String.IsNullOrEmpty(text)) return 0;
            int count = 0;
            for (int i = 0; i < text.Length; i++)
            {
                if (Char.IsHighSurrogate(text[i]) && i + 1 < text.Length && Char.IsLowSurrogate(text[i + 1])) i++;
                count++;
            }
            return count;
        }

        /// <summary>
        /// A body rendered to plain text: markup removed, link text kept, mentions kept as @handle, escapes resolved,
        /// whitespace collapsed.
        /// </summary>
        /// <param name="body">Markdown body, or null.</param>
        /// <returns>Plain text.</returns>
        public static string ToPlainText(string? body)
        {
            if (String.IsNullOrEmpty(body)) return String.Empty;
            // Code is literal: pull fenced blocks and inline spans out first and put their text back unchanged at the end.
            List<string> code = new List<string>();
            string Hold(string value)
            {
                code.Add(value);
                return "\u0001" + (code.Count - 1).ToString(System.Globalization.CultureInfo.InvariantCulture) + "\u0002";
            }
            string text = _FencedCode.Replace(body.Replace("\r\n", "\n"), m => " " + Hold(m.Groups[2].Value) + " ");
            text = _InlineCode.Replace(text, m => Hold(m.Groups[2].Value.Trim()));
            text = _Rule.Replace(text, String.Empty);
            text = _TableSeparator.Replace(text, m => m.Value.Contains('-') && m.Value.Contains('|') ? String.Empty : m.Value);
            text = String.Join("\n", text.Split('\n').Select(line => line.TrimStart().StartsWith('|') ? _TablePipe.Replace(line, " ") : line));
            text = _Link.Replace(text, m => m.Groups[1].Value);
            text = _BlockPrefix.Replace(text, String.Empty);
            text = text.Replace("**", String.Empty).Replace("~~", String.Empty);
            text = Regex.Replace(text, "(?<!\\\\)\\*", String.Empty);
            text = _Underscore.Replace(text, String.Empty);
            text = _Escape.Replace(text, "$1");
            text = Regex.Replace(text, "\u0001(\\d+)\u0002", m => code[Int32.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture)]);
            return _Whitespace.Replace(text, " ").Trim();
        }

        /// <summary>
        /// The excerpt of a body: plain text cut at a word boundary to <paramref name="maxLength"/> characters, with
        /// "..." when truncated.
        /// </summary>
        /// <param name="body">Markdown body, or null.</param>
        /// <param name="maxLength">Maximum characters before the ellipsis. Default 200, minimum 10.</param>
        /// <returns>Excerpt.</returns>
        public static string Excerpt(string? body, int maxLength = ExcerptLength)
        {
            maxLength = Math.Max(10, maxLength);
            string plain = ToPlainText(body);
            if (Length(plain) <= maxLength) return plain;
            StringBuilder sb = new StringBuilder();
            int count = 0;
            for (int i = 0; i < plain.Length && count < maxLength; i++)
            {
                sb.Append(plain[i]);
                if (Char.IsHighSurrogate(plain[i]) && i + 1 < plain.Length) sb.Append(plain[++i]);
                count++;
            }
            string cut = sb.ToString();
            int space = cut.LastIndexOf(' ');
            if (space > maxLength / 2) cut = cut.Substring(0, space);
            return cut.TrimEnd() + "...";
        }

        /// <summary>
        /// Mention handles in a body (section 3.3): "@" plus 3 to 30 of letters, digits, underscore, and period, not
        /// preceded by a letter, digit, underscore, period, or backslash; a trailing period is not part of the handle.
        /// Distinct case-insensitively, in order of first appearance, with the case as first written.
        /// </summary>
        /// <param name="body">Markdown body, or null.</param>
        /// <returns>Handles without the "@".</returns>
        public static List<string> MentionHandles(string? body)
        {
            List<string> handles = new List<string>();
            if (String.IsNullOrEmpty(body)) return handles;
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            // "@name" inside code is code, not a mention (clients never render it as one).
            string text = _InlineCode.Replace(_FencedCode.Replace(body.Replace("\r\n", "\n"), " "), " ");
            foreach (Match match in _Mention.Matches(text))
            {
                string handle = match.Groups[1].Value.TrimEnd('.');
                if (handle.Length < MinHandleLength || handle.Length > MaxHandleLength) continue;
                if (seen.Add(handle)) handles.Add(handle);
            }
            return handles;
        }

        /// <summary>
        /// The first http or https URL in a body (a Markdown link target or a bare URL), with trailing punctuation removed.
        /// </summary>
        /// <param name="body">Markdown body, or null.</param>
        /// <returns>URL, or null.</returns>
        public static string? FirstUrl(string? body)
        {
            if (String.IsNullOrEmpty(body)) return null;
            Match match = _Url.Match(body);
            if (!match.Success) return null;
            string url = match.Value.TrimEnd('.', ',', ';', ':', '!', '?', '*', '_', '~');
            return Uri.TryCreate(url, UriKind.Absolute, out Uri? _) ? url : null;
        }

        #endregion

        #region Private-Methods

        private static bool IsInvisible(char c)
        {
            return (c >= '​' && c <= '‏') || (c >= '‪' && c <= '‮') || (c >= '⁦' && c <= '⁩') || c == '﻿';
        }

        #endregion
    }
}
