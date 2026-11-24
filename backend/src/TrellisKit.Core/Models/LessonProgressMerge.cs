namespace TrellisKit.Core.Models
{
    using System;

    /// <summary>
    /// Input of one progress merge (docs/PHASE4_CONTRACT.md section 5.5), already clamped by the service.
    /// </summary>
    public class LessonProgressMerge
    {
        #region Public-Members

        /// <summary>
        /// Lesson.
        /// </summary>
        public string LessonId { get; set; } = String.Empty;

        /// <summary>
        /// Member.
        /// </summary>
        public string UserId { get; set; } = String.Empty;

        /// <summary>
        /// Clamped position (0 when inside the resume tail).
        /// </summary>
        public double PositionSeconds { get; set; } = 0;

        /// <summary>
        /// Clamped position reached by this update (for the furthest position).
        /// </summary>
        public double ReachedSeconds { get; set; } = 0;

        /// <summary>
        /// Clamped seconds played since the last update.
        /// </summary>
        public double PlayedSeconds { get; set; } = 0;

        /// <summary>
        /// Lesson duration.
        /// </summary>
        public double DurationSeconds { get; set; } = 0;

        /// <summary>
        /// Completion threshold.
        /// </summary>
        public int ThresholdPercent { get; set; } = 90;

        /// <summary>
        /// Client time of the update, or null.
        /// </summary>
        public DateTime? ClientUpdatedUtc { get; set; } = null;

        /// <summary>
        /// Server time.
        /// </summary>
        public DateTime NowUtc { get; set; } = DateTime.UtcNow;

        #endregion
    }
}
