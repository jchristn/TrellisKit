namespace TrellisKit.Core.Integrations.Implementations
{
    using System;
    using System.Collections.Generic;
    using System.Text;
    using System.Text.RegularExpressions;

    /// <summary>
    /// Minimal WebVTT validation and text extraction (DocumentAtom has no WebVTT processor): the file must start with
    /// WEBVTT (after an optional byte order mark), every cue timing line must be well formed, and the extracted text
    /// drops the header, NOTE, STYLE, and REGION blocks, cue identifiers, timings, and inline tags.
    /// Thread safety: stateless; safe to call concurrently.
    /// </summary>
    public static class WebVttParser
    {
        #region Private-Members

        private static readonly Regex _Timing = new Regex(@"^\s*(\d{1,}:)?\d{2}:\d{2}\.\d{3}\s+-->\s+(\d{1,}:)?\d{2}:\d{2}\.\d{3}(\s.*)?$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
        private static readonly Regex _Tags = new Regex(@"<[^>]{0,200}>", RegexOptions.Compiled | RegexOptions.CultureInvariant);

        #endregion

        #region Public-Methods

        /// <summary>
        /// Whether a text is a well-formed WebVTT document.
        /// </summary>
        /// <param name="text">Text.</param>
        /// <param name="error">Why it is not, or null.</param>
        /// <returns>True when valid.</returns>
        public static bool Validate(string? text, out string? error)
        {
            error = null;
            string body = (text ?? String.Empty).TrimStart('﻿');
            List<string> lines = Lines(body);
            if (lines.Count == 0 || !lines[0].StartsWith("WEBVTT", StringComparison.Ordinal) || (lines[0].Length > 6 && lines[0][6] != ' ' && lines[0][6] != '\t'))
            {
                error = "A WebVTT file must start with WEBVTT.";
                return false;
            }
            for (int i = 1; i < lines.Count; i++)
            {
                if (lines[i].Contains("-->", StringComparison.Ordinal) && !_Timing.IsMatch(lines[i]))
                {
                    error = "Line " + (i + 1) + " has a malformed cue timing.";
                    return false;
                }
            }
            return true;
        }

        /// <summary>
        /// The spoken text of a WebVTT document, one cue per line (empty when nothing is spoken).
        /// </summary>
        /// <param name="text">WebVTT text.</param>
        /// <returns>Plain text.</returns>
        public static string ExtractText(string? text)
        {
            List<string> lines = Lines((text ?? String.Empty).TrimStart('﻿'));
            StringBuilder sb = new StringBuilder();
            int i = 0;
            // Skip the header block (WEBVTT line and any header lines until the first blank line).
            while (i < lines.Count && lines[i].Trim().Length > 0) i++;
            string? previous = null;
            while (i < lines.Count)
            {
                while (i < lines.Count && lines[i].Trim().Length == 0) i++;
                if (i >= lines.Count) break;
                List<string> block = new List<string>();
                while (i < lines.Count && lines[i].Trim().Length > 0) block.Add(lines[i++]);
                string first = block[0].TrimStart();
                if (first.StartsWith("NOTE", StringComparison.Ordinal) || first.StartsWith("STYLE", StringComparison.Ordinal) || first.StartsWith("REGION", StringComparison.Ordinal)) continue;
                int timing = block.FindIndex(l => l.Contains("-->", StringComparison.Ordinal));
                if (timing < 0) continue;
                List<string> cue = new List<string>();
                for (int k = timing + 1; k < block.Count; k++)
                {
                    string clean = System.Net.WebUtility.HtmlDecode(_Tags.Replace(block[k], String.Empty)).Trim();
                    if (clean.Length > 0) cue.Add(clean);
                }
                string line = String.Join(" ", cue);
                if (line.Length == 0 || String.Equals(line, previous, StringComparison.Ordinal)) continue;
                sb.Append(line).Append('\n');
                previous = line;
            }
            return sb.ToString().Trim();
        }

        #endregion

        #region Private-Methods

        private static List<string> Lines(string text)
        {
            return new List<string>(text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'));
        }

        #endregion
    }
}
