namespace TrellisKit.Core.Models
{
    using System;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// One word filter match on a write. Pruned after 90 days.
    /// </summary>
    public class WordFilterHit
    {
        #region Public-Members

        /// <summary>
        /// Identifier (wfh_ prefix).
        /// </summary>
        public string Id { get; set; } = String.Empty;

        /// <summary>
        /// Matched term.
        /// </summary>
        public string TermId { get; set; } = String.Empty;

        /// <summary>
        /// Kind of content written.
        /// </summary>
        public TargetTypeEnum TargetType { get; set; } = TargetTypeEnum.FeedPost;

        /// <summary>
        /// Saved content, or null for a blocked write.
        /// </summary>
        public string? TargetId { get; set; } = null;

        /// <summary>
        /// Field name, for example body, title, or mediaIds[0].altText.
        /// </summary>
        public string Field { get; set; } = String.Empty;

        /// <summary>
        /// Action of the term at the time of the match.
        /// </summary>
        public WordFilterActionEnum Action { get; set; } = WordFilterActionEnum.Block;

        /// <summary>
        /// When the match happened (UTC).
        /// </summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        #endregion
    }
}
