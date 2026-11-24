namespace TrellisKit.Core.Models
{
    using System;

    /// <summary>
    /// A chapter marker inside a lesson video.
    /// </summary>
    public class LessonMarker
    {
        #region Public-Members

        /// <summary>
        /// Identifier (lmk_).
        /// </summary>
        public string Id { get; set; } = String.Empty;

        /// <summary>
        /// Lesson.
        /// </summary>
        public string LessonId { get; set; } = String.Empty;

        /// <summary>
        /// Offset in seconds.
        /// </summary>
        public int Seconds { get; set; } = 0;

        /// <summary>
        /// Label (1 to 80 characters).
        /// </summary>
        public string Label { get; set; } = String.Empty;

        #endregion
    }
}
