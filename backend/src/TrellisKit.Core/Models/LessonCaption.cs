namespace TrellisKit.Core.Models
{
    using System;

    /// <summary>
    /// A WebVTT caption track of a lesson.
    /// </summary>
    public class LessonCaption
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
        /// BCP 47 language tag.
        /// </summary>
        public string Language { get; set; } = String.Empty;

        /// <summary>
        /// Display label.
        /// </summary>
        public string Label { get; set; } = String.Empty;

        /// <summary>
        /// Document media (text/vtt).
        /// </summary>
        public string MediaId { get; set; } = String.Empty;

        /// <summary>
        /// Position in the list.
        /// </summary>
        public int Position { get; set; } = 0;

        #endregion
    }
}
