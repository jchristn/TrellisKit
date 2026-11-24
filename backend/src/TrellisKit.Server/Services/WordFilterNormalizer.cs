namespace TrellisKit.Server.Services
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Text;

    /// <summary>
    /// Word filter normalization (contract section 9.4), applied identically to text and terms: compatibility
    /// decomposition (NFKD, which with mark removal is equivalent to NFKC plus diacritic removal), removal of
    /// combining marks (diacritics), zero-width, soft hyphen, and bidirectional control characters, invariant
    /// lowercase with sharp s folded to "ss" and final sigma to sigma, the substitution map 0=o, 1=i, 3=e, 4=a, 5=s,
    /// 7=t, @=a, $=s, every whitespace run collapsed to one space, and runs of the same letter longer than two
    /// collapsed to two ("aaaa" becomes "aa"). Thread safety: stateless; safe to call concurrently.
    /// </summary>
    public static class WordFilterNormalizer
    {
        #region Public-Methods

        /// <summary>
        /// Normalize text and keep the original span of each normalized character.
        /// </summary>
        /// <param name="text">Text, or null.</param>
        /// <returns>Normalized text with index maps.</returns>
        public static WordFilterText Normalize(string? text)
        {
            StringBuilder sb = new StringBuilder();
            List<int> starts = new List<int>();
            List<int> ends = new List<int>();
            if (!String.IsNullOrEmpty(text))
            {
                int i = 0;
                while (i < text.Length)
                {
                    int width = 1;
                    Rune rune;
                    if (Rune.TryGetRuneAt(text, i, out Rune parsed))
                    {
                        rune = parsed;
                        width = parsed.Utf16SequenceLength;
                    }
                    else
                    {
                        i++;
                        continue;
                    }
                    if (!IsIgnorable(rune.Value)) AppendFolded(rune, i, i + width, sb, starts, ends);
                    i += width;
                }
            }
            return Collapse(sb.ToString(), starts, ends);
        }

        /// <summary>
        /// Normalized form of a term: <see cref="Normalize"/> then trimmed.
        /// </summary>
        /// <param name="term">Term, or null.</param>
        /// <returns>Normalized term (empty when nothing is left).</returns>
        public static string NormalizeTerm(string? term)
        {
            return Normalize(term).Text.Trim();
        }

        /// <summary>
        /// Whether the character at a position of normalized text is a letter or digit (surrogate-aware).
        /// </summary>
        /// <param name="text">Text.</param>
        /// <param name="index">Index of a UTF-16 unit.</param>
        /// <returns>True for a letter or digit; false outside the text.</returns>
        public static bool IsWordChar(string text, int index)
        {
            if (text == null || index < 0 || index >= text.Length) return false;
            if (Char.IsLowSurrogate(text[index]) && index > 0 && Char.IsHighSurrogate(text[index - 1])) index--;
            return Char.IsLetterOrDigit(text, index);
        }

        #endregion

        #region Private-Methods

        private static bool IsIgnorable(int value)
        {
            return (value >= 0x200B && value <= 0x200F) || (value >= 0x202A && value <= 0x202E) || (value >= 0x2060 && value <= 0x2064) ||
                (value >= 0x2066 && value <= 0x2069) || value == 0xFEFF || value == 0x00AD || value == 0x034F || value == 0x180E;
        }

        private static void AppendFolded(Rune rune, int start, int end, StringBuilder sb, List<int> starts, List<int> ends)
        {
            string decomposed;
            try
            {
                decomposed = rune.ToString().Normalize(NormalizationForm.FormKD);
            }
            catch (ArgumentException)
            {
                decomposed = rune.ToString();
            }
            foreach (Rune part in decomposed.EnumerateRunes())
            {
                UnicodeCategory category = Rune.GetUnicodeCategory(part);
                if (category == UnicodeCategory.NonSpacingMark || category == UnicodeCategory.SpacingCombiningMark || category == UnicodeCategory.EnclosingMark) continue;
                if (IsIgnorable(part.Value)) continue;
                string lower = Fold(part);
                foreach (char c in lower)
                {
                    sb.Append(Substitute(c));
                    starts.Add(start);
                    ends.Add(end);
                }
            }
        }

        private static string Fold(Rune rune)
        {
            if (rune.Value == 0x00DF || rune.Value == 0x1E9E) return "ss";
            if (Rune.IsWhiteSpace(rune)) return " ";
            Rune lower = Rune.ToLowerInvariant(rune);
            if (lower.Value == 0x03C2) return "σ";
            return lower.ToString();
        }

        private static char Substitute(char c)
        {
            switch (c)
            {
                case '0': return 'o';
                case '1': return 'i';
                case '3': return 'e';
                case '4': return 'a';
                case '5': return 's';
                case '7': return 't';
                case '@': return 'a';
                case '$': return 's';
                default: return c;
            }
        }

        private static WordFilterText Collapse(string text, List<int> starts, List<int> ends)
        {
            StringBuilder sb = new StringBuilder(text.Length);
            List<int> outStarts = new List<int>(text.Length);
            List<int> outEnds = new List<int>(text.Length);
            int run = 0;
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                bool same = sb.Length > 0 && sb[sb.Length - 1] == c;
                if (c == ' ')
                {
                    if (same) continue;
                    run = 1;
                }
                else if (Char.IsLetter(c))
                {
                    run = same ? run + 1 : 1;
                    if (run > 2) continue;
                }
                else
                {
                    run = 1;
                }
                sb.Append(c);
                outStarts.Add(starts[i]);
                outEnds.Add(ends[i]);
            }
            return new WordFilterText(sb.ToString(), outStarts.ToArray(), outEnds.ToArray());
        }

        #endregion
    }
}
