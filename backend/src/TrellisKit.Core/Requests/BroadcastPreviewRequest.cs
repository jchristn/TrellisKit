namespace TrellisKit.Core.Requests
{
    using System;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// POST /api/v1/admin/broadcasts/segments/preview body: a saved segment id or an inline segment, and the category
    /// the channel counts assume (default Optional).
    /// </summary>
    public class BroadcastPreviewRequest
    {
        #region Public-Members

        /// <summary>
        /// Saved segment, or null.
        /// </summary>
        public string? SegmentId { get; set; } = null;

        /// <summary>
        /// Inline segment, or null (with neither, everyone the access model allows).
        /// </summary>
        public BroadcastSegmentRequest? Segment { get; set; } = null;

        /// <summary>
        /// Category for the channel counts (Essential ignores the Announcements preferences).
        /// </summary>
        public BroadcastCategoryEnum? Category { get; set; } = null;

        #endregion
    }
}
