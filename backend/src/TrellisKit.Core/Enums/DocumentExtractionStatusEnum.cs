namespace TrellisKit.Core.Enums
{
    /// <summary>
    /// Status of the text extraction of a Document media.
    /// </summary>
    public enum DocumentExtractionStatusEnum
    {
        /// <summary>
        /// Waiting for the document text worker.
        /// </summary>
        Pending,

        /// <summary>
        /// Text extracted and chunked.
        /// </summary>
        Ready,

        /// <summary>
        /// Extraction failed after the allowed attempts.
        /// </summary>
        Failed,

        /// <summary>
        /// Nothing to extract (unsupported content, empty text, or the media is gone).
        /// </summary>
        Skipped
    }
}
