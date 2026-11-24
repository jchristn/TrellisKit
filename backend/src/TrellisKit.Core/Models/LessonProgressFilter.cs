namespace TrellisKit.Core.Models
{
    using System;

    /// <summary>
    /// Filter of the progress enumerations.
    /// </summary>
    public class LessonProgressFilter
    {
        #region Public-Members

        /// <summary>
        /// Member, or null.
        /// </summary>
        public string? UserId { get; set; } = null;

        /// <summary>
        /// Lesson, or null.
        /// </summary>
        public string? LessonId { get; set; } = null;

        /// <summary>
        /// Series, or null.
        /// </summary>
        public string? SeriesId { get; set; } = null;

        /// <summary>
        /// Completed state, or null.
        /// </summary>
        public bool? Completed { get; set; } = null;

        /// <summary>
        /// In progress, not completed, lesson still openable (Continue watching).
        /// </summary>
        public bool InProgressOnly { get; set; } = false;

        /// <summary>
        /// Archived lessons stay openable when archived after this time.
        /// </summary>
        public DateTime? ArchivedCutoffUtc { get; set; } = null;

        #endregion
    }
}
