namespace TrellisKit.Core.Models
{
    using System;

    /// <summary>
    /// A member's progress on one series, computed in one grouped query (not stored).
    /// </summary>
    public class SeriesProgressSummary
    {
        #region Public-Members

        /// <summary>
        /// Series.
        /// </summary>
        public string SeriesId { get; set; } = String.Empty;

        /// <summary>
        /// Completed published lessons.
        /// </summary>
        public int CompletedLessons { get; set; } = 0;

        /// <summary>
        /// Published lessons.
        /// </summary>
        public int PublishedLessons { get; set; } = 0;

        /// <summary>
        /// Published lessons with progress.
        /// </summary>
        public int StartedLessons { get; set; } = 0;

        /// <summary>
        /// Most recently watched lesson, or null.
        /// </summary>
        public string? LastLessonId { get; set; } = null;

        /// <summary>
        /// When it was watched, or null.
        /// </summary>
        public DateTime? LastWatchedUtc { get; set; } = null;

        #endregion
    }
}
