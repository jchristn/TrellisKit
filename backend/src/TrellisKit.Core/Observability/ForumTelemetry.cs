namespace TrellisKit.Core.Observability
{
    using System;
    using System.Diagnostics.Metrics;
    using System.Threading;

    /// <summary>
    /// Phase 3 instruments on the TrellisKit meter (docs/PHASE3_CONTRACT.md section 16). Labels are low-cardinality only
    /// (never ids, slugs, or text). Thread safety: instruments are thread-safe; gauges read atomically updated fields.
    /// </summary>
    public static class ForumTelemetry
    {
        #region Public-Members

        /// <summary>
        /// Thread writes, labeled action (create, update, delete, publish, pin, unpin, lock, unlock, move), type, outcome.
        /// </summary>
        public static readonly Counter<long> Threads = TrellisKitTelemetry.Meter.CreateCounter<long>("trelliskit.forums.threads", "{thread}", "Forum thread writes.");

        /// <summary>
        /// Poll votes, labeled action (vote, change, remove) and outcome.
        /// </summary>
        public static readonly Counter<long> PollVotes = TrellisKitTelemetry.Meter.CreateCounter<long>("trelliskit.forums.polls.votes", "{vote}", "Poll vote writes.");

        /// <summary>
        /// Polls closed, labeled reason (scheduled, manual).
        /// </summary>
        public static readonly Counter<long> PollsClosed = TrellisKitTelemetry.Meter.CreateCounter<long>("trelliskit.forums.polls.closed", "{poll}", "Polls closed.");

        /// <summary>
        /// Accepted answer changes, labeled action (accept, clear).
        /// </summary>
        public static readonly Counter<long> Answers = TrellisKitTelemetry.Meter.CreateCounter<long>("trelliskit.forums.answers", "{answer}", "Accepted answer changes.");

        /// <summary>
        /// Feedback status transitions, labeled status (the target status).
        /// </summary>
        public static readonly Counter<long> Feedback = TrellisKitTelemetry.Meter.CreateCounter<long>("trelliskit.forums.feedback", "{transition}", "Feedback review transitions.");

        /// <summary>
        /// Promotions, labeled action (promote, unpromote, discuss, undiscuss).
        /// </summary>
        public static readonly Counter<long> Promotions = TrellisKitTelemetry.Meter.CreateCounter<long>("trelliskit.forums.promotions", "{promotion}", "Forum to feed promotions.");

        /// <summary>
        /// Follows, labeled target_type (forum, thread) and action (follow, unfollow, auto).
        /// </summary>
        public static readonly Counter<long> Follows = TrellisKitTelemetry.Meter.CreateCounter<long>("trelliskit.forums.follows", "{follow}", "Forum and thread follows.");

        /// <summary>
        /// Searches, labeled outcome and include_comments (true, false).
        /// </summary>
        public static readonly Counter<long> Searches = TrellisKitTelemetry.Meter.CreateCounter<long>("trelliskit.forums.search", "{search}", "Thread and global searches.");

        /// <summary>
        /// Search duration in seconds, labeled provider (verbex).
        /// </summary>
        public static readonly Histogram<double> SearchDuration = TrellisKitTelemetry.Meter.CreateHistogram<double>("trelliskit.forums.search.duration", "s", "Search duration.");

        /// <summary>
        /// Trending recompute duration in seconds.
        /// </summary>
        public static readonly Histogram<double> TrendingDuration = TrellisKitTelemetry.Meter.CreateHistogram<double>("trelliskit.trending.recompute.duration", "s", "Trending recompute duration.");

        /// <summary>
        /// Threads with a non-zero trending score after the last recompute.
        /// </summary>
        public static readonly ObservableGauge<long> ThreadsScored = TrellisKitTelemetry.Meter.CreateObservableGauge<long>("trelliskit.trending.threads_scored", () => Interlocked.Read(ref _ThreadsScored), "{thread}", "Threads with a trending score.");

        /// <summary>
        /// Open or InReview Feedback threads at the last count.
        /// </summary>
        public static readonly ObservableGauge<long> ReviewRequestsOpen = TrellisKitTelemetry.Meter.CreateObservableGauge<long>("trelliskit.forums.review_requests.open", () => Interlocked.Read(ref _ReviewRequestsOpen), "{thread}", "Open review requests.");

        /// <summary>
        /// Forum mutes, labeled action (mute, unmute, expire).
        /// </summary>
        public static readonly Counter<long> Mutes = TrellisKitTelemetry.Meter.CreateCounter<long>("trelliskit.forums.mutes", "{mute}", "Forum mutes.");

        #endregion

        #region Private-Members

        private static long _ThreadsScored = 0;
        private static long _ReviewRequestsOpen = 0;

        #endregion

        #region Public-Methods

        /// <summary>
        /// Add one to a counter with label pairs (name, value, name, value, ...).
        /// </summary>
        /// <param name="counter">Counter.</param>
        /// <param name="labels">Label names and values.</param>
        public static void Count(Counter<long> counter, params string[] labels)
        {
            TrellisKitTelemetry.Add(counter, 1, labels);
        }

        /// <summary>
        /// Record the scored thread count.
        /// </summary>
        /// <param name="count">Threads with a score.</param>
        public static void SetThreadsScored(long count)
        {
            Interlocked.Exchange(ref _ThreadsScored, Math.Max(0, count));
        }

        /// <summary>
        /// Record the open review request count.
        /// </summary>
        /// <param name="count">Open review requests.</param>
        public static void SetOpenReviewRequests(long count)
        {
            Interlocked.Exchange(ref _ReviewRequestsOpen, Math.Max(0, count));
        }

        #endregion
    }
}
