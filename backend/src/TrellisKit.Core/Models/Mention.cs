namespace TrellisKit.Core.Models
{
    using System;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// A resolved @handle in a post or comment (contract section 3.3).
    /// </summary>
    public class Mention
    {
        #region Public-Members

        /// <summary>
        /// Identifier (mtn_).
        /// </summary>
        public string Id { get; set; } = String.Empty;

        /// <summary>
        /// Source type.
        /// </summary>
        public TargetTypeEnum SourceType { get; set; } = TargetTypeEnum.FeedPost;

        /// <summary>
        /// Source identifier.
        /// </summary>
        public string SourceId { get; set; } = String.Empty;

        /// <summary>
        /// Mentioned member.
        /// </summary>
        public string MentionedUserId { get; set; } = String.Empty;

        /// <summary>
        /// The handle exactly as written in the body.
        /// </summary>
        public string Handle { get; set; } = String.Empty;

        /// <summary>
        /// Author of the source.
        /// </summary>
        public string AuthorUserId { get; set; } = String.Empty;

        /// <summary>
        /// When the mention notification was sent (or skipped for self), or null while pending.
        /// </summary>
        public DateTime? NotifiedUtc { get; set; } = null;

        /// <summary>
        /// Created (UTC).
        /// </summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        #endregion
    }
}
