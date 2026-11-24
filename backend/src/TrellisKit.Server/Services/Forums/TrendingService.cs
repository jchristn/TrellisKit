namespace TrellisKit.Server.Services
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Globalization;
    using System.Linq;
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
    /// Trending scores and the Hot ordering (docs/PHASE3_CONTRACT.md section 5):
    /// score = (wR * reactions + wC * comments + wU * uniqueCommenters) / (ageHours + ageOffsetHours) ^ gravity.
    /// </summary>
    public class TrendingService
    {
        #region Public-Members

        /// <summary>
        /// Clock used by recomputes and breakdowns (tests set a fixed clock).
        /// </summary>
        public Func<DateTime> Clock
        {
            get { return _Clock; }
            set { _Clock = value ?? throw new ArgumentNullException(nameof(Clock)); }
        }

        #endregion

        #region Private-Members

        private readonly ApplicationServices _Services;
        private Func<DateTime> _Clock = () => DateTime.UtcNow;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="services">Property services.</param>
        /// <exception cref="ArgumentNullException">The services are null.</exception>
        public TrendingService(ApplicationServices services)
        {
            _Services = services ?? throw new ArgumentNullException(nameof(services));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// The trending score for the current weights.
        /// </summary>
        /// <param name="reactions">Reactions on the conversation target.</param>
        /// <param name="comments">Visible comments.</param>
        /// <param name="uniqueCommenters">Distinct commenters other than the author.</param>
        /// <param name="ageHours">Hours since publish (negative values count as 0).</param>
        /// <returns>Score (0 or more).</returns>
        public double Score(long reactions, long comments, long uniqueCommenters, double ageHours)
        {
            double numerator = Numerator(reactions, comments, uniqueCommenters);
            double denominator = Denominator(ageHours);
            if (denominator <= 0 || Double.IsNaN(numerator) || Double.IsInfinity(numerator)) return 0;
            double score = numerator / denominator;
            return Double.IsNaN(score) || Double.IsInfinity(score) || score < 0 ? 0 : score;
        }

        /// <summary>
        /// The weighted sum of a score.
        /// </summary>
        /// <param name="reactions">Reactions.</param>
        /// <param name="comments">Comments.</param>
        /// <param name="uniqueCommenters">Distinct commenters.</param>
        /// <returns>Numerator.</returns>
        public double Numerator(long reactions, long comments, long uniqueCommenters)
        {
            return _Services.Settings.Trending.ReactionWeight * reactions + _Services.Settings.Trending.CommentWeight * comments + _Services.Settings.Trending.CommenterWeight * uniqueCommenters;
        }

        /// <summary>
        /// The age term of a score.
        /// </summary>
        /// <param name="ageHours">Hours since publish.</param>
        /// <returns>Denominator.</returns>
        public double Denominator(double ageHours)
        {
            return Math.Pow(Math.Max(0, ageHours) + _Services.Settings.Trending.AgeOffsetHours, _Services.Settings.Trending.Gravity);
        }

        /// <summary>
        /// Recompute the scores of threads active within the window in id-keyset batches and reset every other score to
        /// 0 (the trending worker's pass; the worker holds the singleton lease).
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Threads scored.</returns>
        public async Task<int> RecomputeAsync(CancellationToken token = default)
        {
            using (Activity? activity = TrellisKitTelemetry.StartSpan("trending.recompute"))
            {
                Stopwatch stopwatch = Stopwatch.StartNew();
                DateTime now = _Clock();
                DateTime windowStart = now.AddDays(-_Services.Settings.Trending.WindowDays);
                int batchSize = _Services.Settings.Trending.BatchSize;
                string? after = null;
                int scored = 0;
                while (true)
                {
                    token.ThrowIfCancellationRequested();
                    List<TrendingInput> batch = await _Services.Database.Threads.TrendingBatchAsync(windowStart, after, batchSize, token).ConfigureAwait(false);
                    if (batch.Count == 0) break;
                    Dictionary<string, double> scores = new Dictionary<string, double>(StringComparer.Ordinal);
                    foreach (TrendingInput input in batch)
                        scores[input.ThreadId] = Score(input.Reactions, input.Comments, input.UniqueCommenters, (now - input.PublishedUtc).TotalHours);
                    scored += await _Services.Database.Threads.SetScoresAsync(scores, now, token).ConfigureAwait(false);
                    after = batch[batch.Count - 1].ThreadId;
                    if (batch.Count < batchSize) break;
                }
                await _Services.Database.Threads.ZeroOutsideWindowAsync(windowStart, now, token).ConfigureAwait(false);
                ForumTelemetry.SetThreadsScored(await _Services.Database.Threads.CountScoredAsync(token).ConfigureAwait(false));
                TrellisKitTelemetry.Record(ForumTelemetry.TrendingDuration, stopwatch.Elapsed.TotalSeconds);
                activity?.SetTag("trending.scored", scored);
                activity?.SetStatus(ActivityStatusCode.Ok);
                return scored;
            }
        }

        /// <summary>
        /// POST /api/v1/admin/trending/enumerate: SystemAdmin sees every forum; moderators see their scope.
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="query">Query (ordering ScoreDescending; filters forumIds, type, promoted, minScore).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Page of trending rows.</returns>
        /// <exception cref="ApiErrorException">400 ValidationFailed, 403 Forbidden.</exception>
        public async Task<EnumerationResult<TrendingItemView>> QueueAsync(ContentActor actor, EnumerationQuery? query, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            _Services.Forums.Access.RequireEnabled();
            if (!actor.ModeratesAnyForum) throw ApiErrorException.Forbidden("A forum moderator role is required.");
            EnumerationQuery q = query ?? new EnumerationQuery();
            Dictionary<string, string> errors = new Dictionary<string, string>();
            double? minScore = null;
            string? rawMin = q.FilterString("minScore");
            if (rawMin != null)
            {
                if (Double.TryParse(rawMin, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed) && parsed >= 0) minScore = parsed;
                else errors["filters.minScore"] = "Must be a number of 0 or more.";
            }
            List<string> forumIds = ForumFilters.List(q, "forumIds") ?? new List<string>();
            if (forumIds.Count > ThreadService.MaxForumIds) errors["filters.forumIds"] = "At most " + ThreadService.MaxForumIds + " forums.";
            ThreadFilter filter = new ThreadFilter
            {
                ForumIds = forumIds,
                ScopeForumIds = actor.ForumScope,
                Types = ForumFilters.Enums<ThreadTypeEnum>(q, "type", errors),
                Promoted = q.FilterBool("promoted", errors),
                MinScore = minScore,
                ExcludeArchivedForums = true,
                NowUtc = _Clock()
            };
            if (errors.Count > 0) throw ApiErrorException.Validation(errors);
            EnumerationRequest request = _Services.Enumeration.Prepare(q, ForumCollections.Trending, true, "trending|" + (filter.ScopeForumIds == null ? "*" : String.Join(",", filter.ScopeForumIds)));
            EnumerationResult<ForumThread> page = await _Services.Database.Threads.EnumerateAsync(request, filter, token).ConfigureAwait(false);
            List<ThreadContext> contexts = await _Services.Forums.Access.ContextsAsync(page.Objects, token).ConfigureAwait(false);
            List<ThreadSummaryView> summaries = await _Services.Forums.Views.SummariesAsync(contexts, actor, true, token).ConfigureAwait(false);
            long start = request.HasPosition && page.TotalRecords != null && page.RecordsRemaining != null
                ? page.TotalRecords.Value - page.RecordsRemaining.Value - page.Objects.Count
                : request.Skip;
            DateTime now = _Clock();
            List<TrendingItemView> items = new List<TrendingItemView>();
            for (int i = 0; i < contexts.Count; i++)
            {
                ForumThread t = contexts[i].Thread;
                double age = t.PublishedUtc == null ? 0 : Math.Max(0, (now - t.PublishedUtc.Value).TotalHours);
                items.Add(new TrendingItemView
                {
                    Thread = summaries[i],
                    Breakdown = new TrendingBreakdown
                    {
                        Reactions = t.ReactionTotal,
                        Comments = t.CommentCount,
                        UniqueCommenters = t.UniqueCommenterCount,
                        AgeHours = Math.Round(age, 2),
                        Numerator = Numerator(t.ReactionTotal, t.CommentCount, t.UniqueCommenterCount),
                        Denominator = Math.Round(Denominator(age), 4),
                        Score = Score(t.ReactionTotal, t.CommentCount, t.UniqueCommenterCount, age)
                    },
                    Rank = (int)Math.Max(1, start + i + 1),
                    ComputedUtc = t.TrendingComputedUtc,
                    Promotion = t.PromotedFeedPostId != null && t.PromotedUtc != null ? new TrendingPromotionView { FeedPostId = t.PromotedFeedPostId, PromotedUtc = t.PromotedUtc.Value } : null,
                    Labels = summaries[i].Labels,
                    Tags = summaries[i].Tags
                });
            }
            return page.With(items);
        }

        #endregion
    }
}
