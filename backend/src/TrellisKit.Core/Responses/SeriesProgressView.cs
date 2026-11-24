namespace TrellisKit.Core.Responses
{
    using System;

    /// <summary>
    /// A member's progress on a series.
    /// </summary>
    public class SeriesProgressView
    {
        #region Public-Members

        /// <summary>
        /// Completed published lessons.
        /// </summary>
        public int CompletedLessons { get; set; } = 0;

        /// <summary>
        /// Completed published lessons divided by published lessons.
        /// </summary>
        public int Percent { get; set; } = 0;

        /// <summary>
        /// Most recently watched lesson, or null.
        /// </summary>
        public string? LastLessonId { get; set; } = null;

        /// <summary>
        /// When, or null.
        /// </summary>
        public DateTime? LastWatchedUtc { get; set; } = null;

        #endregion
    }
}
