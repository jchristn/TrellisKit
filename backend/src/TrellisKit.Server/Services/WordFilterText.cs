namespace TrellisKit.Server.Services
{
    using System;

    /// <summary>
    /// Text normalized for word filter matching, with the original span of every normalized character.
    /// Thread safety: immutable after construction.
    /// </summary>
    public class WordFilterText
    {
        #region Public-Members

        /// <summary>
        /// Normalized text.
        /// </summary>
        public string Text { get; }

        /// <summary>
        /// For each normalized UTF-16 unit, the start index of the original character it came from.
        /// </summary>
        public int[] OriginalStarts { get; }

        /// <summary>
        /// For each normalized UTF-16 unit, the end index (exclusive) of the original character it came from.
        /// </summary>
        public int[] OriginalEnds { get; }

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="text">Normalized text.</param>
        /// <param name="originalStarts">Start indexes (same length as the text).</param>
        /// <param name="originalEnds">End indexes (same length as the text).</param>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        /// <exception cref="ArgumentException">The lengths differ.</exception>
        public WordFilterText(string text, int[] originalStarts, int[] originalEnds)
        {
            Text = text ?? throw new ArgumentNullException(nameof(text));
            OriginalStarts = originalStarts ?? throw new ArgumentNullException(nameof(originalStarts));
            OriginalEnds = originalEnds ?? throw new ArgumentNullException(nameof(originalEnds));
            if (originalStarts.Length != text.Length || originalEnds.Length != text.Length) throw new ArgumentException("Index maps must match the text length.");
        }

        #endregion
    }
}
