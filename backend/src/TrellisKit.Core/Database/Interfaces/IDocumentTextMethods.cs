namespace TrellisKit.Core.Database.Interfaces
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core.Models;

    /// <summary>
    /// Extracted text of Document media used by lessons (captions and PDF resources), for search and assistant
    /// grounding (resolved decision 18).
    /// </summary>
    public interface IDocumentTextMethods
    {
        /// <summary>
        /// Queue a document for extraction (a new row, or a Failed or Skipped row back to Pending).
        /// </summary>
        /// <param name="mediaId">Media.</param>
        /// <param name="contentType">Content type.</param>
        /// <param name="nowUtc">Now.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True when the document is now pending.</returns>
        Task<bool> EnqueueAsync(string mediaId, string contentType, DateTime nowUtc, CancellationToken token = default);

        /// <summary>
        /// Read an extraction row, or null.
        /// </summary>
        /// <param name="mediaId">Media.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Row or null.</returns>
        Task<DocumentExtraction?> ReadAsync(string mediaId, CancellationToken token = default);

        /// <summary>
        /// Pending rows, oldest first.
        /// </summary>
        /// <param name="limit">Maximum rows. Minimum 1.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Rows.</returns>
        Task<List<DocumentExtraction>> PendingAsync(int limit, CancellationToken token = default);

        /// <summary>
        /// Store the result of an extraction: the row's fields and its chunks (replaced).
        /// </summary>
        /// <param name="extraction">Row.</param>
        /// <param name="chunks">Chunks in order.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        Task StoreAsync(DocumentExtraction extraction, List<DocumentChunk> chunks, CancellationToken token = default);

        /// <summary>
        /// The chunks of several documents in document and position order, at most limit.
        /// </summary>
        /// <param name="mediaIds">Media.</param>
        /// <param name="limit">Maximum rows. Minimum 1.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Chunks.</returns>
        Task<List<DocumentChunk>> ChunksAsync(IEnumerable<string> mediaIds, int limit, CancellationToken token = default);

        /// <summary>
        /// Delete a document's extraction and chunks.
        /// </summary>
        /// <param name="mediaId">Media.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        Task DeleteAsync(string mediaId, CancellationToken token = default);
    }
}
