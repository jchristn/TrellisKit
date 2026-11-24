namespace TrellisKit.Core.Responses
{
    using System;

    /// <summary>
    /// Document media file (PDF or WebVTT).
    /// </summary>
    public class MediaDocumentView
    {
        #region Public-Members

        /// <summary>
        /// File URL (needs the media grant cookie unless public).
        /// </summary>
        public string? Url { get; set; } = null;

        /// <summary>
        /// Original file name, or null.
        /// </summary>
        public string? FileName { get; set; } = null;

        #endregion
    }
}
