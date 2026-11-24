namespace TrellisKit.Core.Responses
{
    using System;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// A lesson resource.
    /// </summary>
    public class LessonResourceView
    {
        #region Public-Members

        /// <summary>
        /// Identifier.
        /// </summary>
        public string Id { get; set; } = String.Empty;

        /// <summary>
        /// Kind.
        /// </summary>
        public LessonResourceKindEnum Kind { get; set; } = LessonResourceKindEnum.Link;

        /// <summary>
        /// Title.
        /// </summary>
        public string Title { get; set; } = String.Empty;

        /// <summary>
        /// Document media, or null.
        /// </summary>
        public MediaView? Media { get; set; } = null;

        /// <summary>
        /// Link URL, or null.
        /// </summary>
        public string? Url { get; set; } = null;

        /// <summary>
        /// Reference text, or null.
        /// </summary>
        public string? Text { get; set; } = null;

        #endregion
    }
}
