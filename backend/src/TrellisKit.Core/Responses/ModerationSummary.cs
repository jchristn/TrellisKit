namespace TrellisKit.Core.Responses
{
    using System;

    /// <summary>
    /// GET /api/v1/admin/moderation/summary: open and overdue report counts in the caller's scope (admin Home tiles).
    /// </summary>
    public class ModerationSummary
    {
        #region Public-Members

        /// <summary>
        /// Open reports.
        /// </summary>
        public long Open { get; set; } = 0;

        /// <summary>
        /// Open reports past moderation.reviewTargetHours.
        /// </summary>
        public long Overdue { get; set; } = 0;

        /// <summary>
        /// Targets with Open reports (queue rows).
        /// </summary>
        public long OpenTargets { get; set; } = 0;

        #endregion
    }
}
