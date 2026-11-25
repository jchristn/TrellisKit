namespace TrellisKit.Server.Services
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Result of a word filter check that passed (no Block match): the Flag matches to record once the content is
    /// saved (<see cref="WordFilterService.RecordFlagsAsync(WordFilterResult?, TrellisKit.Core.Enums.TargetTypeEnum, string, string?, System.Threading.CancellationToken)"/>).
    /// </summary>
    public class WordFilterResult
    {
        #region Public-Members

        /// <summary>
        /// Flag matches, one per distinct field and term. Never null.
        /// </summary>
        public List<WordFilterFieldMatch> Flags
        {
            get { return _Flags; }
            set { _Flags = value ?? new List<WordFilterFieldMatch>(); }
        }

        /// <summary>
        /// Whether any Flag term matched.
        /// </summary>
        public bool HasFlags
        {
            get { return _Flags.Count > 0; }
        }

        /// <summary>
        /// The start of the first field with a Flag match (at most <see cref="MaxExcerptLength"/> characters, line
        /// breaks folded to spaces), shown to administrators in the alert. Null when nothing was flagged.
        /// </summary>
        public string? Excerpt
        {
            get { return _Excerpt; }
            set { _Excerpt = String.IsNullOrWhiteSpace(value) ? null : Fold(value); }
        }

        /// <summary>
        /// Longest <see cref="Excerpt"/>, in characters.
        /// </summary>
        public const int MaxExcerptLength = 160;

        #endregion

        #region Private-Members

        private List<WordFilterFieldMatch> _Flags = new List<WordFilterFieldMatch>();
        private string? _Excerpt = null;

        #endregion

        #region Private-Methods

        private static string Fold(string value)
        {
            string text = value.Replace('\r', ' ').Replace('\n', ' ').Trim();
            return text.Length > MaxExcerptLength ? text.Substring(0, MaxExcerptLength) : text;
        }

        #endregion
    }
}
