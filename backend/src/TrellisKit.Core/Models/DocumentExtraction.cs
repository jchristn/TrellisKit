namespace TrellisKit.Core.Models
{
    using System;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// Text extraction state of a Document media (documentextractions).
    /// </summary>
    public class DocumentExtraction
    {
        #region Public-Members

        /// <summary>
        /// Media.
        /// </summary>
        public string MediaId { get; set; } = String.Empty;

        /// <summary>
        /// Status.
        /// </summary>
        public DocumentExtractionStatusEnum Status { get; set; } = DocumentExtractionStatusEnum.Pending;

        /// <summary>
        /// Content type (application/pdf or text/vtt).
        /// </summary>
        public string ContentType { get; set; } = String.Empty;

        /// <summary>
        /// Extractor that produced the text, or null.
        /// </summary>
        public string? Extractor { get; set; } = null;

        /// <summary>
        /// Chunker that produced the chunks, or null.
        /// </summary>
        public string? Chunker { get; set; } = null;

        /// <summary>
        /// Chunks stored.
        /// </summary>
        public int ChunkCount { get; set; } = 0;

        /// <summary>
        /// Characters of extracted text.
        /// </summary>
        public int CharacterCount { get; set; } = 0;

        /// <summary>
        /// Summary (when summarization is on), or null.
        /// </summary>
        public string? Summary { get; set; } = null;

        /// <summary>
        /// Last error, or null.
        /// </summary>
        public string? Error { get; set; } = null;

        /// <summary>
        /// Attempts so far.
        /// </summary>
        public int Attempts { get; set; } = 0;

        /// <summary>
        /// When extracted, or null.
        /// </summary>
        public DateTime? ExtractedUtc { get; set; } = null;

        /// <summary>
        /// Created.
        /// </summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Last change.
        /// </summary>
        public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;

        #endregion
    }
}
