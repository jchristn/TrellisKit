namespace TrellisKit.Core.Responses
{
    using System;

    /// <summary>
    /// One lesson in a member's viewing history (staff).
    /// </summary>
    public class LessonViewingRow
    {
        #region Public-Members

        /// <summary>
        /// Lesson.
        /// </summary>
        public LessonSummaryView Lesson { get; set; } = new LessonSummaryView();

        /// <summary>
        /// Progress.
        /// </summary>
        public LessonProgressView Progress { get; set; } = new LessonProgressView();

        /// <summary>
        /// Playback sessions.
        /// </summary>
        public long Plays { get; set; } = 0;

        /// <summary>
        /// Seconds played across sessions.
        /// </summary>
        public double PlayedSeconds { get; set; } = 0;

        #endregion
    }
}
