namespace TrellisKit.Core.Responses
{
    using System;

    /// <summary>
    /// A lesson in the library metrics.
    /// </summary>
    public class LessonMetricView
    {
        #region Public-Members

        /// <summary>
        /// Lesson.
        /// </summary>
        public string Id { get; set; } = String.Empty;

        /// <summary>
        /// Title.
        /// </summary>
        public string Title { get; set; } = String.Empty;

        /// <summary>
        /// Series.
        /// </summary>
        public string SeriesId { get; set; } = String.Empty;

        /// <summary>
        /// Plays in the window.
        /// </summary>
        public long Plays { get; set; } = 0;

        /// <summary>
        /// Completions in the window.
        /// </summary>
        public long Completions { get; set; } = 0;

        #endregion
    }
}
