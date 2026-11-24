namespace TrellisKit.Core.Integrations.Models
{
    using System;

    /// <summary>
    /// One piece of extracted document text (a PDF text block or the spoken text of a caption file).
    /// </summary>
    public class ExtractedSegment
    {
        #region Public-Members

        /// <summary>
        /// Text.
        /// </summary>
        public string Text { get; set; } = String.Empty;

        /// <summary>
        /// Page number (PDF), or null.
        /// </summary>
        public int? PageNumber { get; set; } = null;

        #endregion
    }
}
