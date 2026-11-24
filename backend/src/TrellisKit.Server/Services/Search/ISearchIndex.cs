namespace TrellisKit.Server.Services
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// The engine search index (Phase 3 resolved decision 13): one inverted index per property. The implementation is
    /// swappable (Verbex is alpha upstream); callers never depend on the implementation.
    /// Thread safety: implementations are safe for concurrent searches and serialized writes.
    /// </summary>
    public interface ISearchIndex : IAsyncDisposable
    {
        /// <summary>
        /// Provider name for telemetry (low cardinality).
        /// </summary>
        string Provider { get; }

        /// <summary>
        /// Add or replace a document.
        /// </summary>
        /// <param name="document">Document.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        Task UpsertAsync(SearchDocument document, CancellationToken token = default);

        /// <summary>
        /// Remove a document (no error when absent).
        /// </summary>
        /// <param name="documentId">Document identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        Task RemoveAsync(string documentId, CancellationToken token = default);

        /// <summary>
        /// Ranked documents matching every term of the text (with lemmatization, stop-word removal, and a prefix match
        /// on the last term), filtered by labels (all) and tags (all).
        /// </summary>
        /// <param name="text">Search text.</param>
        /// <param name="labels">Required labels, or empty.</param>
        /// <param name="tags">Required tags, or empty.</param>
        /// <param name="maxResults">Most hits.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Hits by descending score.</returns>
        Task<List<SearchHit>> SearchAsync(string text, IReadOnlyList<string> labels, IReadOnlyDictionary<string, string> tags, int maxResults, CancellationToken token = default);

        /// <summary>
        /// Documents in the index.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Count.</returns>
        Task<long> CountAsync(CancellationToken token = default);

        /// <summary>
        /// Remove every document (rebuild).
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        Task ClearAsync(CancellationToken token = default);
    }
}
