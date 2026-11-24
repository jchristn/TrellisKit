namespace TrellisKit.Core.Responses
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// GET /api/v1/admin/metrics/library (aggregates, plus no per-member data).
    /// </summary>
    public class LibraryMetrics
    {
        #region Public-Members

        /// <summary>
        /// Window in days.
        /// </summary>
        public int Days { get; set; } = 30;

        /// <summary>
        /// Plays per day.
        /// </summary>
        public List<DailyCount> PlaysByDay { get; set; } = new List<DailyCount>();

        /// <summary>
        /// Completions per day.
        /// </summary>
        public List<DailyCount> CompletionsByDay { get; set; } = new List<DailyCount>();

        /// <summary>
        /// Members who played a lesson in the window.
        /// </summary>
        public long ActiveLearners { get; set; } = 0;

        /// <summary>
        /// Most played lessons (max 20).
        /// </summary>
        public BoundedPreview<LessonMetricView> TopLessons { get; set; } = new BoundedPreview<LessonMetricView>();

        /// <summary>
        /// Completion rate per series (max 20).
        /// </summary>
        public BoundedPreview<SeriesCompletionView> CompletionRateBySeries { get; set; } = new BoundedPreview<SeriesCompletionView>();

        #endregion
    }
}
