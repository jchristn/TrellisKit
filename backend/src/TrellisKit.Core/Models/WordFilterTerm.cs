namespace TrellisKit.Core.Models
{
    using System;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// One word filter term (contract section 9.4).
    /// </summary>
    public class WordFilterTerm
    {
        #region Public-Members

        /// <summary>
        /// Identifier (wft_ prefix).
        /// </summary>
        public string Id { get; set; } = String.Empty;

        /// <summary>
        /// Term as the administrator entered it (trimmed).
        /// </summary>
        public string Term { get; set; } = String.Empty;

        /// <summary>
        /// Normalized term used for matching and uniqueness.
        /// </summary>
        public string NormalizedTerm { get; set; } = String.Empty;

        /// <summary>
        /// Word (whole words only) or Substring (anywhere).
        /// </summary>
        public WordFilterMatchTypeEnum MatchType { get; set; } = WordFilterMatchTypeEnum.Word;

        /// <summary>
        /// Block (reject the write) or Flag (accept and report).
        /// </summary>
        public WordFilterActionEnum Action { get; set; } = WordFilterActionEnum.Block;

        /// <summary>
        /// Optional administrator note, or null.
        /// </summary>
        public string? Note { get; set; } = null;

        /// <summary>
        /// Administrator who created the term, or null for the admin token.
        /// </summary>
        public string? CreatedByUserId { get; set; } = null;

        /// <summary>
        /// Created (UTC).
        /// </summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Last change (UTC).
        /// </summary>
        public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;

        #endregion
    }
}
