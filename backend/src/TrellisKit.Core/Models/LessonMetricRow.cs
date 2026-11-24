namespace TrellisKit.Core.Models
{
    using System;

    /// <summary>
    /// Plays and completions of one lesson in a metrics window.
    /// </summary>
    public class LessonMetricRow
    {
        #region Public-Members

        /// <summary>
        /// Lesson.
        /// </summary>
        public string LessonId { get; set; } = String.Empty;

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
