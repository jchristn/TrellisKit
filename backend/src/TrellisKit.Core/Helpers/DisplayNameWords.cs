namespace TrellisKit.Core.Helpers
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Text;

    /// <summary>
    /// Splits display names into the lowercased search words stored in displaynamewords (contract section 3.4).
    /// Thread safety: stateless; safe to call concurrently.
    /// </summary>
    public static class DisplayNameWords
    {
        #region Public-Members

        /// <summary>
        /// Longest stored word; longer words are cut to this length.
        /// </summary>
        public const int MaxWordLength = 30;

        /// <summary>
        /// Most words stored per member.
        /// </summary>
        public const int MaxWords = 20;

        #endregion

        #region Public-Methods

        /// <summary>
        /// The distinct words of a display name: Unicode NFC, lowercased (invariant), split on every character that is
        /// not a letter, digit, or combining mark, each cut to <see cref="MaxWordLength"/> Unicode scalar values, in order of
        /// first appearance, at most <see cref="MaxWords"/>.
        /// </summary>
        /// <param name="displayName">Display name, or null.</param>
        /// <returns>Words (empty for null or blank).</returns>
        public static List<string> Split(string? displayName)
        {
            List<string> words = new List<string>();
            if (String.IsNullOrWhiteSpace(displayName)) return words;
            string text;
            try
            {
                text = displayName.Normalize(NormalizationForm.FormC).ToLowerInvariant();
            }
            catch (ArgumentException)
            {
                text = displayName.ToLowerInvariant();
            }
            HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
            StringBuilder current = new StringBuilder();
            List<Rune> runes = new List<Rune>();
            foreach (Rune rune in text.EnumerateRunes()) runes.Add(rune);
            runes.Add(new Rune(' '));
            int length = 0;
            foreach (Rune rune in runes)
            {
                if (Rune.IsLetterOrDigit(rune) || IsMark(rune))
                {
                    if (length < MaxWordLength) current.Append(rune.ToString());
                    length++;
                    continue;
                }
                if (current.Length == 0) continue;
                string word = current.ToString();
                current.Clear();
                length = 0;
                if (seen.Add(word)) words.Add(word);
                if (words.Count >= MaxWords) break;
            }
            return words;
        }

        #endregion

        #region Private-Methods

        private static bool IsMark(Rune rune)
        {
            UnicodeCategory category = Rune.GetUnicodeCategory(rune);
            return category == UnicodeCategory.NonSpacingMark || category == UnicodeCategory.SpacingCombiningMark;
        }

        #endregion
    }
}
