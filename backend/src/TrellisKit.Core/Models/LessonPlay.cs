namespace TrellisKit.Core.Models
{
    using System;

    /// <summary>
    /// One playback session of a lesson (lessonplays), for metrics and viewing history.
    /// </summary>
    public class LessonPlay
    {
        #region Public-Members

        /// <summary>
        /// Identifier.
        /// </summary>
        public string Id { get; set; } = String.Empty;

        /// <summary>
        /// Lesson.
        /// </summary>
        public string LessonId { get; set; } = String.Empty;

        /// <summary>
        /// Member, or null.
        /// </summary>
        public string? UserId { get; set; } = null;

        /// <summary>
        /// Client session identifier.
        /// </summary>
        public string SessionId { get; set; } = String.Empty;

        /// <summary>
        /// Platform (web, ios, android).
        /// </summary>
        public string Platform { get; set; } = "web";

        /// <summary>
        /// Seconds played in the session.
        /// </summary>
        public double PlayedSeconds { get; set; } = 0;

        /// <summary>
        /// First update of the session.
        /// </summary>
        public DateTime StartedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Last update of the session.
        /// </summary>
        public DateTime LastUtc { get; set; } = DateTime.UtcNow;

        #endregion
    }
}
