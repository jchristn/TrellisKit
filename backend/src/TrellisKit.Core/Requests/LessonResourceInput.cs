namespace TrellisKit.Core.Requests
{
    using System;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// A resource in a lesson write.
    /// </summary>
    public class LessonResourceInput
    {
        #region Public-Members

        /// <summary>
        /// Kind.
        /// </summary>
        public LessonResourceKindEnum? Kind { get; set; } = null;

        /// <summary>
        /// Title (1 to 120 characters).
        /// </summary>
        public string? Title { get; set; } = null;

        /// <summary>
        /// Ready Document media (Document).
        /// </summary>
        public string? MediaId { get; set; } = null;

        /// <summary>
        /// https URL (Link).
        /// </summary>
        public string? Url { get; set; } = null;

        /// <summary>
        /// Text up to 300 characters (Reference).
        /// </summary>
        public string? Text { get; set; } = null;

        #endregion
    }
}
