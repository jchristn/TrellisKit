namespace TrellisKit.Core.Models
{
    using System;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// One row of a member's content history: a feed post or comment identifier.
    /// </summary>
    public class ModerationContentRow
    {
        #region Public-Members

        /// <summary>
        /// FeedPost or Comment.
        /// </summary>
        public TargetTypeEnum TargetType { get; set; } = TargetTypeEnum.FeedPost;

        /// <summary>
        /// Identifier.
        /// </summary>
        public string TargetId { get; set; } = String.Empty;

        /// <summary>
        /// Created (UTC).
        /// </summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Reports on the item (any status).
        /// </summary>
        public long ReportCount { get; set; } = 0;

        #endregion
    }
}
