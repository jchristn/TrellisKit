namespace TrellisKit.Core.Responses
{
    using System;

    /// <summary>
    /// A Continue watching item.
    /// </summary>
    public class ContinueWatchingItem
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

        #endregion
    }
}
