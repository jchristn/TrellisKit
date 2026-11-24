namespace TrellisKit.Core.Requests
{
    using System;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// Typed filters of GET /api/v1/admin/feed (contract section 10.1).
    /// </summary>
    public class AdminFeedFilter
    {
        #region Public-Members

        /// <summary>
        /// Any FeedPost state or Deleted.
        /// </summary>
        public FeedPostStateEnum? State { get; set; } = null;

        /// <summary>
        /// Author.
        /// </summary>
        public string? AuthorUserId { get; set; } = null;

        /// <summary>
        /// Pinned or not.
        /// </summary>
        public bool? Pinned { get; set; } = null;

        /// <summary>
        /// Lower bound on publishedUtc, or createdUtc for unpublished posts (filters.publishedAfterUtc, alias fromUtc).
        /// The standard createdAfterUtc window is separate and applies to createdUtc.
        /// </summary>
        public DateTime? FromUtc { get; set; } = null;

        /// <summary>
        /// Upper bound on publishedUtc, or createdUtc for unpublished posts (filters.publishedBeforeUtc, alias toUtc).
        /// The standard createdBeforeUtc window is separate and applies to createdUtc.
        /// </summary>
        public DateTime? ToUtc { get; set; } = null;

        /// <summary>
        /// Only posts with this audience (filters.audience), or null for any.
        /// </summary>
        public ContentAudienceEnum? Audience { get; set; } = null;

        /// <summary>
        /// Current time, for the Scheduled and Processing states.
        /// </summary>
        public DateTime NowUtc { get; set; } = DateTime.UtcNow;

        #endregion
    }
}
