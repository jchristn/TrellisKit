namespace TrellisKit.Server.Services
{
    using System;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// One word filter match in a named field of a write.
    /// </summary>
    public class WordFilterFieldMatch
    {
        #region Public-Members

        /// <summary>
        /// Field name, for example title, body, mediaIds[0].altText, labels[2], or tags.level.
        /// </summary>
        public string Field { get; set; } = String.Empty;

        /// <summary>
        /// Matched term identifier.
        /// </summary>
        public string TermId { get; set; } = String.Empty;

        /// <summary>
        /// Matched term as entered (for moderators; never echoed to the author).
        /// </summary>
        public string Term { get; set; } = String.Empty;

        /// <summary>
        /// Action of the term.
        /// </summary>
        public WordFilterActionEnum Action { get; set; } = WordFilterActionEnum.Flag;

        #endregion
    }
}
