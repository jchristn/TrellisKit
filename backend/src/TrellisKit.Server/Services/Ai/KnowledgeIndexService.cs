namespace TrellisKit.Server.Services.Ai
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;
    using System.Security.Cryptography;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core;
    using TrellisKit.Core.Enumeration;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Exceptions;
    using TrellisKit.Core.Integrations.Models;
    using TrellisKit.Core.Models;
    using TrellisKit.Core.Observability;
    using TrellisKit.Core.Requests;
    using TrellisKit.Core.Responses;

    /// <summary>
    /// Knowledge ingestion into the vector store (docs/AI_CONTRACT.md section 7.3, RecallDB per resolved decision 9): for
    /// each assistant with knowledge on and an embedding endpoint, the content its sources select (titles plus bodies,
    /// 800-character chunks with 100 overlap) is embedded in batches and stored in the assistant's collection with the
    /// target, content hash, and audience. Content is re-embedded when its hash changes and removed when it is deleted,
    /// hidden, or no longer selected. Each item's state is kept in aiknowledgeitems for the admin console.
    /// Thread safety: one pass per assistant at a time per instance; the worker's job lease keeps instances apart.
    /// </summary>
    public class KnowledgeIndexService
    {
        #region Private-Members

        private readonly AiServices _Ai;
        private readonly ConcurrentDictionary<string, byte> _Running = new ConcurrentDictionary<string, byte>(StringComparer.Ordinal);

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the service.
        /// </summary>
        /// <param name="ai">AI services of the property.</param>
        /// <exception cref="ArgumentNullException">The services are null.</exception>
        public KnowledgeIndexService(AiServices ai)
        {
            _Ai = ai ?? throw new ArgumentNullException(nameof(ai));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Ingestion status of an assistant.
        /// </summary>
        /// <param name="assistant">Assistant.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Status.</returns>
        public async Task<KnowledgeIndexView> StatusAsync(Assistant assistant, CancellationToken token)
        {
            ArgumentNullException.ThrowIfNull(assistant);
            AiKnowledgeIndex? index = await _Ai.App.Database.Ai.Assistants.ReadIndexAsync(assistant.Id, token).ConfigureAwait(false);
            bool embedding = assistant.Models.ContainsKey(AssistantPurposeEnum.Embedding);
            AiKnowledgeIndexStatusEnum status = index?.Status ?? AiKnowledgeIndexStatusEnum.NotConfigured;
            if (_Running.ContainsKey(assistant.Id)) status = AiKnowledgeIndexStatusEnum.Running;
            if (!embedding || !_Ai.VectorStore.IsConfigured) status = AiKnowledgeIndexStatusEnum.NotConfigured;
            return new KnowledgeIndexView
            {
                Status = status,
                VectorStoreConfigured = _Ai.VectorStore.IsConfigured,
                EmbeddingConfigured = embedding,
                EndpointId = index?.EndpointId,
                CollectionId = index?.CollectionId,
                Dimensions = index?.Dimensions,
                ItemCount = index?.ItemCount ?? 0,
                ChunkCount = index?.ChunkCount ?? 0,
                LastRunUtc = index?.LastRunUtc,
                LastSuccessUtc = index?.LastSuccessUtc,
                LastError = index?.LastError,
                ReindexRequested = index?.ReindexRequested ?? false
            };
        }

        /// <summary>
        /// Enumerate an assistant's knowledge items (filters status, targetType).
        /// </summary>
        /// <param name="assistant">Assistant.</param>
        /// <param name="query">Query.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Page.</returns>
        public async Task<EnumerationResult<KnowledgeItemView>> ItemsAsync(Assistant assistant, EnumerationQuery? query, CancellationToken token)
        {
            ArgumentNullException.ThrowIfNull(assistant);
            EnumerationRequest request = _Ai.App.Enumeration.Prepare(query, AiCollections.KnowledgeItems, true, assistant.Id);
            Dictionary<string, string> errors = new Dictionary<string, string>();
            AiEnumerationFilter filter = new AiEnumerationFilter
            {
                ItemStatus = request.Query.FilterEnum<AiKnowledgeItemStatusEnum>("status", errors),
                TargetType = request.Query.FilterString("targetType")
            };
            if (errors.Count > 0) throw ApiErrorException.Validation(errors);
            EnumerationResult<AiKnowledgeItem> page = await _Ai.App.Database.Ai.Assistants.EnumerateItemsAsync(request, assistant.Id, filter, token).ConfigureAwait(false);
            return page.With(page.Objects.Select(k => new KnowledgeItemView
            {
                TargetType = k.TargetType,
                TargetId = k.TargetId,
                SourceId = k.SourceId,
                Title = k.Title,
                Audience = k.Audience,
                ChunkCount = k.ChunkCount,
                Status = k.Status,
                Error = k.Error,
                IndexedUtc = k.IndexedUtc,
                UpdatedUtc = k.UpdatedUtc
            }).ToList());
        }

        /// <summary>
        /// Request a full reindex and start a pass in the background.
        /// </summary>
        /// <param name="assistant">Assistant.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Status after the request.</returns>
        public async Task<KnowledgeIndexView> ReindexAsync(Assistant assistant, CancellationToken token)
        {
            ArgumentNullException.ThrowIfNull(assistant);
            DateTime now = DateTime.UtcNow;
            AiKnowledgeIndex index = await _Ai.App.Database.Ai.Assistants.ReadIndexAsync(assistant.Id, token).ConfigureAwait(false) ??
                new AiKnowledgeIndex { AssistantId = assistant.Id, CreatedUtc = now };
            index.ReindexRequested = true;
            index.UpdatedUtc = now;
            await _Ai.App.Database.Ai.Assistants.UpsertIndexAsync(index, token).ConfigureAwait(false);
            _ = Task.Run(async () =>
            {
                try
                {
                    await IndexAsync(assistant, CancellationToken.None).ConfigureAwait(false);
                }
                catch (Exception e) when (e is not OutOfMemoryException)
                {
                    _Ai.App.Logger.Warn("[KnowledgeIndexService] reindex of " + assistant.Id + " failed: " + e.Message);
                }
            });
            return await StatusAsync(assistant, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Run one ingestion pass for an assistant (skipped when one is already running here).
        /// </summary>
        /// <param name="assistant">Assistant with its sources and models.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Chunks embedded in this pass.</returns>
        public async Task<int> IndexAsync(Assistant assistant, CancellationToken token)
        {
            ArgumentNullException.ThrowIfNull(assistant);
            if (!_Running.TryAdd(assistant.Id, 0)) return 0;
            try
            {
                return await IndexCoreAsync(assistant, token).ConfigureAwait(false);
            }
            finally
            {
                _Running.TryRemove(assistant.Id, out byte _);
            }
        }

        /// <summary>
        /// Delete an assistant's collection and ingestion state (assistant deletion).
        /// </summary>
        /// <param name="assistantId">Assistant id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        public async Task DeleteAsync(string assistantId, CancellationToken token)
        {
            AiKnowledgeIndex? index = await _Ai.App.Database.Ai.Assistants.ReadIndexAsync(assistantId, token).ConfigureAwait(false);
            if (index?.CollectionId != null && _Ai.VectorStore.IsConfigured)
            {
                try
                {
                    await _Ai.VectorStore.DeleteCollectionAsync(index.CollectionId, token).ConfigureAwait(false);
                }
                catch (Exception e) when (e is not OperationCanceledException)
                {
                    _Ai.App.Logger.Warn("[KnowledgeIndexService] collection delete failed for " + assistantId + ": " + e.Message);
                }
            }
            await _Ai.App.Database.Ai.Assistants.DeleteKnowledgeAsync(assistantId, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Split text into chunks of 800 characters with 100 overlap, preferring whitespace boundaries.
        /// </summary>
        /// <param name="text">Text.</param>
        /// <returns>Chunks (at least one for non-empty text).</returns>
        public static List<string> Chunk(string text)
        {
            List<string> chunks = new List<string>();
            string value = (text ?? String.Empty).Trim();
            if (value.Length == 0) return chunks;
            int size = AiConstants.ChunkCharacters;
            int overlap = AiConstants.ChunkOverlap;
            int start = 0;
            while (start < value.Length)
            {
                int length = Math.Min(size, value.Length - start);
                int end = start + length;
                if (end < value.Length)
                {
                    int space = value.LastIndexOf(' ', end - 1, Math.Min(length, 200));
                    if (space > start + overlap) end = space;
                }
                chunks.Add(value.Substring(start, end - start).Trim());
                if (end >= value.Length) break;
                start = Math.Max(start + 1, end - overlap);
            }
            return chunks.Where(c => c.Length > 0).ToList();
        }

        #endregion

        #region Private-Methods

        private async Task<int> IndexCoreAsync(Assistant assistant, CancellationToken token)
        {
            DateTime now = DateTime.UtcNow;
            AiKnowledgeIndex index = await _Ai.App.Database.Ai.Assistants.ReadIndexAsync(assistant.Id, token).ConfigureAwait(false) ??
                new AiKnowledgeIndex { AssistantId = assistant.Id, CreatedUtc = now };
            ModelEndpoint? embedding = null;
            if (assistant.Models.TryGetValue(AssistantPurposeEnum.Embedding, out string? endpointId)) embedding = await _Ai.App.Database.Ai.Endpoints.ReadAsync(endpointId, token).ConfigureAwait(false);
            if (!assistant.KnowledgeEnabled || embedding == null || !_Ai.VectorStore.IsConfigured)
            {
                index.Status = AiKnowledgeIndexStatusEnum.NotConfigured;
                index.UpdatedUtc = now;
                await _Ai.App.Database.Ai.Assistants.UpsertIndexAsync(index, token).ConfigureAwait(false);
                return 0;
            }
            index.Status = AiKnowledgeIndexStatusEnum.Running;
            index.LastRunUtc = now;
            index.UpdatedUtc = now;
            await _Ai.App.Database.Ai.Assistants.UpsertIndexAsync(index, token).ConfigureAwait(false);
            int embedded = 0;
            try
            {
                bool rebuild = index.ReindexRequested || index.CollectionId == null || index.EndpointId != embedding.Id;
                List<AiKnowledgeItem> existing = await _Ai.App.Database.Ai.Assistants.ListItemsAsync(assistant.Id, 100000, token).ConfigureAwait(false);
                if (rebuild)
                {
                    if (index.CollectionId != null) await _Ai.VectorStore.DeleteCollectionAsync(index.CollectionId, token).ConfigureAwait(false);
                    index.CollectionId = null;
                    index.Dimensions = null;
                    foreach (AiKnowledgeItem item in existing) item.ContentHash = null;
                }
                Dictionary<string, AiKnowledgeItem> byKey = existing.ToDictionary(i => i.TargetType + ":" + i.TargetId, StringComparer.Ordinal);
                Dictionary<string, KnowledgeSourceItem> selected = await SelectAsync(assistant, token).ConfigureAwait(false);
                foreach (KeyValuePair<string, KnowledgeSourceItem> pair in selected)
                {
                    token.ThrowIfCancellationRequested();
                    KnowledgeSourceItem source = pair.Value;
                    string text = source.Title + "\n\n" + source.Text;
                    string hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
                    byKey.TryGetValue(pair.Key, out AiKnowledgeItem? item);
                    if (item != null && item.Status == AiKnowledgeItemStatusEnum.Indexed && item.ContentHash == hash && item.Audience == source.Audience) continue;
                    AiKnowledgeItem next = item ?? new AiKnowledgeItem { AssistantId = assistant.Id, TargetType = source.TargetType, TargetId = source.TargetId };
                    next.SourceId = source.SourceId;
                    next.Title = source.Title;
                    next.Audience = source.Audience;
                    next.UpdatedUtc = DateTime.UtcNow;
                    try
                    {
                        List<string> chunks = Chunk(text);
                        List<float[]> vectors = chunks.Count == 0 ? new List<float[]>() : await _Ai.Invoker.EmbedAsync(embedding, chunks, assistant.Id, false, token).ConfigureAwait(false);
                        if (vectors.Count > 0 && index.CollectionId == null)
                        {
                            index.Dimensions = vectors[0].Length;
                            index.CollectionId = await _Ai.VectorStore.CreateCollectionAsync("tk-" + _Ai.App.PropertyId + "-" + assistant.Id, vectors[0].Length, token).ConfigureAwait(false);
                            index.EndpointId = embedding.Id;
                        }
                        if (index.CollectionId != null)
                        {
                            if (item != null && item.ChunkCount > 0 && !rebuild) await _Ai.VectorStore.DeleteAsync(index.CollectionId, Keys(next, item.ChunkCount), token).ConfigureAwait(false);
                            List<VectorDocument> documents = new List<VectorDocument>();
                            for (int i = 0; i < chunks.Count && i < vectors.Count; i++)
                            {
                                documents.Add(new VectorDocument
                                {
                                    Key = next.TargetType + ":" + next.TargetId + ":" + i.ToString(CultureInfo.InvariantCulture),
                                    DocumentId = next.TargetType + ":" + next.TargetId,
                                    Position = i,
                                    Content = chunks[i],
                                    Embedding = vectors[i],
                                    Tags = new Dictionary<string, string>
                                    {
                                        { "targetType", next.TargetType },
                                        { "targetId", next.TargetId },
                                        { "title", next.Title ?? String.Empty },
                                        { "audience", next.Audience ?? String.Empty },
                                        { "propertyId", _Ai.App.PropertyId }
                                    }
                                });
                            }
                            await _Ai.VectorStore.AddAsync(index.CollectionId, documents, token).ConfigureAwait(false);
                            embedded += documents.Count;
                            next.ChunkCount = documents.Count;
                        }
                        next.ContentHash = hash;
                        next.Status = AiKnowledgeItemStatusEnum.Indexed;
                        next.Error = null;
                        next.IndexedUtc = DateTime.UtcNow;
                        TrellisKitTelemetry.Add(AiTelemetry.IngestedChunks, next.ChunkCount, "outcome", "ok");
                    }
                    catch (Exception e) when (e is not OperationCanceledException)
                    {
                        next.Status = AiKnowledgeItemStatusEnum.Failed;
                        next.Error = e is ApiErrorException api ? api.Error : Cut(e.Message, 300);
                        TrellisKitTelemetry.Add(AiTelemetry.IngestedChunks, 1, "outcome", "error");
                    }
                    await _Ai.App.Database.Ai.Assistants.UpsertItemAsync(next, token).ConfigureAwait(false);
                }
                foreach (AiKnowledgeItem stale in existing.Where(i => !selected.ContainsKey(i.TargetType + ":" + i.TargetId) && i.Status != AiKnowledgeItemStatusEnum.Removed))
                {
                    if (index.CollectionId != null && stale.ChunkCount > 0 && !rebuild) await _Ai.VectorStore.DeleteAsync(index.CollectionId, Keys(stale, stale.ChunkCount), token).ConfigureAwait(false);
                    stale.Status = AiKnowledgeItemStatusEnum.Removed;
                    stale.ChunkCount = 0;
                    stale.ContentHash = null;
                    stale.UpdatedUtc = DateTime.UtcNow;
                    await _Ai.App.Database.Ai.Assistants.UpsertItemAsync(stale, token).ConfigureAwait(false);
                }
                List<AiKnowledgeItem> after = await _Ai.App.Database.Ai.Assistants.ListItemsAsync(assistant.Id, 100000, token).ConfigureAwait(false);
                index.ItemCount = after.Count(i => i.Status == AiKnowledgeItemStatusEnum.Indexed);
                index.ChunkCount = after.Where(i => i.Status == AiKnowledgeItemStatusEnum.Indexed).Sum(i => i.ChunkCount);
                index.Status = after.Any(i => i.Status == AiKnowledgeItemStatusEnum.Failed) ? AiKnowledgeIndexStatusEnum.Failed : AiKnowledgeIndexStatusEnum.Idle;
                index.LastError = index.Status == AiKnowledgeIndexStatusEnum.Failed ? "Some items failed to index." : null;
                index.LastSuccessUtc = DateTime.UtcNow;
                index.ReindexRequested = false;
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                index.Status = AiKnowledgeIndexStatusEnum.Failed;
                index.LastError = e is ApiErrorException api ? api.Error : Cut(e.Message, 300);
                _Ai.App.Logger.Warn("[KnowledgeIndexService] pass for " + assistant.Id + " failed: " + index.LastError);
            }
            index.UpdatedUtc = DateTime.UtcNow;
            await _Ai.App.Database.Ai.Assistants.UpsertIndexAsync(index, token).ConfigureAwait(false);
            return embedded;
        }

        private async Task<Dictionary<string, KnowledgeSourceItem>> SelectAsync(Assistant assistant, CancellationToken token)
        {
            Dictionary<string, KnowledgeSourceItem> selected = new Dictionary<string, KnowledgeSourceItem>(StringComparer.Ordinal);
            ContentActor system = new ContentActor { IsBreakGlass = true, IsSystemAdmin = true, IsStaff = true, HasMembership = true, AgeAttested = true };
            foreach (AssistantKnowledgeSource source in assistant.Sources.Where(s => s.TargetType == TargetTypeEnum.FeedPost))
            {
                string? continuation = null;
                int taken = 0;
                while (taken < source.MaxItems)
                {
                    EnumerationQuery query = new EnumerationQuery
                    {
                        MaxResults = Math.Min(50, source.MaxItems - taken),
                        Ordering = "CreatedDescending",
                        ContinuationToken = continuation,
                        Labels = new List<string>(source.Labels),
                        LabelMatch = source.LabelMatch,
                        Tags = new Dictionary<string, string>(source.Tags)
                    };
                    EnumerationResult<FeedPostView> page = await _Ai.App.Feed.ListAsync(system, query, token).ConfigureAwait(false);
                    foreach (FeedPostView post in page.Objects)
                    {
                        taken++;
                        if (post.State != FeedPostStateEnum.Published || (post.Moderation != null && post.Moderation.State != ModerationStateEnum.None)) continue;
                        string key = "FeedPost:" + post.Id;
                        if (selected.ContainsKey(key)) continue;
                        selected[key] = new KnowledgeSourceItem
                        {
                            SourceId = source.Id,
                            TargetType = "FeedPost",
                            TargetId = post.Id,
                            Title = KnowledgeRetrievalService.TitleOf(post.Title, post.Body),
                            Text = MarkdownNormalizer.ToPlainText(post.Body),
                            Audience = post.Audience.ToString()
                        };
                    }
                    if (page.EndOfResults || String.IsNullOrEmpty(page.ContinuationToken)) break;
                    continuation = page.ContinuationToken;
                }
            }
            foreach (AssistantKnowledgeSource source in assistant.Sources.Where(s => s.TargetType != TargetTypeEnum.FeedPost && KnowledgeTypes.IsAvailable(_Ai.App, s.TargetType)))
            {
                ContentTargetProvider provider = _Ai.App.ContentTargets.Find(source.TargetType)!;
                List<KnowledgeDocument> documents = await provider.KnowledgeDocumentsAsync(source.TargetType, source, Math.Clamp(source.MaxItems, 1, 2000), token).ConfigureAwait(false);
                foreach (KnowledgeDocument document in documents.Take(Math.Clamp(source.MaxItems, 1, 2000)))
                {
                    if (String.IsNullOrEmpty(document.TargetId)) continue;
                    string key = source.TargetType + ":" + document.TargetId;
                    if (selected.ContainsKey(key)) continue;
                    selected[key] = new KnowledgeSourceItem
                    {
                        SourceId = source.Id,
                        TargetType = source.TargetType.ToString(),
                        TargetId = document.TargetId,
                        Title = document.Title,
                        Text = document.Text,
                        Audience = document.Audience
                    };
                }
            }
            return selected;
        }

        private static List<string> Keys(AiKnowledgeItem item, int count)
        {
            List<string> keys = new List<string>();
            for (int i = 0; i < count; i++) keys.Add(item.TargetType + ":" + item.TargetId + ":" + i.ToString(CultureInfo.InvariantCulture));
            return keys;
        }

        private static string Cut(string text, int max)
        {
            return text.Length <= max ? text : text.Substring(0, max);
        }

        #endregion
    }
}
