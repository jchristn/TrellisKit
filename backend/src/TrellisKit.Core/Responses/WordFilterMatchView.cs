namespace TrellisKit.Core.Responses
{
    using System;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// One word filter match in a text, with its position in the original (un-normalized) text in UTF-16 code units.
    /// </summary>
    public class WordFilterMatchView
    {
        #region Public-Members

        /// <summary>
        /// Matched term identifier.
        /// </summary>
        public string TermId { get; set; } = String.Empty;

        /// <summary>
        /// Matched term as entered.
        /// </summary>
        public string Term { get; set; } = String.Empty;

        /// <summary>
        /// Action of the term.
        /// </summary>
        public WordFilterActionEnum Action { get; set; } = WordFilterActionEnum.Block;

        /// <summary>
        /// Start index in the original text.
        /// </summary>
        public int Start { get; set; } = 0;

        /// <summary>
        /// Length in the original text.
        /// </summary>
        public int Length { get; set; } = 0;

        #endregion
    }
}
