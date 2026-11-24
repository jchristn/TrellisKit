namespace TrellisKit.Core.Models
{
    using System;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// A member's progress on a lesson (lessonprogress).
    /// </summary>
    public class LessonProgress
    {
        #region Public-Members

        /// <summary>
        /// Identifier (lpr_).
        /// </summary>
        public string Id { get; set; } = String.Empty;

        /// <summary>
        /// Lesson.
        /// </summary>
        public string LessonId { get; set; } = String.Empty;

        /// <summary>
        /// Member.
        /// </summary>
        public string UserId { get; set; } = String.Empty;

        /// <summary>
        /// Resume point.
        /// </summary>
        public double PositionSeconds { get; set; } = 0;

        /// <summary>
        /// Furthest position reached (monotonic).
        /// </summary>
        public double MaxPositionSeconds { get; set; } = 0;

        /// <summary>
        /// Seconds actually played (capped at 3 times the duration).
        /// </summary>
        public double WatchedSeconds { get; set; } = 0;

        /// <summary>
        /// Whether completed (sticky).
        /// </summary>
        public bool Completed { get; set; } = false;

        /// <summary>
        /// When completed, or null.
        /// </summary>
        public DateTime? CompletedUtc { get; set; } = null;

        /// <summary>
        /// How it was completed, or null.
        /// </summary>
        public LessonCompletionSourceEnum? CompletionSource { get; set; } = null;

        /// <summary>
        /// Last watched.
        /// </summary>
        public DateTime LastWatchedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Client time of the newest accepted update.
        /// </summary>
        public DateTime? ClientUpdatedUtc { get; set; } = null;

        /// <summary>
        /// Created.
        /// </summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Last change.
        /// </summary>
        public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;

        #endregion
    }
}
