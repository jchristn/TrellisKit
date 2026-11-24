namespace TrellisKit.Core.Responses
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// A member's progress on a lesson (docs/PHASE4_CONTRACT.md section 2.6).
    /// </summary>
    public class LessonProgressView
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
        /// Resume point.
        /// </summary>
        public double PositionSeconds { get; set; } = 0;

        /// <summary>
        /// Furthest position.
        /// </summary>
        public double MaxPositionSeconds { get; set; } = 0;

        /// <summary>
        /// Seconds actually played.
        /// </summary>
        public double WatchedSeconds { get; set; } = 0;

        /// <summary>
        /// round(100 * min(maxPositionSeconds, watchedSeconds) / duration).
        /// </summary>
        public int Percent { get; set; } = 0;

        /// <summary>
        /// Completed (sticky).
        /// </summary>
        public bool Completed { get; set; } = false;

        /// <summary>
        /// When completed.
        /// </summary>
        public DateTime? CompletedUtc { get; set; } = null;

        /// <summary>
        /// Last watched.
        /// </summary>
        public DateTime LastWatchedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Labels.
        /// </summary>
        public List<string> Labels { get; set; } = new List<string>();

        /// <summary>
        /// Tags.
        /// </summary>
        public Dictionary<string, string> Tags { get; set; } = new Dictionary<string, string>();

        #endregion
    }
}
