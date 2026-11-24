namespace Test.Shared.Fakes
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core.Integrations.Interfaces;
    using TrellisKit.Core.Integrations.Models;

    /// <summary>
    /// In-memory vector store with cosine similarity, standing in for RecallDB in tests.
    /// Thread safety: safe for concurrent use.
    /// </summary>
    public sealed class InMemoryVectorStore : IVectorStore
    {
        private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, VectorDocument>> _Collections = new ConcurrentDictionary<string, ConcurrentDictionary<string, VectorDocument>>(StringComparer.Ordinal);

        /// <inheritdoc />
        public bool IsConfigured
        {
            get { return true; }
        }

        /// <summary>
        /// Documents stored in a collection.
        /// </summary>
        /// <param name="collectionId">Collection id.</param>
        /// <returns>Count.</returns>
        public int Count(string collectionId)
        {
            return _Collections.TryGetValue(collectionId, out ConcurrentDictionary<string, VectorDocument>? docs) ? docs.Count : 0;
        }

        /// <inheritdoc />
        public Task<string> CreateCollectionAsync(string name, int dimensions, CancellationToken token = default)
        {
            string id = "col_" + Guid.NewGuid().ToString("N");
            _Collections[id] = new ConcurrentDictionary<string, VectorDocument>(StringComparer.Ordinal);
            return Task.FromResult(id);
        }

        /// <inheritdoc />
        public Task DeleteCollectionAsync(string collectionId, CancellationToken token = default)
        {
            _Collections.TryRemove(collectionId, out ConcurrentDictionary<string, VectorDocument>? _);
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public Task AddAsync(string collectionId, List<VectorDocument> documents, CancellationToken token = default)
        {
            ConcurrentDictionary<string, VectorDocument> docs = _Collections.GetOrAdd(collectionId, id => new ConcurrentDictionary<string, VectorDocument>(StringComparer.Ordinal));
            foreach (VectorDocument document in documents)
                if (!docs.TryAdd(document.Key, document)) throw new InvalidOperationException("Duplicate key " + document.Key);
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public Task DeleteAsync(string collectionId, List<string> keys, CancellationToken token = default)
        {
            if (_Collections.TryGetValue(collectionId, out ConcurrentDictionary<string, VectorDocument>? docs))
                foreach (string key in keys) docs.TryRemove(key, out VectorDocument? _);
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public Task<List<VectorHit>> SearchAsync(string collectionId, float[] embedding, int maxResults, CancellationToken token = default)
        {
            if (!_Collections.TryGetValue(collectionId, out ConcurrentDictionary<string, VectorDocument>? docs)) return Task.FromResult(new List<VectorHit>());
            List<VectorHit> hits = docs.Values.Select(d => new VectorHit { Key = d.Key, DocumentId = d.DocumentId, Content = d.Content, Tags = d.Tags, Score = Cosine(embedding, d.Embedding) })
                .OrderByDescending(h => h.Score).Take(maxResults).ToList();
            return Task.FromResult(hits);
        }

        private static double Cosine(float[] a, float[] b)
        {
            int n = Math.Min(a.Length, b.Length);
            double dot = 0;
            double na = 0;
            double nb = 0;
            for (int i = 0; i < n; i++)
            {
                dot += a[i] * b[i];
                na += a[i] * a[i];
                nb += b[i] * b[i];
            }
            return na == 0 || nb == 0 ? 0 : dot / (Math.Sqrt(na) * Math.Sqrt(nb));
        }
    }
}
