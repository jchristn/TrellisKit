namespace TrellisKit.Core.Integrations.Models
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// The text of a document, in reading order.
    /// </summary>
    public class ExtractedDocument
    {
        #region Public-Members

        /// <summary>
        /// Extractor name (for example documentatom-pdf, webvtt).
        /// </summary>
        public string Extractor { get; set; } = String.Empty;

        /// <summary>
        /// Segments in reading order.
        /// </summary>
        public List<ExtractedSegment> Segments { get; set; } = new List<ExtractedSegment>();

        #endregion
    }
}
