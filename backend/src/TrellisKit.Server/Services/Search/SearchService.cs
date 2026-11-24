namespace TrellisKit.Server.Services
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Globalization;
    using System.Linq;
    using System.Text.RegularExpressions;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core.Enumeration;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Exceptions;
    using TrellisKit.Core.Models;
    using TrellisKit.Core.Observability;
    using TrellisKit.Core.Requests;
    using TrellisKit.Core.Responses;

    /// <summary>
    /// Engine search (Phase 3 resolved decisions 11 and 13): a background indexer keeps the property's search index
    /// current from a queue (writes enqueue directly; a change sweep catches everything else), thread search candidates
    /// for the thread lists, and global search over feed posts, threads, comments, and members. Every result passes the
    /// same visibility rules as the lists; the index only ranks.
    /// Thread safety: safe to share; index writes are serialized by the index.
    /// </summary>
    public class SearchService : IAsyncDisposable
    {
        #region Public-Members

        /// <summary>
        /// Searchable types of the global search.
        /// </summary>
        public static readonly IReadOnlyList<string> Types = new[] { "FeedPost", "Thread", "Comment", "User", "Venue", "Instructor", "Event", "Series", "Lesson", "Idea" };

        /// <summary>
        /// The global search types served by content target providers (docs/PHASE4_CONTRACT.md section 7). A type is
        /// searched only while a provider serves it.
        /// </summary>
        public static readonly IReadOnlyList<TargetTypeEnum> ProviderTypes = new[] { TargetTypeEnum.Venue, TargetTypeEnum.Instructor, TargetTypeEnum.Event, TargetTypeEnum.Series, TargetTypeEnum.Lesson };

        /// <summary>
        /// Most changed object ids requested from each content target provider type per sweep or rebuild. Default
        /// 5000, minimum 1, maximum 100000; objects beyond it are picked up by the next sweep.
        /// </summary>
        public int ProviderSweepLimit
        {
            get { return _ProviderSweepLimit; }
            set
            {
                if (value < 1 || value > 100000) throw new ArgumentOutOfRangeException(nameof(ProviderSweepLimit), "ProviderSweepLimit must be 1 to 100000.");
                _ProviderSweepLimit = value;
            }
        }

        /// <summary>
        /// Longest snippet text.
        /// </summary>
        public const int SnippetLength = 200;

        /// <summary>
        /// Queue entries are retried this many times before they are left for a rebuild.
        /// </summary>
        public const int MaxAttempts = 5;

        /// <summary>
        /// Signal that wakes the indexer (set by the property runtime).
        /// </summary>
        public Action? Signal { get; set; } = null;

        /// <summary>
        /// Creates the property's index on first use; tests may replace it. Default: Verbex on PostgreSQL.
        /// </summary>
        public Func<ISearchIndex>? IndexFactory { get; set; } = null;

        #endregion

        #region Private-Members

        private static readonly Regex _Token = new Regex("[\\p{L}\\p{N}]+", RegexOptions.Compiled | RegexOptions.CultureInvariant);
        private readonly ApplicationServices _Services;
        private readonly object _IndexLock = new object();
        private readonly SemaphoreSlim _DrainLock = new SemaphoreSlim(1, 1);
        private ISearchIndex? _Index = null;
        private int _ProviderSweepLimit = 5000;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="services">Property services.</param>
        /// <exception cref="ArgumentNullException">The services are null.</exception>
        public SearchService(ApplicationServices services)
        {
            _Services = services ?? throw new ArgumentNullException(nameof(services));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// The property's search index (created on first use).
        /// </summary>
        public ISearchIndex Index
        {
            get
            {
                lock (_IndexLock)
                {
                    if (_Index != null) return _Index;
                    _Index = IndexFactory != null ? IndexFactory() : new VerbexSearchIndex(_Services.Settings.Database, _Services.Settings.Search, _Services.PropertyId, EnsureSchemaAsync);
                    return _Index;
                }
            }
        }

        /// <summary>
        /// Queue an object for indexing and wake the indexer.
        /// </summary>
        /// <param name="targetType">Object type.</param>
        /// <param name="targetId">Object identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        public async Task EnqueueAsync(TargetTypeEnum targetType, string targetId, CancellationToken token = default)
        {
            if (!_Services.Settings.Search.Enabled || String.IsNullOrEmpty(targetId)) return;
            await _Services.Database.SearchQueue.EnqueueAsync(targetType, targetId, DateTime.UtcNow, token).ConfigureAwait(false);
            try
            {
                Signal?.Invoke();
            }
            catch (InvalidOperationException e)
            {
                _Services.Logger.Warn("[SearchService] indexer signal failed: " + e.Message);
            }
        }

        /// <summary>
        /// Queue every object changed since the last sweep (the first sweep queues everything).
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Entries queued.</returns>
        public async Task<int> SweepAsync(CancellationToken token = default)
        {
            DateTime now = DateTime.UtcNow;
            DateTime? watermark = await _Services.Database.SearchQueue.ReadWatermarkAsync(token).ConfigureAwait(false);
            int queued = watermark == null
                ? await _Services.Database.SearchQueue.EnqueueAllAsync(now, token).ConfigureAwait(false)
                : await _Services.Database.SearchQueue.EnqueueChangedAsync(watermark.Value.AddSeconds(-1), now, token).ConfigureAwait(false);
            queued += await EnqueueProvidersAsync(watermark?.AddSeconds(-1), now, token).ConfigureAwait(false);
            await _Services.Database.SearchQueue.WriteWatermarkAsync(now, token).ConfigureAwait(false);
            return queued;
        }

        /// <summary>
        /// Index up to a batch of queued objects (add, replace, or remove each document).
        /// </summary>
        /// <param name="limit">Most entries.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Entries processed.</returns>
        public async Task<int> DrainAsync(int limit, CancellationToken token = default)
        {
            if (!_Services.Settings.Search.Enabled) return 0;
            await _DrainLock.WaitAsync(token).ConfigureAwait(false);
            try
            {
                List<SearchQueueItem> items = await _Services.Database.SearchQueue.PeekAsync(Math.Max(1, limit), MaxAttempts, token).ConfigureAwait(false);
                int done = 0;
                foreach (SearchQueueItem item in items)
                {
                    token.ThrowIfCancellationRequested();
                    try
                    {
                        SearchDocument? document = await DocumentAsync(item.TargetType, item.TargetId, token).ConfigureAwait(false);
                        string id = item.TargetType + ":" + item.TargetId;
                        if (document == null) await Index.RemoveAsync(id, token).ConfigureAwait(false);
                        else await Index.UpsertAsync(document, token).ConfigureAwait(false);
                        await _Services.Database.SearchQueue.CompleteAsync(item, token).ConfigureAwait(false);
                        done++;
                    }
                    catch (Exception e) when (e is not OperationCanceledException)
                    {
                        _Services.Logger.Warn("[SearchService] indexing " + item.TargetType + " " + item.TargetId + " failed: " + e.Message);
                        await _Services.Database.SearchQueue.FailAsync(item, token).ConfigureAwait(false);
                    }
                }
                return done;
            }
            finally
            {
                _DrainLock.Release();
            }
        }

        /// <summary>
        /// Sweep and drain until the queue is empty (tests, and the rebuild route).
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Entries indexed.</returns>
        public async Task<int> IndexNowAsync(CancellationToken token = default)
        {
            await SweepAsync(token).ConfigureAwait(false);
            int total = 0;
            for (int pass = 0; pass < 1000; pass++)
            {
                int done = await DrainAsync(_Services.Settings.Search.IndexBatchSize, token).ConfigureAwait(false);
                total += done;
                if (done == 0) break;
            }
            return total;
        }

        /// <summary>
        /// POST /api/v1/admin/search/rebuild: clear the index and queue every object (SystemAdmin).
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="info">Request information.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Objects queued.</returns>
        /// <exception cref="ApiErrorException">403 Forbidden, 404 NotAvailable.</exception>
        public async Task<int> RebuildAsync(ContentActor actor, RequestInfo info, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            ArgumentNullException.ThrowIfNull(info);
            if (!actor.IsSystemAdmin) throw ApiErrorException.Forbidden("Only a SystemAdmin can rebuild the search index.");
            if (!_Services.Settings.Search.Enabled) throw new ApiErrorException(404, "NotAvailable", "Search is turned off (search.enabled).");
            await Index.ClearAsync(token).ConfigureAwait(false);
            DateTime now = DateTime.UtcNow;
            int queued = await _Services.Database.SearchQueue.EnqueueAllAsync(now, token).ConfigureAwait(false);
            queued += await EnqueueProvidersAsync(null, now, token).ConfigureAwait(false);
            await _Services.Database.SearchQueue.WriteWatermarkAsync(now, token).ConfigureAwait(false);
            await _Services.Audit.WriteAsync(info, "search.rebuild", null, null, queued + " objects queued", token).ConfigureAwait(false);
            Signal?.Invoke();
            return queued;
        }

        /// <summary>
        /// Ranked thread candidates of a thread search (titles weigh most, then bodies and poll options, then comments).
        /// </summary>
        /// <param name="search">Search text.</param>
        /// <param name="includeComments">Whether comment matches count.</param>
        /// <param name="forumId">Limit to one forum, or null.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Candidates.</returns>
        public async Task<ThreadSearchCandidates> ThreadCandidatesAsync(string search, bool includeComments, string? forumId, CancellationToken token = default)
        {
            ThreadSearchCandidates result = new ThreadSearchCandidates();
            if (!_Services.Settings.Search.Enabled) return result;
            Stopwatch stopwatch = Stopwatch.StartNew();
            string outcome = "success";
            try
            {
                int limit = _Services.Settings.Search.CandidateLimit;
                Dictionary<string, string> threadTags = new Dictionary<string, string>(StringComparer.Ordinal) { { "tk.type", "Thread" } };
                if (forumId != null) threadTags["tk.forum"] = forumId;
                List<SearchHit> threads = await Index.SearchAsync(search, Array.Empty<string>(), threadTags, limit, token).ConfigureAwait(false);
                Dictionary<string, double> scores = threads.ToDictionary(h => h.ObjectId, h => h.Score, StringComparer.Ordinal);
                if (includeComments)
                {
                    Dictionary<string, string> commentTags = new Dictionary<string, string>(StringComparer.Ordinal) { { "tk.type", "Comment" } };
                    if (forumId != null) commentTags["tk.forum"] = forumId;
                    List<SearchHit> comments = await Index.SearchAsync(search, Array.Empty<string>(), commentTags, limit, token).ConfigureAwait(false);
                    List<Comment> rows = await _Services.Database.Comments.ReadManyAsync(comments.Select(h => h.ObjectId), token).ConfigureAwait(false);
                    Dictionary<string, Comment> byId = rows.ToDictionary(c => c.Id, StringComparer.Ordinal);
                    List<string> feedTargets = rows.Where(c => c.TargetType == TargetTypeEnum.FeedPost).Select(c => c.TargetId).Distinct(StringComparer.Ordinal).ToList();
                    Dictionary<string, string> linked = new Dictionary<string, string>(StringComparer.Ordinal);
                    foreach (string postId in feedTargets)
                    {
                        ForumThread? thread = await _Services.Database.Threads.ReadBySourceAsync(TargetTypeEnum.FeedPost.ToString(), postId, token).ConfigureAwait(false);
                        if (thread != null && (forumId == null || thread.ForumId == forumId)) linked[postId] = thread.Id;
                    }
                    foreach (SearchHit hit in comments)
                    {
                        if (!byId.TryGetValue(hit.ObjectId, out Comment? comment) || comment.DeletedUtc != null || comment.ModerationState != ModerationStateEnum.None) continue;
                        string? threadId = comment.TargetType == TargetTypeEnum.Thread ? comment.TargetId : (linked.TryGetValue(comment.TargetId, out string? t) ? t : null);
                        if (threadId == null) continue;
                        double score = hit.Score * 0.8;
                        if (!result.CommentHits.ContainsKey(threadId)) result.CommentHits[threadId] = comment.Id;
                        if (!scores.TryGetValue(threadId, out double existing) || score > existing) scores[threadId] = score;
                    }
                }
                result.ThreadIds = scores.OrderByDescending(p => p.Value).ThenBy(p => p.Key, StringComparer.Ordinal).Take(limit).Select(p => p.Key).ToList();
                return result;
            }
            catch (Exception e) when (e is not OperationCanceledException && e is not ApiErrorException)
            {
                outcome = "error";
                _Services.Logger.Warn("[SearchService] thread search failed: " + e.Message);
                return result;
            }
            finally
            {
                ForumTelemetry.Count(ForumTelemetry.Searches, "outcome", outcome, "include_comments", includeComments ? "true" : "false");
                TrellisKitTelemetry.Record(ForumTelemetry.SearchDuration, stopwatch.Elapsed.TotalSeconds, "provider", Index.Provider);
            }
        }

        /// <summary>
        /// Add search snippets to thread list items (title, then body, poll options, then the matching comment).
        /// </summary>
        /// <param name="views">Thread list items.</param>
        /// <param name="contexts">Their contexts, in the same order.</param>
        /// <param name="search">Search text.</param>
        /// <param name="candidates">Candidates with comment hits.</param>
        /// <param name="includeComments">Whether comment matches count.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        public async Task ApplySnippetsAsync(List<ThreadSummaryView> views, List<ThreadContext> contexts, string search, ThreadSearchCandidates candidates, bool includeComments, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(views);
            ArgumentNullException.ThrowIfNull(contexts);
            ArgumentNullException.ThrowIfNull(candidates);
            List<string> terms = Terms(search);
            Dictionary<string, Poll> polls = await _Services.Database.Polls.ReadByThreadsAsync(contexts.Where(c => c.Thread.Type == ThreadTypeEnum.Poll).Select(c => c.Thread.Id), token).ConfigureAwait(false);
            Dictionary<string, Comment> comments = includeComments
                ? (await _Services.Database.Comments.ReadManyAsync(candidates.CommentHits.Values, token).ConfigureAwait(false)).ToDictionary(c => c.Id, StringComparer.Ordinal)
                : new Dictionary<string, Comment>(StringComparer.Ordinal);
            for (int i = 0; i < views.Count && i < contexts.Count; i++)
            {
                ThreadContext c = contexts[i];
                SearchSnippetView? snippet = Snippet(c.Thread.Title, terms, "Title", null);
                string body = MarkdownNormalizer.ToPlainText(c.Thread.Kind == ThreadKindEnum.LinkedFeedPost ? c.Source?.Body : c.Thread.Body);
                snippet ??= Snippet(body, terms, "Body", null);
                if (snippet == null && polls.TryGetValue(c.Thread.Id, out Poll? poll))
                    snippet = Snippet(String.Join(" / ", poll.Options.Select(o => o.Label)), terms, "PollOption", null);
                if (snippet == null && candidates.CommentHits.TryGetValue(c.Thread.Id, out string? commentId) && comments.TryGetValue(commentId, out Comment? comment))
                    snippet = Snippet(MarkdownNormalizer.ToPlainText(comment.Body), terms, "Comment", comment.Id);
                views[i].SearchSnippet = snippet ?? Lead(c.Thread.Title, "Title");
            }
        }

        /// <summary>
        /// POST /api/v1/search/enumerate: one search across feed posts, threads, comments, and members (filters.types,
        /// labels, tags, and the created window), ranked by relevance (or the created orderings).
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="query">Query; search is required.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Page of results.</returns>
        /// <exception cref="ApiErrorException">400 ValidationFailed, 429 RateLimited.</exception>
        public async Task<EnumerationResult<SearchResultItem>> GlobalAsync(ContentActor actor, EnumerationQuery? query, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            _Services.Media.EnforceRead(actor);
            EnumerationQuery q = query ?? new EnumerationQuery();
            Dictionary<string, string> errors = new Dictionary<string, string>();
            List<string> types = ForumFilters.List(q, "types") ?? new List<string>();
            List<string> wanted = types.Count == 0 ? Types.ToList() : Types.Where(t => types.Contains(t, StringComparer.OrdinalIgnoreCase)).ToList();
            if (!_Services.Property.Property.Features.Forums) wanted.Remove("Thread");
            foreach (TargetTypeEnum providerType in ProviderTypes)
                if (_Services.ContentTargets.Find(providerType) == null) wanted.Remove(providerType.ToString());
            if (!_Services.Property.Property.Features.Ideas) wanted.Remove("Idea");
            EnumerationRequest request = _Services.Enumeration.Prepare(q, ForumCollections.Search, actor.IsStaffFor(TargetTypeEnum.Thread), "search|" + (actor.UserId ?? "-"));
            string search = (request.Query.Search ?? String.Empty).Trim();
            if (search.Length == 0) errors["search"] = "Required (1 to " + EnumerationNormalizer.MaxSearchLength + " characters).";
            if (errors.Count > 0) throw ApiErrorException.Validation(errors);
            _Services.Limits.EnforceMember(actor, "search.global", _Services.Settings.Search.PerMinute, RateLimitService.Minute);
            Stopwatch stopwatch = Stopwatch.StartNew();
            EnumerationResult<SearchResultItem> result = new EnumerationResult<SearchResultItem> { MaxResults = request.MaxResults, Skip = request.Skip };
            if (!_Services.Settings.Search.Enabled || wanted.Count == 0)
            {
                result.TotalRecords = 0;
                result.RecordsRemaining = 0;
                return result;
            }
            List<string> labels = request.Query.LabelMatch == LabelMatchEnum.All ? request.Query.Labels.Select(MetadataRules.Normalize).Distinct(StringComparer.Ordinal).ToList() : new List<string>();
            Dictionary<string, string> tags = request.Query.Tags.Where(t => t.Value != "*").ToDictionary(t => MetadataRules.Normalize(t.Key), t => t.Value, StringComparer.Ordinal);
            List<SearchHit> hits;
            string outcome = "success";
            try
            {
                hits = await Index.SearchAsync(search, labels, tags, _Services.Settings.Search.CandidateLimit, token).ConfigureAwait(false);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                outcome = "error";
                _Services.Logger.Warn("[SearchService] global search failed: " + e.Message);
                hits = new List<SearchHit>();
            }
            finally
            {
                TrellisKitTelemetry.Record(ForumTelemetry.SearchDuration, stopwatch.Elapsed.TotalSeconds, "provider", Index.Provider);
            }
            ForumTelemetry.Count(ForumTelemetry.Searches, "outcome", outcome, "include_comments", wanted.Contains("Comment") ? "true" : "false");
            hits = hits.Where(h => wanted.Contains(h.Type, StringComparer.Ordinal)).ToList();
            List<SearchResultItem> visible = await VisibleResultsAsync(actor, hits, Terms(search), request, token).ConfigureAwait(false);
            if (String.Equals(request.Ordering.Name, "CreatedDescending", StringComparison.Ordinal)) visible = visible.OrderByDescending(v => v.CreatedUtc).ThenByDescending(v => v.Id, StringComparer.Ordinal).ToList();
            else if (String.Equals(request.Ordering.Name, "CreatedAscending", StringComparison.Ordinal)) visible = visible.OrderBy(v => v.CreatedUtc).ThenBy(v => v.Id, StringComparer.Ordinal).ToList();
            int offset = request.Skip;
            if (request.HasPosition)
            {
                if (!Int32.TryParse(request.AfterKey, NumberStyles.Integer, CultureInfo.InvariantCulture, out offset) || offset < 0) throw ContinuationTokens.Invalid();
            }
            List<SearchResultItem> pageItems = visible.Skip(offset).Take(request.MaxResults).ToList();
            bool more = offset + pageItems.Count < visible.Count;
            result.Objects = pageItems;
            result.Skip = request.HasPosition ? 0 : request.Skip;
            result.EndOfResults = !more;
            result.TotalRecords = visible.Count;
            result.RecordsRemaining = Math.Max(0, visible.Count - offset - pageItems.Count);
            if (more && pageItems.Count > 0)
                result.ContinuationToken = ContinuationTokens.Encode(request.Fingerprint, request.Ordering.Name, (offset + pageItems.Count).ToString(CultureInfo.InvariantCulture), pageItems[pageItems.Count - 1].Id);
            return result;
        }

        /// <summary>
        /// A plain-text snippet of at most 200 characters around the first match of any term, with highlights.
        /// </summary>
        /// <param name="text">Plain text.</param>
        /// <param name="terms">Lowercase search terms.</param>
        /// <param name="field">Field name.</param>
        /// <param name="commentId">Comment identifier, or null.</param>
        /// <returns>Snippet, or null when no term matches.</returns>
        public static SearchSnippetView? Snippet(string? text, IReadOnlyList<string> terms, string field, string? commentId)
        {
            ArgumentNullException.ThrowIfNull(terms);
            string source = (text ?? String.Empty).Trim();
            if (source.Length == 0 || terms.Count == 0) return null;
            string lower = source.ToLowerInvariant();
            int first = -1;
            foreach (string term in terms)
            {
                int at = FindStem(lower, term);
                if (at >= 0 && (first < 0 || at < first)) first = at;
            }
            if (first < 0) return null;
            int start = Math.Max(0, first - 60);
            if (start > 0)
            {
                int space = source.IndexOf(' ', start);
                if (space >= 0 && space < first) start = space + 1;
            }
            int length = Math.Min(SnippetLength, source.Length - start);
            string cut = source.Substring(start, length);
            string cutLower = cut.ToLowerInvariant();
            List<SnippetHighlight> highlights = new List<SnippetHighlight>();
            foreach (string term in terms)
            {
                string stem = Stem(term);
                int index = 0;
                while (index < cutLower.Length)
                {
                    int at = cutLower.IndexOf(stem, index, StringComparison.Ordinal);
                    if (at < 0) break;
                    int end = at + stem.Length;
                    while (end < cutLower.Length && Char.IsLetterOrDigit(cutLower[end])) end++;
                    if (at == 0 || !Char.IsLetterOrDigit(cutLower[at - 1])) highlights.Add(new SnippetHighlight { Start = at, Length = end - at });
                    index = end;
                }
            }
            highlights = highlights.GroupBy(h => h.Start).Select(g => g.OrderByDescending(h => h.Length).First()).OrderBy(h => h.Start).ToList();
            return new SearchSnippetView { Field = field, CommentId = commentId, Text = cut, Highlights = highlights };
        }

        /// <summary>
        /// Lowercase search terms of a query.
        /// </summary>
        /// <param name="search">Search text.</param>
        /// <returns>Terms.</returns>
        public static List<string> Terms(string? search)
        {
            return _Token.Matches(search ?? String.Empty).Select(m => m.Value.ToLowerInvariant()).Where(t => t.Length > 0).Distinct(StringComparer.Ordinal).ToList();
        }

        /// <inheritdoc />
        public async ValueTask DisposeAsync()
        {
            ISearchIndex? index;
            lock (_IndexLock)
            {
                index = _Index;
                _Index = null;
            }
            if (index != null) await index.DisposeAsync().ConfigureAwait(false);
            GC.SuppressFinalize(this);
        }

        #endregion

        #region Private-Methods

        private async Task EnsureSchemaAsync(string schema, CancellationToken token)
        {
            if (!Regex.IsMatch(schema, "^[A-Za-z_][A-Za-z0-9_]{0,62}$")) throw new ArgumentException("search.schema must be a simple identifier: " + schema + ".");
            await _Services.Database.Deployment.ExecuteQueryAsync("CREATE SCHEMA IF NOT EXISTS " + schema + ";", false, token).ConfigureAwait(false);
        }

        private static string Stem(string term)
        {
            if (term.Length <= 4) return term;
            foreach (string suffix in new[] { "ing", "ed", "es", "s" })
                if (term.EndsWith(suffix, StringComparison.Ordinal) && term.Length - suffix.Length >= 3) return term.Substring(0, term.Length - suffix.Length);
            return term;
        }

        private static int FindStem(string lower, string term)
        {
            string stem = Stem(term);
            int index = 0;
            while (index < lower.Length)
            {
                int at = lower.IndexOf(stem, index, StringComparison.Ordinal);
                if (at < 0) return -1;
                if (at == 0 || !Char.IsLetterOrDigit(lower[at - 1])) return at;
                index = at + 1;
            }
            return -1;
        }

        private static SearchSnippetView Lead(string? text, string field)
        {
            string source = (text ?? String.Empty).Trim();
            return new SearchSnippetView { Field = field, Text = source.Length > SnippetLength ? source.Substring(0, SnippetLength) : source };
        }

        private async Task<SearchDocument?> DocumentAsync(TargetTypeEnum type, string id, CancellationToken token)
        {
            switch (type)
            {
                case TargetTypeEnum.Thread:
                    {
                        ForumThread? thread = await _Services.Database.Threads.ReadAsync(id, token).ConfigureAwait(false);
                        if (thread == null || ForumAccessService.StateOf(thread) != ThreadStateEnum.Published) return null;
                        string body = thread.Body ?? String.Empty;
                        if (thread.Kind == ThreadKindEnum.LinkedFeedPost && thread.SourceId != null)
                            body = (await _Services.Database.FeedPosts.ReadAsync(thread.SourceId, token).ConfigureAwait(false))?.Body ?? String.Empty;
                        string options = String.Empty;
                        if (thread.Type == ThreadTypeEnum.Poll)
                        {
                            Poll? poll = await _Services.Database.Polls.ReadByThreadAsync(thread.Id, token).ConfigureAwait(false);
                            if (poll != null) options = String.Join(" ", poll.Options.Select(o => o.Label));
                        }
                        SearchDocument document = await WithMetadataAsync(TargetTypeEnum.Thread, thread.Id, String.Join(" ", thread.Title, thread.Title, thread.Title, MarkdownNormalizer.ToPlainText(body), options), token).ConfigureAwait(false);
                        document.Tags["tk.type"] = "Thread";
                        document.Tags["tk.forum"] = thread.ForumId;
                        return document;
                    }
                case TargetTypeEnum.Comment:
                    {
                        Comment? comment = await _Services.Database.Comments.ReadAsync(id, token).ConfigureAwait(false);
                        if (comment == null || comment.DeletedUtc != null || comment.ModerationState != ModerationStateEnum.None || String.IsNullOrWhiteSpace(comment.Body)) return null;
                        SearchDocument document = await WithMetadataAsync(TargetTypeEnum.Comment, comment.Id, MarkdownNormalizer.ToPlainText(comment.Body), token).ConfigureAwait(false);
                        document.Tags["tk.type"] = "Comment";
                        document.Tags["tk.target"] = comment.TargetType + ":" + comment.TargetId;
                        ForumThread? thread = comment.TargetType == TargetTypeEnum.Thread
                            ? await _Services.Database.Threads.ReadAsync(comment.TargetId, token).ConfigureAwait(false)
                            : await _Services.Database.Threads.ReadBySourceAsync(TargetTypeEnum.FeedPost.ToString(), comment.TargetId, token).ConfigureAwait(false);
                        if (thread != null) document.Tags["tk.forum"] = thread.ForumId;
                        return document;
                    }
                case TargetTypeEnum.FeedPost:
                    {
                        FeedPost? post = await _Services.Database.FeedPosts.ReadAsync(id, token).ConfigureAwait(false);
                        if (post == null || post.Kind != FeedPostKindEnum.Original || post.StateAt(DateTime.UtcNow) != FeedPostStateEnum.Published) return null;
                        string title = post.Title ?? String.Empty;
                        SearchDocument document = await WithMetadataAsync(TargetTypeEnum.FeedPost, post.Id, String.Join(" ", title, title, title, MarkdownNormalizer.ToPlainText(post.Body)), token).ConfigureAwait(false);
                        document.Tags["tk.type"] = "FeedPost";
                        return document;
                    }
                case TargetTypeEnum.Idea:
                    {
                        Idea? idea = await _Services.Database.Ideas.ReadAsync(id, token).ConfigureAwait(false);
                        if (idea == null || idea.DeletedUtc != null || idea.ModerationState != ModerationStateEnum.None || idea.MergedIntoId != null) return null;
                        SearchDocument document = await WithMetadataAsync(TargetTypeEnum.Idea, idea.Id, String.Join(" ", idea.Title, idea.Title, idea.Title, MarkdownNormalizer.ToPlainText(idea.Body)), token).ConfigureAwait(false);
                        document.Tags["tk.type"] = "Idea";
                        return document;
                    }
                case TargetTypeEnum.User:
                    {
                        User? user = await _Services.Database.Users.ReadAsync(id, token).ConfigureAwait(false);
                        if (user == null || user.IsDeleted || user.IsBanned) return null;
                        SearchDocument document = await WithMetadataAsync(TargetTypeEnum.User, user.Id, String.Join(" ", user.DisplayName, user.DisplayName, user.Handle ?? String.Empty, user.Bio ?? String.Empty), token).ConfigureAwait(false);
                        document.Tags["tk.type"] = "User";
                        return document;
                    }
                default:
                    {
                        ContentTargetProvider? provider = ProviderTypes.Contains(type) ? _Services.ContentTargets.Find(type) : null;
                        if (provider == null) return null;
                        SearchDocument? document = await provider.SearchDocumentAsync(type, id, token).ConfigureAwait(false);
                        if (document == null) return null;
                        document.Id = type + ":" + id;
                        document.Tags["tk.type"] = type.ToString();
                        return document;
                    }
            }
        }

        private async Task<int> EnqueueProvidersAsync(DateTime? sinceUtc, DateTime nowUtc, CancellationToken token)
        {
            int queued = 0;
            foreach (TargetTypeEnum type in ProviderTypes)
            {
                ContentTargetProvider? provider = _Services.ContentTargets.Find(type);
                if (provider == null) continue;
                List<string> ids = await provider.ChangedIdsAsync(type, sinceUtc, _ProviderSweepLimit, token).ConfigureAwait(false);
                foreach (string id in ids.Where(i => !String.IsNullOrEmpty(i)).Distinct(StringComparer.Ordinal).Take(_ProviderSweepLimit))
                {
                    token.ThrowIfCancellationRequested();
                    await _Services.Database.SearchQueue.EnqueueAsync(type, id, nowUtc, token).ConfigureAwait(false);
                    queued++;
                }
            }
            return queued;
        }

        private async Task<SearchDocument> WithMetadataAsync(TargetTypeEnum type, string id, string content, CancellationToken token)
        {
            ObjectMetadata metadata = await _Services.Database.Metadata.ReadAsync(type, id, token).ConfigureAwait(false);
            SearchDocument document = new SearchDocument { Id = type + ":" + id, Content = content, Labels = metadata.Labels.Select(MetadataRules.Normalize).Distinct(StringComparer.Ordinal).ToList() };
            foreach (KeyValuePair<string, string> tag in metadata.Tags) document.Tags[MetadataRules.Normalize(tag.Key)] = tag.Value ?? String.Empty;
            return document;
        }

        private async Task<List<SearchResultItem>> VisibleResultsAsync(ContentActor actor, List<SearchHit> hits, List<string> terms, EnumerationRequest request, CancellationToken token)
        {
            DateTime now = DateTime.UtcNow;
            HashSet<string> hidden = String.IsNullOrEmpty(actor.UserId) ? new HashSet<string>(StringComparer.Ordinal) : await _Services.Blocks.HiddenAuthorsForAsync(actor.UserId, token).ConfigureAwait(false);
            HashSet<string> related = String.IsNullOrEmpty(actor.UserId) ? new HashSet<string>(StringComparer.Ordinal) : await _Services.Blocks.RelatedAsync(actor.UserId, token).ConfigureAwait(false);
            Dictionary<string, FeedPost> posts = (await _Services.Database.FeedPosts.ReadManyAsync(hits.Where(h => h.Type == "FeedPost").Select(h => h.ObjectId), token).ConfigureAwait(false)).ToDictionary(p => p.Id, StringComparer.Ordinal);
            Dictionary<string, Comment> comments = (await _Services.Database.Comments.ReadManyAsync(hits.Where(h => h.Type == "Comment").Select(h => h.ObjectId), token).ConfigureAwait(false)).ToDictionary(c => c.Id, StringComparer.Ordinal);
            List<string> threadIds = hits.Where(h => h.Type == "Thread").Select(h => h.ObjectId).Concat(comments.Values.Where(c => c.TargetType == TargetTypeEnum.Thread).Select(c => c.TargetId)).Distinct(StringComparer.Ordinal).ToList();
            Dictionary<string, ThreadContext> threads = (await _Services.Forums.Access.ContextsAsync(await _Services.Database.Threads.ReadManyAsync(threadIds, token).ConfigureAwait(false), token).ConfigureAwait(false))
                .ToDictionary(c => c.Thread.Id, StringComparer.Ordinal);
            List<string> commentPostIds = comments.Values.Where(c => c.TargetType == TargetTypeEnum.FeedPost && !posts.ContainsKey(c.TargetId)).Select(c => c.TargetId).Distinct(StringComparer.Ordinal).ToList();
            foreach (FeedPost post in await _Services.Database.FeedPosts.ReadManyAsync(commentPostIds, token).ConfigureAwait(false)) posts[post.Id] = post;
            Dictionary<string, User> users = (await _Services.Database.Users.ReadManyAsync(hits.Where(h => h.Type == "User").Select(h => h.ObjectId).ToList(), token).ConfigureAwait(false)).ToDictionary(u => u.Id, StringComparer.Ordinal);
            bool ideasOn = _Services.Property.Property.Features.Ideas;
            Dictionary<string, Idea> ideas = !ideasOn ? new Dictionary<string, Idea>(StringComparer.Ordinal)
                : (await _Services.Database.Ideas.ReadManyAsync(hits.Where(h => h.Type == "Idea").Select(h => h.ObjectId).Concat(comments.Values.Where(c => c.TargetType == TargetTypeEnum.Idea).Select(c => c.TargetId)), token).ConfigureAwait(false))
                    .ToDictionary(i => i.Id, StringComparer.Ordinal);

            List<SearchResultItem> items = new List<SearchResultItem>();
            List<string> authorIds = new List<string>();
            foreach (SearchHit hit in hits)
            {
                SearchResultItem? item = null;
                switch (hit.Type)
                {
                    case "FeedPost":
                        if (posts.TryGetValue(hit.ObjectId, out FeedPost? post) && post.Kind == FeedPostKindEnum.Original && post.StateAt(now) == FeedPostStateEnum.Published
                            && ContentAccessService.CanSeePost(post, actor, now) && !hidden.Contains(post.AuthorUserId))
                        {
                            item = new SearchResultItem { Type = "FeedPost", Id = post.Id, Title = post.Title, Path = "/feed/" + post.Id, CreatedUtc = post.CreatedUtc,
                                Snippet = Snippet(post.Title, terms, "Title", null) ?? Snippet(MarkdownNormalizer.ToPlainText(post.Body), terms, "Body", null) ?? Lead(post.Excerpt, "Body"),
                                Author = new UserSummary { Id = post.AuthorUserId } };
                        }
                        break;
                    case "Thread":
                        if (threads.TryGetValue(hit.ObjectId, out ThreadContext? context) && ForumAccessService.StateOf(context.Thread) == ThreadStateEnum.Published
                            && ForumAccessService.CanSeeThread(context, actor) && !hidden.Contains(context.Thread.AuthorUserId))
                        {
                            string body = MarkdownNormalizer.ToPlainText(context.Thread.Kind == ThreadKindEnum.LinkedFeedPost ? context.Source?.Body : context.Thread.Body);
                            item = new SearchResultItem { Type = "Thread", Id = context.Thread.Id, Title = context.Thread.Title, Path = ForumNotificationService.ThreadPath(context.Thread.Id, null),
                                CreatedUtc = context.Thread.CreatedUtc, Forum = new ForumRefView { Id = context.Forum.Id, Slug = context.Forum.Slug, Name = context.Forum.Name },
                                Snippet = Snippet(context.Thread.Title, terms, "Title", null) ?? Snippet(body, terms, "Body", null) ?? Lead(context.Thread.Title, "Title"),
                                Author = new UserSummary { Id = context.Thread.AuthorUserId } };
                        }
                        break;
                    case "Comment":
                        if (comments.TryGetValue(hit.ObjectId, out Comment? comment) && comment.DeletedUtc == null && comment.ModerationState == ModerationStateEnum.None && !hidden.Contains(comment.AuthorUserId))
                        {
                            if (comment.TargetType == TargetTypeEnum.Thread && threads.TryGetValue(comment.TargetId, out ThreadContext? owner)
                                && ForumAccessService.StateOf(owner.Thread) == ThreadStateEnum.Published && ForumAccessService.CanSeeThread(owner, actor))
                            {
                                item = new SearchResultItem { Type = "Comment", Id = comment.Id, Path = ForumNotificationService.ThreadPath(owner.Thread.Id, comment.Id), CreatedUtc = comment.CreatedUtc,
                                    Forum = new ForumRefView { Id = owner.Forum.Id, Slug = owner.Forum.Slug, Name = owner.Forum.Name } };
                            }
                            else if (comment.TargetType == TargetTypeEnum.Idea && ideas.TryGetValue(comment.TargetId, out Idea? ideaOwner) && ideaOwner.ModerationState == ModerationStateEnum.None
                                && IdeaService.CanRead(ideaOwner, actor))
                            {
                                item = new SearchResultItem { Type = "Comment", Id = comment.Id, Path = IdeaNotificationService.IdeaPath(ideaOwner.Id, comment.Id), CreatedUtc = comment.CreatedUtc };
                            }
                            else if (comment.TargetType == TargetTypeEnum.FeedPost && posts.TryGetValue(comment.TargetId, out FeedPost? parent) && parent.StateAt(now) == FeedPostStateEnum.Published
                                && ContentAccessService.CanSeePost(parent, actor, now))
                            {
                                item = new SearchResultItem { Type = "Comment", Id = comment.Id, Path = "/feed/" + parent.Id + "?comment=" + comment.Id, CreatedUtc = comment.CreatedUtc };
                            }
                            if (item != null)
                            {
                                item.Snippet = Snippet(MarkdownNormalizer.ToPlainText(comment.Body), terms, "Comment", comment.Id) ?? Lead(comment.Excerpt, "Comment");
                                item.Author = new UserSummary { Id = comment.AuthorUserId };
                            }
                        }
                        break;
                    case "Venue":
                    case "Instructor":
                    case "Event":
                    case "Series":
                    case "Lesson":
                        {
                            TargetTypeEnum providerType = Enum.Parse<TargetTypeEnum>(hit.Type);
                            ContentTargetProvider? provider = _Services.ContentTargets.Find(providerType);
                            // Every hit passes the object's own visibility check as the caller (locked stubs are not results).
                            if (provider == null || !await provider.IsVisibleAsync(actor, providerType, hit.ObjectId, token).ConfigureAwait(false)) break;
                            SearchResultItem? found = await provider.SearchResultAsync(actor, providerType, hit.ObjectId, terms, token).ConfigureAwait(false);
                            if (found == null) break;
                            found.Type = providerType.ToString();
                            found.Id = hit.ObjectId;
                            if (String.IsNullOrEmpty(found.Path)) found.Path = provider.PathOf(providerType, hit.ObjectId);
                            if (found.Author?.Id != null && hidden.Contains(found.Author.Id)) break;
                            item = found;
                        }
                        break;
                    case "Idea":
                        if (ideas.TryGetValue(hit.ObjectId, out Idea? idea) && idea.ModerationState == ModerationStateEnum.None && idea.MergedIntoId == null && IdeaService.CanRead(idea, actor)
                            && (idea.AuthorUserId == null || !idea.ShowAuthor || !hidden.Contains(idea.AuthorUserId)))
                        {
                            item = new SearchResultItem { Type = "Idea", Id = idea.Id, Title = idea.Title, Path = IdeaNotificationService.IdeaPath(idea.Id, null), CreatedUtc = idea.CreatedUtc,
                                Snippet = Snippet(idea.Title, terms, "Title", null) ?? Snippet(MarkdownNormalizer.ToPlainText(idea.Body), terms, "Body", null) ?? Lead(idea.Title, "Title"),
                                Author = idea.ShowAuthor && idea.AuthorUserId != null ? new UserSummary { Id = idea.AuthorUserId } : null };
                        }
                        break;
                    case "User":
                        if (users.TryGetValue(hit.ObjectId, out User? user) && !user.IsDeleted && !user.IsBanned && !related.Contains(user.Id))
                        {
                            item = new SearchResultItem { Type = "User", Id = user.Id, Title = user.DisplayName, Path = "/members/" + user.Id, CreatedUtc = user.CreatedUtc,
                                Snippet = Snippet(user.DisplayName + (user.Handle == null ? String.Empty : " @" + user.Handle), terms, "DisplayName", null) ?? Snippet(user.Bio, terms, "Bio", null) ?? Lead(user.DisplayName, "DisplayName"),
                                Author = new UserSummary { Id = user.Id } };
                        }
                        break;
                }
                if (item == null) continue;
                if (request.Query.CreatedAfterUtc != null && item.CreatedUtc < request.Query.CreatedAfterUtc.Value) continue;
                if (request.Query.CreatedBeforeUtc != null && item.CreatedUtc >= request.Query.CreatedBeforeUtc.Value) continue;
                items.Add(item);
                if (item.Author?.Id != null) authorIds.Add(item.Author.Id);
            }
            Dictionary<string, UserSummary> summaries = await _Services.Summaries.BuildAsync(authorIds.Select(a => (string?)a), token).ConfigureAwait(false);
            foreach (SearchResultItem item in items)
                if (item.Author?.Id != null) item.Author = summaries.TryGetValue(item.Author.Id, out UserSummary? s) ? s : UserSummary.Deleted();
            List<SearchResultItem> filtered = new List<SearchResultItem>();
            foreach (IGrouping<string, SearchResultItem> group in items.GroupBy(i => i.Type))
            {
                TargetTypeEnum type = Enum.Parse<TargetTypeEnum>(group.Key);
                Dictionary<string, ObjectMetadata> metadata = await _Services.Metadata.LoadAsync(type, group.Select(i => i.Id), actor.IsStaffFor(type), token).ConfigureAwait(false);
                foreach (SearchResultItem item in group)
                {
                    ObjectMetadata meta = metadata.TryGetValue(item.Id, out ObjectMetadata? m) ? m : new ObjectMetadata();
                    item.Labels = meta.Labels;
                    item.Tags = meta.Tags;
                }
            }
            foreach (SearchResultItem item in items)
                if (MatchesMetadata(item, request.Query)) filtered.Add(item);
            filtered = CollapseRecurringEvents(filtered, now);
            await DescribeRecurrencesAsync(filtered, token).ConfigureAwait(false);
            return filtered;
        }

        /// <summary>
        /// Collapse the occurrences of each recurring event series into one result, in the position of its first hit. The
        /// result opens the next upcoming occurrence that is not canceled, else the next upcoming canceled one, else the
        /// latest past occurrence; OccurrenceCount is the number of matching occurrences.
        /// </summary>
        /// <param name="items">Results in rank order.</param>
        /// <param name="nowUtc">Current time.</param>
        /// <returns>Results with one entry per series.</returns>
        /// <exception cref="ArgumentNullException">The list is null.</exception>
        public static List<SearchResultItem> CollapseRecurringEvents(List<SearchResultItem> items, DateTime nowUtc)
        {
            ArgumentNullException.ThrowIfNull(items);
            Dictionary<string, List<SearchResultItem>> series = new Dictionary<string, List<SearchResultItem>>(StringComparer.Ordinal);
            foreach (SearchResultItem item in items)
            {
                string? key = item.Event?.RecurrenceId;
                if (key == null) continue;
                if (!series.TryGetValue(key, out List<SearchResultItem>? list)) series[key] = list = new List<SearchResultItem>();
                list.Add(item);
            }
            if (series.Count == 0) return items;
            List<SearchResultItem> result = new List<SearchResultItem>();
            HashSet<string> placed = new HashSet<string>(StringComparer.Ordinal);
            foreach (SearchResultItem item in items)
            {
                string? key = item.Event?.RecurrenceId;
                if (key == null) { result.Add(item); continue; }
                if (!placed.Add(key)) continue;
                List<SearchResultItem> group = series[key];
                List<SearchResultItem> upcoming = group.Where(g => g.Event!.StartUtc >= nowUtc || !g.Event.IsPast).OrderBy(g => g.Event!.StartUtc).ToList();
                SearchResultItem chosen = upcoming.FirstOrDefault(g => !g.Event!.IsCanceled) ?? upcoming.FirstOrDefault() ?? group.OrderByDescending(g => g.Event!.StartUtc).First();
                chosen.Event!.OccurrenceCount = group.Count;
                result.Add(chosen);
            }
            return result;
        }

        private async Task DescribeRecurrencesAsync(List<SearchResultItem> items, CancellationToken token)
        {
            List<SearchResultItem> recurring = items.Where(i => i.Event?.RecurrenceId != null).ToList();
            if (recurring.Count == 0) return;
            Dictionary<string, EventRecurrence> recurrences = await _Services.Database.Events.Recurrences.ReadManyAsync(recurring.Select(i => i.Event!.RecurrenceId!).Distinct(StringComparer.Ordinal), token).ConfigureAwait(false);
            foreach (SearchResultItem item in recurring)
            {
                if (!recurrences.TryGetValue(item.Event!.RecurrenceId!, out EventRecurrence? recurrence)) continue;
                TimeZoneInfo zone = EventTimeZones.Find(item.Event.TimeZone) ?? TimeZoneInfo.Utc;
                DateTime anchor = EventTimeZones.TryParseLocal(recurrence.StartLocal, out DateTime a) ? a : EventTimeZones.ToLocal(item.Event.StartUtc, zone);
                item.Event.RecurrenceSummary = RecurrenceSummary.Describe(RecurrenceService.RuleOf(recurrence), anchor);
            }
        }

        private static bool MatchesMetadata(SearchResultItem item, EnumerationQuery query)
        {
            List<string> wanted = query.Labels.Select(MetadataRules.Normalize).Distinct(StringComparer.Ordinal).ToList();
            HashSet<string> own = new HashSet<string>(item.Labels.Select(MetadataRules.Normalize), StringComparer.Ordinal);
            if (wanted.Count > 0)
            {
                bool hit = query.LabelMatch == LabelMatchEnum.Any ? wanted.Any(own.Contains) : wanted.All(own.Contains);
                if (!hit) return false;
            }
            foreach (KeyValuePair<string, string> tag in query.Tags)
            {
                string? key = item.Tags.Keys.FirstOrDefault(k => String.Equals(MetadataRules.Normalize(k), MetadataRules.Normalize(tag.Key), StringComparison.Ordinal));
                if (key == null) return false;
                if (tag.Value != "*" && !String.Equals(item.Tags[key], tag.Value, StringComparison.Ordinal)) return false;
            }
            return true;
        }

        #endregion
    }
}
