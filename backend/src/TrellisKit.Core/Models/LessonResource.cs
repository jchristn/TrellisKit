namespace TrellisKit.Core.Models
{
    using System;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// A document, link, or reference attached to a lesson.
    /// </summary>
    public class LessonResource
    {
        #region Public-Members

        /// <summary>
        /// Identifier (lrs_).
        /// </summary>
        public string Id { get; set; } = String.Empty;

        /// <summary>
        /// Lesson.
        /// </summary>
        public string LessonId { get; set; } = String.Empty;

        /// <summary>
        /// Kind.
        /// </summary>
        public LessonResourceKindEnum Kind { get; set; } = LessonResourceKindEnum.Link;

        /// <summary>
        /// Title (1 to 120 characters).
        /// </summary>
        public string Title { get; set; } = String.Empty;

        /// <summary>
        /// Document media (Document resources), or null.
        /// </summary>
        public string? MediaId { get; set; } = null;

        /// <summary>
        /// https URL (Link resources), or null.
        /// </summary>
        public string? Url { get; set; } = null;

        /// <summary>
        /// Text up to 300 characters (Reference resources), or null.
        /// </summary>
        public string? Text { get; set; } = null;

        /// <summary>
        /// Position in the list.
        /// </summary>
        public int Position { get; set; } = 0;

        #endregion
    }
}
