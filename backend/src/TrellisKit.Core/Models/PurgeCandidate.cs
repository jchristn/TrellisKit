namespace TrellisKit.Core.Models
{
    using System;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// Content due for purging (removed or deleted longer than moderation.retainRemovedDays with no Open report).
    /// </summary>
    public class PurgeCandidate
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

        #endregion
    }
}
