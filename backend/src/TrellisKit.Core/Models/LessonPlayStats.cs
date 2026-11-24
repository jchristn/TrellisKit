namespace TrellisKit.Core.Models
{
    using System;

    /// <summary>
    /// Playback sessions of a lesson, for one member or for everyone.
    /// </summary>
    public class LessonPlayStats
    {
        #region Public-Members

        /// <summary>
        /// Lesson.
        /// </summary>
        public string LessonId { get; set; } = String.Empty;

        /// <summary>
        /// Member, or null for every member.
        /// </summary>
        public string? UserId { get; set; } = null;

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
