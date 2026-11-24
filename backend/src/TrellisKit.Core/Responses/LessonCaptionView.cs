namespace TrellisKit.Core.Responses
{
    using System;

    /// <summary>
    /// A caption track.
    /// </summary>
    public class LessonCaptionView
    {
        #region Public-Members

        /// <summary>
        /// BCP 47 language tag.
        /// </summary>
        public string Language { get; set; } = String.Empty;

        /// <summary>
        /// Label.
        /// </summary>
        public string Label { get; set; } = String.Empty;

        /// <summary>
        /// Document media (text/vtt), or null.
        /// </summary>
        public MediaView? Media { get; set; } = null;

        #endregion
    }
}
