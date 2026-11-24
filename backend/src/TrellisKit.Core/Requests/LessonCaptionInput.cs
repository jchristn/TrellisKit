namespace TrellisKit.Core.Requests
{
    using System;

    /// <summary>
    /// A caption track in a lesson write.
    /// </summary>
    public class LessonCaptionInput
    {
        #region Public-Members

        /// <summary>
        /// BCP 47 language tag.
        /// </summary>
        public string? Language { get; set; } = null;

        /// <summary>
        /// Label (1 to 40 characters).
        /// </summary>
        public string? Label { get; set; } = null;

        /// <summary>
        /// Ready Document media of type text/vtt.
        /// </summary>
        public string? MediaId { get; set; } = null;

        #endregion
    }
}
