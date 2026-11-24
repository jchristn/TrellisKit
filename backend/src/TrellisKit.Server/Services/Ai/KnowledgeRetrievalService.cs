namespace TrellisKit.Server.Services.Ai
{
    using PolyPrompt.Models;
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Globalization;
    using System.Linq;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Exceptions;
    using TrellisKit.Core.Integrations.Models;
    using TrellisKit.Core.Models;
    using TrellisKit.Core.Observability;
    using TrellisKit.Core.Requests;
    using TrellisKit.Core.Responses;

    /// <summary>
    /// Knowledge grounding (docs/AI_CONTRACT.md section 7). Keyword retrieval runs each source's content enumeration as
    /// the chatting member (visitors use the public feed), so audience, blocks, moderation, and gating apply exactly as in
    /// the app; vector retrieval searches the assistant's RecallDB collection and re-checks each hit as the member;
    /// Hybrid merges both by reciprocal rank fusion (k = 60); an optional rerank model reorders; the top items become a
    /// numbered context block. Feed posts are the content type available in this release.
    /// Thread safety: safe to call concurrently.
    /// </summary>
    public class KnowledgeRetrievalService
    {
        #region Private-Members

        private const int _MaxKeywords = 4;
        private const int _RrfK = 60;
        private static readonly HashSet<string> _StopWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "the", "and", "for", "are", "but", "not", "you", "your", "with", "this", "that", "have", "from", "what", "when", "where", "which", "who", "how", "why",
            "can", "could", "would", "should", "does", "did", "was", "were", "will", "about", "into", "there", "their", "they", "them", "then", "than", "some", "any",
            "all", "our", "out", "get", "got", "has", "had", "its", "just", "like", "please", "tell", "know", "want", "need", "also"
        };

        private readonly AiServices _Ai;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the service.
        /// </summary>
        /// <param name="ai">AI services of the property.</param>
        /// <exception cref="ArgumentNullException">The services are null.</exception>
        public KnowledgeRetrievalService(AiServices ai)
        {
            _Ai = ai ?? throw new ArgumentNullException(nameof(ai));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Retrieve knowledge for a message.
        /// </summary>
        /// <param name="assistant">Assistant.</param>
        /// <param name="endpoints">Endpoints of the assistant, by id.</param>
        /// <param name="caller">Caller (retrieval runs as this member or visitor).</param>
        /// <param name="query">Query text (already rewritten when a rewrite model is set).</param>
        /// <param name="isTest">Admin test traffic.</param>
        /// <param name="stages">Telemetry stages to append to.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Result (empty when knowledge is off or nothing matched).</returns>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        public async Task<KnowledgeResult> RetrieveAsync(Assistant assistant, Dictionary<string, ModelEndpoint> endpoints, AssistantCaller caller, string query, bool isTest, List<AssistantMessageStage> stages, CancellationToken token)
        {
            ArgumentNullException.ThrowIfNull(assistant);
            ArgumentNullException.ThrowIfNull(endpoints);
            ArgumentNullException.ThrowIfNull(caller);
            ArgumentNullException.ThrowIfNull(stages);
            KnowledgeResult result = new KnowledgeResult { Query = query ?? String.Empty };
            if (!assistant.KnowledgeEnabled || String.IsNullOrWhiteSpace(query) || assistant.Sources.Count == 0) return result;
            Stopwatch watch = Stopwatch.StartNew();
            DateTime started = DateTime.UtcNow;
            ModelEndpoint? embedding = assistant.Models.TryGetValue(AssistantPurposeEnum.Embedding, out string? eid) && endpoints.TryGetValue(eid, out ModelEndpoint? e) ? e : null;
            bool vectorPossible = embedding != null && _Ai.VectorStore.IsConfigured;
            AssistantKnowledgeModeEnum mode = assistant.KnowledgeMode;
            if (mode != AssistantKnowledgeModeEnum.Keyword && !vectorPossible) mode = AssistantKnowledgeModeEnum.Keyword;
            result.Mode = mode.ToString().ToLowerInvariant();

            List<KnowledgeCandidate> keyword = new List<KnowledgeCandidate>();
            List<KnowledgeCandidate> vector = new List<KnowledgeCandidate>();
            string? detail = null;
            using (Activity? span = AiTelemetry.Span("ai.knowledge.retrieve"))
            {
                span?.SetTag("ai.assistant.id", assistant.Id);
                span?.SetTag("ai.knowledge.mode", result.Mode);
                if (mode != AssistantKnowledgeModeEnum.Vector) keyword = await KeywordAsync(assistant, caller, query, token).ConfigureAwait(false);
                if (mode != AssistantKnowledgeModeEnum.Keyword)
                {
                    try
                    {
                        vector = await VectorAsync(assistant, embedding!, caller, query, isTest, token).ConfigureAwait(false);
                    }
                    catch (ApiErrorException ex)
                    {
                        detail = "vector retrieval failed (" + ex.Error + "); keyword only";
                        if (keyword.Count == 0 && mode == AssistantKnowledgeModeEnum.Vector) keyword = await KeywordAsync(assistant, caller, query, token).ConfigureAwait(false);
                    }
                }
            }
            List<KnowledgeCandidate> merged = mode == AssistantKnowledgeModeEnum.Hybrid ? Fuse(keyword, vector) : mode == AssistantKnowledgeModeEnum.Vector && vector.Count > 0 ? vector : keyword.Count > 0 ? keyword : vector;
            result.TotalCandidates = merged.Count;
            int max = Math.Clamp(assistant.MaxContextItems, 1, 50);
            if (merged.Count > 1 && assistant.Models.TryGetValue(AssistantPurposeEnum.Rerank, out string? rid) && endpoints.TryGetValue(rid, out ModelEndpoint? rerank))
            {
                DateTime rerankStarted = DateTime.UtcNow;
                Stopwatch rerankWatch = Stopwatch.StartNew();
                List<KnowledgeCandidate> pool = merged.Take(Math.Max(max * 3, 10)).ToList();
                try
                {
                    List<RerankResult> scores = await _Ai.Invoker.RerankAsync(rerank, query, pool.Select(c => c.Title + "\n" + c.Excerpt).ToList(), assistant.Id, isTest, token).ConfigureAwait(false);
                    merged = scores.Where(s => s.Index >= 0 && s.Index < pool.Count).Select(s =>
                    {
                        KnowledgeCandidate c = pool[s.Index];
                        c.Score = s.Score;
                        return c;
                    }).ToList();
                    stages.Add(new AssistantMessageStage { Stage = "rerank", EndpointId = rerank.Id, Model = rerank.Model, StartedUtc = rerankStarted, DurationMs = rerankWatch.ElapsedMilliseconds, Detail = pool.Count + " reranked" });
                }
                catch (ApiErrorException ex)
                {
                    stages.Add(new AssistantMessageStage { Stage = "rerank", EndpointId = rerank.Id, Model = rerank.Model, StartedUtc = rerankStarted, DurationMs = rerankWatch.ElapsedMilliseconds, Success = false, Detail = ex.Error });
                }
            }
            result.Items = merged.Take(max).ToList();
            result.ContextBlock = BuildContext(result.Items, Math.Clamp(assistant.MaxContextCharacters, 500, 200000));
            TrellisKitTelemetry.Record(AiTelemetry.RetrievalDuration, watch.Elapsed.TotalSeconds, "mode", result.Mode);
            TrellisKitTelemetry.Record(AiTelemetry.RetrievalItems, result.Items.Count, "mode", result.Mode);
            stages.Add(new AssistantMessageStage
            {
                Stage = "retrieval",
                EndpointId = mode != AssistantKnowledgeModeEnum.Keyword ? embedding?.Id : null,
                Model = mode != AssistantKnowledgeModeEnum.Keyword ? embedding?.Model : null,
                StartedUtc = started,
                DurationMs = watch.ElapsedMilliseconds,
                Detail = result.Mode + ": " + keyword.Count + " keyword, " + vector.Count + " vector, " + result.Items.Count + " in context" + (detail != null ? "; " + detail : String.Empty)
            });
            return result;
        }

        /// <summary>
        /// Rewrite the latest turns into a standalone retrieval query with the queryRewrite model (200 tokens); the
        /// member's message as is when no model is set or the call fails.
        /// </summary>
        /// <param name="assistant">Assistant.</param>
        /// <param name="endpoints">Endpoints of the assistant, by id.</param>
        /// <param name="history">Earlier turns (role and content), oldest first.</param>
        /// <param name="message">The new member message.</param>
        /// <param name="isTest">Test traffic.</param>
        /// <param name="stages">Telemetry stages.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Query text.</returns>
        public async Task<string> RewriteAsync(Assistant assistant, Dictionary<string, ModelEndpoint> endpoints, List<KeyValuePair<string, string>> history, string message, bool isTest, List<AssistantMessageStage> stages, CancellationToken token)
        {
            ArgumentNullException.ThrowIfNull(assistant);
            if (!assistant.KnowledgeEnabled || history == null || history.Count == 0 || !assistant.Models.TryGetValue(AssistantPurposeEnum.QueryRewrite, out string? id) || !endpoints.TryGetValue(id, out ModelEndpoint? endpoint))
                return message;
            StringBuilder prompt = new StringBuilder("Rewrite the last user message as one standalone search query that keeps every detail needed to find relevant content. Reply with the query only.\n\n");
            foreach (KeyValuePair<string, string> turn in history.TakeLast(6)) prompt.Append(turn.Key).Append(": ").Append(Cut(turn.Value, 600)).Append('\n');
            prompt.Append("user: ").Append(Cut(message, 1200));
            DateTime started = DateTime.UtcNow;
            CompletionOutcome outcome = await _Ai.Invoker.PromptAsync(endpoint, prompt.ToString(), null, 200, 0, AssistantPurposeEnum.QueryRewrite, assistant.Id, isTest, token).ConfigureAwait(false);
            string rewritten = outcome.Success ? (outcome.Text ?? String.Empty).Trim().Trim('"') : String.Empty;
            stages.Add(new AssistantMessageStage
            {
                Stage = "queryRewrite",
                EndpointId = endpoint.Id,
                Model = endpoint.Model,
                StartedUtc = started,
                DurationMs = outcome.LatencyMs,
                Success = outcome.Success,
                Detail = outcome.Success ? "rewritten" : "kept the message (" + outcome.ErrorCode + ")",
                PromptTokens = outcome.PromptTokens,
                CompletionTokens = outcome.CompletionTokens
            });
            return rewritten.Length == 0 ? message : Cut(rewritten, 500);
        }

        /// <summary>
        /// Admin preview (section 5.3): retrieval as an anonymous Members-audience caller, or as a given member.
        /// </summary>
        /// <param name="assistant">Assistant.</param>
        /// <param name="endpoints">Endpoints of the assistant, by id.</param>
        /// <param name="caller">Caller to retrieve as.</param>
        /// <param name="query">Query.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Preview.</returns>
        public async Task<KnowledgePreviewView> PreviewAsync(Assistant assistant, Dictionary<string, ModelEndpoint> endpoints, AssistantCaller caller, string query, CancellationToken token)
        {
            ArgumentNullException.ThrowIfNull(assistant);
            Assistant enabled = assistant;
            bool wasEnabled = enabled.KnowledgeEnabled;
            enabled.KnowledgeEnabled = true;
            KnowledgeResult result = await RetrieveAsync(enabled, endpoints, caller, query, true, new List<AssistantMessageStage>(), token).ConfigureAwait(false);
            enabled.KnowledgeEnabled = wasEnabled;
            return new KnowledgePreviewView
            {
                Items = result.Items.Select((i, rank) => new KnowledgePreviewItem { Id = i.TargetType + ":" + i.TargetId, Rank = rank, TargetType = i.TargetType, TargetId = i.TargetId, Title = i.Title, Path = i.Path, Score = Math.Round(i.Score, 6), Excerpt = i.Excerpt }).ToList(),
                TotalCandidates = result.TotalCandidates,
                Mode = result.Mode
            };
        }

        /// <summary>
        /// Map the [n] keys cited in a reply to its knowledge items.
        /// </summary>
        /// <param name="text">Reply text.</param>
        /// <param name="items">Items placed in the context.</param>
        /// <returns>Citations in key order.</returns>
        public static List<AssistantCitation> Citations(string text, List<KnowledgeCandidate> items)
        {
            List<AssistantCitation> citations = new List<AssistantCitation>();
            if (String.IsNullOrEmpty(text) || items == null || items.Count == 0) return citations;
            foreach (KnowledgeCandidate item in items.OrderBy(i => i.Index))
            {
                if (!text.Contains("[" + item.Index.ToString(CultureInfo.InvariantCulture) + "]", StringComparison.Ordinal)) continue;
                citations.Add(new AssistantCitation { Index = item.Index, TargetType = item.TargetType, TargetId = item.TargetId, Title = item.Title, Path = item.Path, Score = item.Score });
            }
            return citations;
        }

        /// <summary>
        /// Plain-text title of a feed post (its title, or the start of its body).
        /// </summary>
        /// <param name="title">Title, or null.</param>
        /// <param name="body">Body.</param>
        /// <returns>Title.</returns>
        public static string TitleOf(string? title, string? body)
        {
            if (!String.IsNullOrWhiteSpace(title)) return title.Trim();
            string plain = MarkdownNormalizer.ToPlainText(body ?? String.Empty).Trim();
            return plain.Length <= 80 ? plain : plain.Substring(0, 80).TrimEnd() + "...";
        }

        #endregion

        #region Private-Methods

        private async Task<List<KnowledgeCandidate>> KeywordAsync(Assistant assistant, AssistantCaller caller, string query, CancellationToken token)
        {
            List<string> keywords = Keywords(query);
            if (keywords.Count == 0) return new List<KnowledgeCandidate>();
            Dictionary<string, KnowledgeCandidate> found = new Dictionary<string, KnowledgeCandidate>(StringComparer.Ordinal);
            foreach (AssistantKnowledgeSource source in assistant.Sources.Where(s => s.TargetType == TargetTypeEnum.FeedPost))
            {
                foreach (string keyword in keywords)
                {
                    EnumerationQuery q = new EnumerationQuery
                    {
                        Search = keyword,
                        MaxResults = Math.Clamp(Math.Min(source.MaxItems, 25), 1, 25),
                        Ordering = "CreatedDescending",
                        Labels = new List<string>(source.Labels),
                        LabelMatch = source.LabelMatch,
                        Tags = new Dictionary<string, string>(source.Tags)
                    };
                    try
                    {
                        if (caller.UserId == null && !caller.IsSystemAdmin)
                        {
                            EnumerationResult<PublicFeedPost> page = await _Ai.App.Feed.ListPublicAsync(q, caller.SourceIp, token).ConfigureAwait(false);
                            foreach (PublicFeedPost post in page.Objects) Add(found, post.Id, post.Title, post.Body, keywords);
                        }
                        else
                        {
                            EnumerationResult<FeedPostView> page = await _Ai.App.Feed.ListAsync(caller.Actor, q, token).ConfigureAwait(false);
                            foreach (FeedPostView post in page.Objects.Where(p => p.State == FeedPostStateEnum.Published && (p.Moderation == null || p.Moderation.State == ModerationStateEnum.None)))
                                Add(found, post.Id, post.Title, post.Body, keywords);
                        }
                    }
                    catch (ApiErrorException ex) when (ex.StatusCode == 404 || ex.StatusCode == 403 || ex.StatusCode == 402 || ex.StatusCode == 429)
                    {
                        // The caller cannot read this content (or is rate limited): no candidates from this source.
                        break;
                    }
                }
            }
            foreach (AssistantKnowledgeSource source in assistant.Sources.Where(s => s.TargetType != TargetTypeEnum.FeedPost && KnowledgeTypes.IsAvailable(_Ai.App, s.TargetType)))
            {
                ContentTargetProvider provider = _Ai.App.ContentTargets.Find(source.TargetType)!;
                ContentActor actor = ActorOf(caller);
                foreach (string keyword in keywords)
                {
                    List<KnowledgeDocument> documents;
                    try
                    {
                        documents = await provider.KnowledgeSearchAsync(actor, source.TargetType, source, keyword, Math.Clamp(Math.Min(source.MaxItems, 25), 1, 25), token).ConfigureAwait(false);
                    }
                    catch (ApiErrorException ex) when (ex.StatusCode == 404 || ex.StatusCode == 403 || ex.StatusCode == 402 || ex.StatusCode == 401 || ex.StatusCode == 429)
                    {
                        break;
                    }
                    foreach (KnowledgeDocument document in documents.Take(25))
                    {
                        string key = source.TargetType + ":" + document.TargetId;
                        if (found.ContainsKey(key))
                        {
                            found[key].Score = Math.Max(found[key].Score, KeywordScore(document.Title, document.Text, keywords));
                            continue;
                        }
                        // Re-check as the chatting member: the provider's own read rules decide.
                        if (!await provider.IsVisibleAsync(actor, source.TargetType, document.TargetId, token).ConfigureAwait(false)) continue;
                        found[key] = new KnowledgeCandidate
                        {
                            TargetType = source.TargetType.ToString(),
                            TargetId = document.TargetId,
                            Title = TitleOf(document.Title, document.Text),
                            Path = provider.PathOf(source.TargetType, document.TargetId),
                            Excerpt = Cut(document.Text.Trim(), 1200),
                            Score = KeywordScore(document.Title, document.Text, keywords)
                        };
                    }
                }
            }
            return found.Values.OrderByDescending(c => c.Score).ThenBy(c => c.TargetId, StringComparer.Ordinal).ToList();
        }

        private static ContentActor ActorOf(AssistantCaller caller)
        {
            if (caller.UserId == null && !caller.IsSystemAdmin) return new ContentActor { IsAnonymous = true, CanPost = false, CanPostMedia = false, CanUploadVideo = false, SourceIp = caller.SourceIp, Access = caller.Actor.Access };
            return caller.Actor;
        }

        private static double KeywordScore(string? title, string? text, List<string> keywords)
        {
            string lower = ((title ?? String.Empty) + " " + (text ?? String.Empty)).ToLowerInvariant();
            return keywords.Count == 0 ? 0 : keywords.Sum(k => lower.Contains(k, StringComparison.Ordinal) ? 1.0 : 0.0) / keywords.Count;
        }

        private async Task<List<KnowledgeCandidate>> VectorAsync(Assistant assistant, ModelEndpoint embedding, AssistantCaller caller, string query, bool isTest, CancellationToken token)
        {
            AiKnowledgeIndex? index = await _Ai.App.Database.Ai.Assistants.ReadIndexAsync(assistant.Id, token).ConfigureAwait(false);
            if (index == null || String.IsNullOrEmpty(index.CollectionId) || index.ChunkCount == 0) return new List<KnowledgeCandidate>();
            List<float[]> vectors = await _Ai.Invoker.EmbedAsync(embedding, new List<string> { query }, assistant.Id, isTest, token).ConfigureAwait(false);
            if (vectors.Count == 0) return new List<KnowledgeCandidate>();
            List<VectorHit> hits;
            try
            {
                hits = await _Ai.VectorStore.SearchAsync(index.CollectionId, vectors[0], _Ai.App.Settings.Ai.VectorCandidates, token).ConfigureAwait(false);
            }
            catch (Exception e) when (e is not OperationCanceledException && e is not ApiErrorException)
            {
                _Ai.App.Logger.Warn("[KnowledgeRetrievalService] vector search failed for " + assistant.Id + ": " + e.Message);
                throw AiErrors.ModelUnavailable();
            }
            List<KnowledgeCandidate> candidates = new List<KnowledgeCandidate>();
            int checks = 0;
            foreach (IGrouping<string, VectorHit> group in hits.GroupBy(h => h.DocumentId))
            {
                if (checks >= Math.Max(assistant.MaxContextItems * 3, 10)) break;
                VectorHit best = group.OrderByDescending(h => h.Score).First();
                string targetType = best.Tags.TryGetValue("targetType", out string? t) ? t : "FeedPost";
                string targetId = best.Tags.TryGetValue("targetId", out string? id) ? id : group.Key.Split(':').Last();
                if (targetType != "FeedPost")
                {
                    if (!Enum.TryParse(targetType, false, out TargetTypeEnum providerType) || !KnowledgeTypes.IsAvailable(_Ai.App, providerType)) continue;
                    checks++;
                    ContentTargetProvider provider = _Ai.App.ContentTargets.Find(providerType)!;
                    if (!await provider.IsVisibleAsync(ActorOf(caller), providerType, targetId, token).ConfigureAwait(false)) continue;
                    candidates.Add(new KnowledgeCandidate
                    {
                        TargetType = providerType.ToString(),
                        TargetId = targetId,
                        Title = best.Tags.TryGetValue("title", out string? title) && !String.IsNullOrEmpty(title) ? title : TitleOf(null, best.Content),
                        Path = provider.PathOf(providerType, targetId),
                        Score = best.Score,
                        Excerpt = Cut(best.Content, 1200)
                    });
                    continue;
                }
                checks++;
                KnowledgeCandidate? visible = await RecheckAsync(caller, targetId, token).ConfigureAwait(false);
                if (visible == null) continue;
                visible.Score = best.Score;
                visible.Excerpt = Cut(best.Content, 1200);
                candidates.Add(visible);
            }
            return candidates;
        }

        private async Task<KnowledgeCandidate?> RecheckAsync(AssistantCaller caller, string postId, CancellationToken token)
        {
            try
            {
                if (caller.UserId == null && !caller.IsSystemAdmin)
                {
                    PublicFeedPost post = await _Ai.App.Feed.GetPublicAsync(postId, caller.SourceIp, token).ConfigureAwait(false);
                    return Candidate(post.Id, post.Title, post.Body);
                }
                FeedPostView view = await _Ai.App.Feed.GetAsync(caller.Actor, postId, token).ConfigureAwait(false);
                if (view.State != FeedPostStateEnum.Published || (view.Moderation != null && view.Moderation.State != ModerationStateEnum.None)) return null;
                return Candidate(view.Id, view.Title, view.Body);
            }
            catch (ApiErrorException)
            {
                return null;
            }
        }

        private static void Add(Dictionary<string, KnowledgeCandidate> found, string id, string? title, string body, List<string> keywords)
        {
            string text = ((title ?? String.Empty) + " " + MarkdownNormalizer.ToPlainText(body)).ToLowerInvariant();
            double score = keywords.Sum(k => text.Contains(k, StringComparison.Ordinal) ? 1.0 : 0.0) / keywords.Count;
            if (found.TryGetValue(id, out KnowledgeCandidate? existing))
            {
                existing.Score = Math.Max(existing.Score, score);
                return;
            }
            KnowledgeCandidate candidate = Candidate(id, title, body);
            candidate.Score = score;
            found[id] = candidate;
        }

        private static KnowledgeCandidate Candidate(string id, string? title, string body)
        {
            return new KnowledgeCandidate
            {
                TargetType = "FeedPost",
                TargetId = id,
                Title = TitleOf(title, body),
                Path = "/feed/" + id,
                Excerpt = Cut(MarkdownNormalizer.ToPlainText(body).Trim(), 1200)
            };
        }

        private static List<KnowledgeCandidate> Fuse(List<KnowledgeCandidate> keyword, List<KnowledgeCandidate> vector)
        {
            Dictionary<string, KnowledgeCandidate> byId = new Dictionary<string, KnowledgeCandidate>(StringComparer.Ordinal);
            Dictionary<string, double> scores = new Dictionary<string, double>(StringComparer.Ordinal);
            foreach (List<KnowledgeCandidate> list in new[] { keyword, vector })
            {
                for (int rank = 0; rank < list.Count; rank++)
                {
                    KnowledgeCandidate c = list[rank];
                    string key = c.TargetType + ":" + c.TargetId;
                    if (!byId.ContainsKey(key)) byId[key] = c;
                    scores[key] = (scores.TryGetValue(key, out double s) ? s : 0) + 1.0 / (_RrfK + rank + 1);
                }
            }
            return scores.OrderByDescending(p => p.Value).Select(p =>
            {
                KnowledgeCandidate c = byId[p.Key];
                c.Score = p.Value;
                return c;
            }).ToList();
        }

        private static string? BuildContext(List<KnowledgeCandidate> items, int maxCharacters)
        {
            if (items.Count == 0) return null;
            StringBuilder block = new StringBuilder("Community knowledge for this question. Each item has a key such as [1]. Use the items only when they are relevant, cite the keys of the items you use, and say so when they do not answer the question.\n");
            int index = 1;
            foreach (KnowledgeCandidate item in items)
            {
                string entry = "\n[" + index.ToString(CultureInfo.InvariantCulture) + "] " + item.Title + "\n" + item.Excerpt + "\n";
                if (block.Length + entry.Length > maxCharacters)
                {
                    int room = maxCharacters - block.Length;
                    if (room < 200) break;
                    entry = entry.Substring(0, room);
                }
                item.Index = index++;
                block.Append(entry);
            }
            items.RemoveAll(i => i.Index == 0);
            return block.ToString();
        }

        private static List<string> Keywords(string query)
        {
            List<string> words = new List<string>();
            StringBuilder current = new StringBuilder();
            foreach (char c in query.ToLowerInvariant() + " ")
            {
                if (Char.IsLetterOrDigit(c))
                {
                    current.Append(c);
                    continue;
                }
                if (current.Length >= 3 && !_StopWords.Contains(current.ToString())) words.Add(current.ToString());
                current.Clear();
            }
            return words.Distinct(StringComparer.Ordinal).OrderByDescending(w => w.Length).Take(_MaxKeywords).ToList();
        }

        private static string Cut(string text, int max)
        {
            return text.Length <= max ? text : text.Substring(0, max);
        }

        #endregion
    }
}
