namespace TrellisKit.Core.Integrations.Interfaces
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core.Integrations.Models;

    /// <summary>
    /// Vector store for assistant knowledge (docs/AI_CONTRACT.md section 7.3; RecallDB per resolved decision 9). Each
    /// assistant's index is one collection.
    /// </summary>
    public interface IVectorStore
    {
        /// <summary>
        /// Whether the store is configured and may be called.
        /// </summary>
        bool IsConfigured { get; }

        /// <summary>
        /// Create a collection.
        /// </summary>
        /// <param name="name">Collection name.</param>
        /// <param name="dimensions">Embedding dimensions.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Collection id.</returns>
        /// <exception cref="InvalidOperationException">The store rejected the request.</exception>
        Task<string> CreateCollectionAsync(string name, int dimensions, CancellationToken token = default);

        /// <summary>
        /// Delete a collection and every document in it (missing collections are ignored).
        /// </summary>
        /// <param name="collectionId">Collection id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        Task DeleteCollectionAsync(string collectionId, CancellationToken token = default);

        /// <summary>
        /// Store documents (keys must not exist yet; delete them first when replacing).
        /// </summary>
        /// <param name="collectionId">Collection id.</param>
        /// <param name="documents">Documents.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        Task AddAsync(string collectionId, List<VectorDocument> documents, CancellationToken token = default);

        /// <summary>
        /// Delete documents by key (missing keys are ignored).
        /// </summary>
        /// <param name="collectionId">Collection id.</param>
        /// <param name="keys">Keys.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        Task DeleteAsync(string collectionId, List<string> keys, CancellationToken token = default);

        /// <summary>
        /// Nearest chunks by cosine similarity.
        /// </summary>
        /// <param name="collectionId">Collection id.</param>
        /// <param name="embedding">Query embedding.</param>
        /// <param name="maxResults">Most results. Minimum 1, maximum 1000.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Hits, best first.</returns>
        Task<List<VectorHit>> SearchAsync(string collectionId, float[] embedding, int maxResults, CancellationToken token = default);
    }
}
