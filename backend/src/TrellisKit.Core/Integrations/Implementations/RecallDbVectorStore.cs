namespace TrellisKit.Core.Integrations.Implementations
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Net;
    using System.Net.Http;
    using System.Threading;
    using System.Threading.Tasks;
    using RecallDb.Sdk;
    using RecallDb.Sdk.Constants;
    using RecallDb.Sdk.Models;
    using TrellisKit.Core.Integrations.Interfaces;
    using TrellisKit.Core.Integrations.Models;
    using TrellisKit.Core.Settings;

    /// <summary>
    /// RecallDB vector store (RecallDb.Sdk 0.2.3). Endpoint, bearer token, and tenant are read live from ai.recallDb*
    /// settings; a client is created per call over one shared HttpClient (RecallDbClient does not own an injected one).
    /// Thread safety: safe to call concurrently.
    /// </summary>
    public sealed class RecallDbVectorStore : IVectorStore, IDisposable
    {
        #region Public-Members

        /// <inheritdoc />
        public bool IsConfigured
        {
            get { return !String.IsNullOrWhiteSpace(_Settings.RecallDbEndpoint) && !String.IsNullOrWhiteSpace(_Settings.RecallDbToken); }
        }

        #endregion

        #region Private-Members

        private const int _BatchSize = 100;
        private readonly AiSettings _Settings;
        private readonly HttpClient _Http = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the store.
        /// </summary>
        /// <param name="settings">AI settings (read live).</param>
        /// <exception cref="ArgumentNullException">The settings are null.</exception>
        public RecallDbVectorStore(AiSettings settings)
        {
            _Settings = settings ?? throw new ArgumentNullException(nameof(settings));
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public async Task<string> CreateCollectionAsync(string name, int dimensions, CancellationToken token = default)
        {
            if (String.IsNullOrWhiteSpace(name)) throw new ArgumentNullException(nameof(name));
            using (RecallDbClient client = Client())
            {
                CollectionMetadata created = await client.CreateCollectionAsync(Tenant, new CollectionMetadata
                {
                    Name = name,
                    Description = "TrellisKit assistant knowledge",
                    Dimensionality = dimensions,
                    Active = true
                }, token).ConfigureAwait(false);
                if (String.IsNullOrEmpty(created.Id)) throw new InvalidOperationException("RecallDB returned a collection without an id.");
                return created.Id;
            }
        }

        /// <inheritdoc />
        public async Task DeleteCollectionAsync(string collectionId, CancellationToken token = default)
        {
            if (String.IsNullOrWhiteSpace(collectionId)) return;
            using (RecallDbClient client = Client())
            {
                try
                {
                    await client.DeleteCollectionAsync(Tenant, collectionId, token).ConfigureAwait(false);
                }
                catch (RecallDbException e) when (e.StatusCode == HttpStatusCode.NotFound)
                {
                    // Already gone.
                }
            }
        }

        /// <inheritdoc />
        public async Task AddAsync(string collectionId, List<VectorDocument> documents, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(documents);
            if (documents.Count == 0) return;
            using (RecallDbClient client = Client())
            {
                foreach (List<VectorDocument> batch in Batches(documents))
                {
                    List<DocumentRecord> records = batch.Select(d => new DocumentRecord
                    {
                        DocumentKey = d.Key,
                        DocumentId = d.DocumentId,
                        Position = d.Position,
                        ContentType = "Text",
                        Content = d.Content,
                        Embeddings = d.Embedding.ToList(),
                        Labels = new List<string> { d.Tags.TryGetValue("targetType", out string? type) ? type : "content" },
                        Tags = new Dictionary<string, string>(d.Tags)
                    }).ToList();
                    await client.CreateDocumentBatchAsync(Tenant, collectionId, records, token).ConfigureAwait(false);
                }
            }
        }

        /// <inheritdoc />
        public async Task DeleteAsync(string collectionId, List<string> keys, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(keys);
            if (keys.Count == 0) return;
            using (RecallDbClient client = Client())
            {
                foreach (List<string> batch in keys.Distinct(StringComparer.Ordinal).Chunk(_BatchSize).Select(c => c.ToList()))
                {
                    try
                    {
                        await client.DeleteDocumentBatchAsync(Tenant, collectionId, batch, token).ConfigureAwait(false);
                    }
                    catch (RecallDbException e) when (e.StatusCode == HttpStatusCode.NotFound)
                    {
                        // Nothing to delete.
                    }
                }
            }
        }

        /// <inheritdoc />
        public async Task<List<VectorHit>> SearchAsync(string collectionId, float[] embedding, int maxResults, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(embedding);
            using (RecallDbClient client = Client())
            {
                SearchResult result = await client.SearchAsync(Tenant, collectionId, new SearchQuery
                {
                    Vector = new VectorQuery { SearchType = VectorSearchTypes.CosineSimilarity, Embeddings = embedding.ToList() },
                    MaxResults = Math.Clamp(maxResults, 1, 1000)
                }, token).ConfigureAwait(false);
                return (result.Documents ?? new List<DocumentRecord>()).Select(d => new VectorHit
                {
                    Key = d.DocumentKey ?? String.Empty,
                    DocumentId = d.DocumentId ?? String.Empty,
                    Content = d.Content ?? String.Empty,
                    Score = d.Score,
                    Tags = d.Tags ?? new Dictionary<string, string>()
                }).ToList();
            }
        }

        /// <summary>
        /// Dispose the shared HTTP client.
        /// </summary>
        public void Dispose()
        {
            _Http.Dispose();
        }

        #endregion

        #region Private-Methods

        private string Tenant
        {
            get { return String.IsNullOrWhiteSpace(_Settings.RecallDbTenantId) ? "default" : _Settings.RecallDbTenantId.Trim(); }
        }

        private RecallDbClient Client()
        {
            if (!IsConfigured) throw new InvalidOperationException("RecallDB is not configured (ai.recallDbEndpoint and ai.recallDbToken).");
            return new RecallDbClient(_Settings.RecallDbEndpoint.Trim(), _Settings.RecallDbToken.Trim(), _Http);
        }

        private static IEnumerable<List<VectorDocument>> Batches(List<VectorDocument> documents)
        {
            for (int i = 0; i < documents.Count; i += _BatchSize) yield return documents.Skip(i).Take(_BatchSize).ToList();
        }

        #endregion
    }
}
