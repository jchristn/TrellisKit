namespace TrellisKit.Core.Responses
{
    using System;

    /// <summary>
    /// GET /api/v1/admin/feed/{id} (contract section 10.1): the FeedPost in any state (including Deleted) with bounded
    /// previews of its revisions and moderation actions (at most 5 each, with enumeration pointers) and its report
    /// counts.
    /// </summary>
    public class AdminFeedPostDetail : FeedPostView
    {
        #region Public-Members

        /// <summary>
        /// Soft delete time, or null.
        /// </summary>
        public DateTime? DeletedUtc { get; set; } = null;

        /// <summary>
        /// Who deleted it, or null.
        /// </summary>
        public string? DeletedByUserId { get; set; } = null;

        /// <summary>
        /// Open reports (feedposts.openreportcount).
        /// </summary>
        public int OpenReportCount { get; set; } = 0;

        /// <summary>
        /// Newest revisions (at most 5).
        /// </summary>
        public BoundedPreview<FeedPostRevisionView> Revisions { get; set; } = new BoundedPreview<FeedPostRevisionView>();

        /// <summary>
        /// Newest moderation actions (at most 5). Items are filled by the moderation feature; until then the preview
        /// carries the count only.
        /// </summary>
        public BoundedPreview<object> ModerationActions { get; set; } = new BoundedPreview<object>();

        /// <summary>
        /// Report counts.
        /// </summary>
        public ReportSummaryView ReportSummary { get; set; } = new ReportSummaryView();

        #endregion
    }
}
