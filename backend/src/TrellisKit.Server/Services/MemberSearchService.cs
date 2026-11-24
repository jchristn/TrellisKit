namespace TrellisKit.Server.Services
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Linq;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core.Database;
    using TrellisKit.Core.Enumeration;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Exceptions;
    using TrellisKit.Core.Observability;
    using TrellisKit.Core.Models;
    using TrellisKit.Core.Requests;
    using TrellisKit.Core.Responses;
    using TrellisKit.Core.Settings;

    /// <summary>
    /// Member search for mention autocomplete (contract section 3.4) and maintenance of the displaynamewords table it
    /// uses. Thread safety: safe to call concurrently.
    /// </summary>
    public class MemberSearchService
    {
        #region Public-Members

        /// <summary>
        /// Rate limit bucket.
        /// </summary>
        public const string RateBucket = "member-search";

        /// <summary>
        /// Longest query after the optional leading "@".
        /// </summary>
        public const int MaxQueryLength = 30;

        /// <summary>
        /// Default result count.
        /// </summary>
        public const int DefaultLimit = 8;

        /// <summary>
        /// Largest result count (maxResults is clamped to it).
        /// </summary>
        public const int MaxLimit = 20;

        #endregion

        #region Private-Members

        private readonly DatabaseDriverBase _Database;
        private readonly AppSettings _Settings;
        private readonly RateLimitService _Limits;
        private readonly UserSummaryService _Summaries;
        private readonly EnumerationService _Enumeration;
        private readonly MetadataService _Metadata;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the service.
        /// </summary>
        /// <param name="database">Database driver.</param>
        /// <param name="settings">Running settings (read live).</param>
        /// <param name="limits">Rate limits.</param>
        /// <param name="summaries">UserSummary builder.</param>
        /// <param name="enumeration">Enumeration preparation.</param>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        public MemberSearchService(DatabaseDriverBase database, AppSettings settings, RateLimitService limits, UserSummaryService summaries, EnumerationService enumeration)
        {
            _Enumeration = enumeration ?? throw new ArgumentNullException(nameof(enumeration));
            _Database = database ?? throw new ArgumentNullException(nameof(database));
            _Settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _Limits = limits ?? throw new ArgumentNullException(nameof(limits));
            _Summaries = summaries ?? throw new ArgumentNullException(nameof(summaries));
            _Metadata = new MetadataService(_Database, _Enumeration);
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Search members by a prefix of their handle or any display name word (ranked keyset pages, collection
        /// SafetyCollections.MemberSearch: maxResults default 8, capped at 20). Order: exact handle,
        /// then the context post's author and commenters (contextType FeedPost), then handle prefix, then display name
        /// word prefix. Excludes the caller, deleted, banned, and age-unattested accounts, members without an active
        /// membership (SystemAdmin holders are included), and both directions of the caller's blocks. Label, tag, and
        /// created-window filters apply to members. Created orderings and skip use the standard database enumeration.
        /// Staff receive exact totals; member totals follow enumeration.countMemberTotals.
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="query">Query (search, or the q alias: 1 to 30 characters after an optional leading "@"; filters contextType and contextId optional).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Matching members.</returns>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        /// <exception cref="ApiErrorException">400 ValidationFailed, 429 RateLimited.</exception>
        public async Task<EnumerationResult<UserSummary>> SearchAsync(ContentActor actor, MemberSearchQuery query, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            ArgumentNullException.ThrowIfNull(query);
            using (Activity? activity = TrellisKitTelemetry.StartSpan("members.search"))
            {
                Dictionary<string, string> errors = new Dictionary<string, string>();
                string prefix = (String.IsNullOrWhiteSpace(query.Search) ? query.Q ?? String.Empty : query.Search).Trim();
                if (prefix.StartsWith("@", StringComparison.Ordinal)) prefix = prefix.Substring(1);
                prefix = prefix.ToLowerInvariant();
                if (prefix.Length < 1 || prefix.Length > MaxQueryLength) errors["search"] = "Enter 1 to " + MaxQueryLength + " characters (an optional leading @ is ignored).";
                foreach (KeyValuePair<string, JsonElement> filter in query.Filters)
                {
                    if (filter.Value.ValueKind != JsonValueKind.String && filter.Value.ValueKind != JsonValueKind.Null)
                        errors["filters." + filter.Key] = "Must be a string.";
                }
                string? rawType = query.FilterString("contextType");
                string? contextId = query.FilterString("contextId");
                TargetTypeEnum? contextType = null;
                if (!String.IsNullOrWhiteSpace(rawType))
                {
                    if (!Int32.TryParse(rawType, out int _) && Enum.TryParse(rawType.Trim(), true, out TargetTypeEnum parsed) && (parsed == TargetTypeEnum.FeedPost || parsed == TargetTypeEnum.Thread)) contextType = parsed;
                    else errors["filters.contextType"] = "Use FeedPost or Thread.";
                    if (String.IsNullOrWhiteSpace(contextId)) errors["filters.contextId"] = "Required with contextType.";
                }
                else if (!String.IsNullOrWhiteSpace(contextId))
                {
                    errors["filters.contextType"] = "Required with contextId.";
                }
                query.Search = null;
                query.Q = null;
                if (errors.Count > 0)
                {
                    TrellisKitTelemetry.Add(TrellisKitTelemetry.MemberSearches, 1, "outcome", "validation_failed");
                    throw ApiErrorException.Validation(errors);
                }
                if (query.MaxResults == null) query.MaxResults = DefaultLimit;
                bool staff = actor.IsStaffFor(TargetTypeEnum.User);
                string scope = JsonSerializer.Serialize(new { prefix, actor.UserId, actor.IsAnonymous, staff, contextType, contextId = contextId?.Trim() });
                EnumerationRequest request = _Enumeration.Prepare(query, SafetyCollections.MemberSearch, staff, scope, MaxLimit);
                if (staff) request.CountTotals = true;
                try
                {
                    _Limits.EnforceMember(actor, RateBucket, _Settings.RateLimits.MemberSearchPerMinute, RateLimitService.Minute);
                }
                catch (ApiErrorException)
                {
                    TrellisKitTelemetry.Add(TrellisKitTelemetry.MemberSearches, 1, "outcome", "rate_limited");
                    throw;
                }
                MemberSearchCriteria criteria = new MemberSearchCriteria
                {
                    CallerUserId = actor.UserId,
                    Prefix = prefix,
                    ContextFeedPostId = contextType == TargetTypeEnum.FeedPost ? contextId!.Trim() : null,
                    ContextThreadId = contextType == TargetTypeEnum.Thread ? contextId!.Trim() : null,
                    NowUtc = DateTime.UtcNow
                };
                EnumerationResult<string> page = await _Database.MemberSearch.SearchAsync(criteria, request, token).ConfigureAwait(false);
                List<string> ids = page.Objects;
                Dictionary<string, UserSummary> summaries = await _Summaries.BuildAsync(ids.Select(id => (string?)id), staff ? ProfileViewerEnum.Staff : ProfileViewerEnum.Member, token).ConfigureAwait(false);
                Dictionary<string, ObjectMetadata> metadata = await _Metadata.LoadAsync(TargetTypeEnum.User, ids, staff, token).ConfigureAwait(false);
                List<UserSummary> items = new List<UserSummary>();
                foreach (string id in ids)
                {
                    if (!summaries.TryGetValue(id, out UserSummary? summary) || summary.IsDeleted) continue;
                    if (metadata.TryGetValue(id, out ObjectMetadata? values))
                    {
                        summary.Labels = values.Labels;
                        summary.Tags = values.Tags;
                    }
                    items.Add(summary);
                }
                TrellisKitTelemetry.Add(TrellisKitTelemetry.MemberSearches, 1, "outcome", items.Count == 0 ? "empty" : "success");
                activity?.SetTag("results", items.Count);
                return page.With(items);
            }
        }

        /// <summary>
        /// Replace a member's display name words (call whenever displayName is set or changed).
        /// </summary>
        /// <param name="userId">Member.</param>
        /// <param name="displayName">Current display name.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Words stored.</returns>
        /// <exception cref="ArgumentNullException">The user id is null or empty.</exception>
        public Task<int> UpdateWordsAsync(string userId, string? displayName, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(userId)) throw new ArgumentNullException(nameof(userId));
            return _Database.MemberSearch.ReplaceWordsAsync(userId, displayName, token);
        }

        /// <summary>
        /// Store words for every non-deleted member that has none (startup backfill; idempotent and cheap once done).
        /// </summary>
        /// <param name="batchSize">Members per batch. Default 500.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Batches processed.</returns>
        public async Task<int> BackfillAsync(int batchSize = 500, CancellationToken token = default)
        {
            int batches = 0;
            string? after = null;
            while (true)
            {
                token.ThrowIfCancellationRequested();
                string? last = await _Database.MemberSearch.BackfillBatchAsync(after, batchSize, token).ConfigureAwait(false);
                if (last == null) return batches;
                batches++;
                after = last;
            }
        }

        #endregion
    }
}
