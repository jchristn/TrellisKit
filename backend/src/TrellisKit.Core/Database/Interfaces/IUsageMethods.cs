namespace TrellisKit.Core.Database.Interfaces
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core.Enumeration;
    using TrellisKit.Core.Models;
    using TrellisKit.Core.Responses;

    /// <summary>
    /// Usage metering storage and computation (usagehourly, usagedaily, usageactivity, usagemediasizes, usagestate;
    /// docs/USAGE_METERING_CONTRACT.md). Implementations are bound to one property and safe to call concurrently.
    /// </summary>
    public interface IUsageMethods
    {
        /// <summary>
        /// Read the rollup state.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>State, or null before the first pass.</returns>
        Task<UsageState?> ReadStateAsync(CancellationToken token = default);

        /// <summary>
        /// Insert or replace the rollup state.
        /// </summary>
        /// <param name="state">State.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        /// <exception cref="ArgumentNullException">The state is null.</exception>
        Task WriteStateAsync(UsageState state, CancellationToken token = default);

        /// <summary>
        /// The oldest creation time of any member, post, thread, comment, or media row.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Time, or null when the property has no such rows.</returns>
        Task<DateTime?> EarliestSourceUtcAsync(CancellationToken token = default);

        /// <summary>
        /// Compute a derived or measured meter from the source tables for each bucket (replace semantics: the result is
        /// the full value of each bucket). Gauges are evaluated at each bucket's end. Recorded meters are summed from
        /// their hourly rows.
        /// </summary>
        /// <param name="meter">Meter id.</param>
        /// <param name="buckets">Buckets (at most 1000).</param>
        /// <param name="hourly">Whether the buckets are UTC hours (AI rollups then read hourly rows only).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Value per bucket key (every key present).</returns>
        /// <exception cref="ArgumentException">Unknown meter or too many buckets.</exception>
        Task<Dictionary<string, long>> ComputeAsync(string meter, IReadOnlyList<UsageBucketRange> buckets, bool hourly, CancellationToken token = default);

        /// <summary>
        /// Replace hourly values.
        /// </summary>
        /// <param name="values">Values (at most 5000).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Rows written.</returns>
        Task<int> WriteHourlyAsync(IReadOnlyList<UsageHourlyValue> values, CancellationToken token = default);

        /// <summary>
        /// Add to hourly values (recorded meters only), in one statement.
        /// </summary>
        /// <param name="values">Increments (at most 5000).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Rows written.</returns>
        Task<int> AddHourlyAsync(IReadOnlyList<UsageHourlyValue> values, CancellationToken token = default);

        /// <summary>
        /// Replace daily values; rows already closed are never changed.
        /// </summary>
        /// <param name="values">Values (at most 5000).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Rows written.</returns>
        Task<int> WriteDailyAsync(IReadOnlyList<UsageDailyValue> values, CancellationToken token = default);

        /// <summary>
        /// The oldest local day that still has an open daily row.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Day (yyyy-MM-dd), or null.</returns>
        Task<string?> OldestOpenDayAsync(CancellationToken token = default);

        /// <summary>
        /// The newest local day that has a daily row.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Day (yyyy-MM-dd), or null.</returns>
        Task<string?> LatestDayAsync(CancellationToken token = default);

        /// <summary>
        /// Delete every open daily row (time zone change).
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Rows deleted.</returns>
        Task<int> DeleteOpenDailyAsync(CancellationToken token = default);

        /// <summary>
        /// Aggregate daily rows per bucket and meter: rows whose period start is in [StartUtc, EndUtc).
        /// </summary>
        /// <param name="buckets">Buckets (at most 1000).</param>
        /// <param name="meters">Meter ids.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Aggregates (only buckets and meters with rows).</returns>
        Task<List<UsageAggregate>> AggregateDailyAsync(IReadOnlyList<UsageBucketRange> buckets, IReadOnlyList<string> meters, CancellationToken token = default);

        /// <summary>
        /// Aggregate hourly rows per bucket and meter: rows whose hour start is in [StartUtc, EndUtc).
        /// </summary>
        /// <param name="buckets">Buckets (at most 1000).</param>
        /// <param name="meters">Meter ids.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Aggregates (only buckets and meters with rows).</returns>
        Task<List<UsageAggregate>> AggregateHourlyAsync(IReadOnlyList<UsageBucketRange> buckets, IReadOnlyList<string> meters, CancellationToken token = default);

        /// <summary>
        /// Enumerate daily usage records (UsageCollections.Records).
        /// </summary>
        /// <param name="request">Prepared request.</param>
        /// <param name="filter">Filters.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Page.</returns>
        Task<EnumerationResult<UsageRecord>> EnumerateAsync(EnumerationRequest request, UsageRecordFilter filter, CancellationToken token = default);

        /// <summary>
        /// Read daily records of a day range, ordered by day then meter.
        /// </summary>
        /// <param name="fromDay">First day (inclusive).</param>
        /// <param name="toDay">Last day (inclusive).</param>
        /// <param name="maxRows">Most rows returned (at most 100000).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Records.</returns>
        Task<List<UsageRecord>> ReadDailyAsync(string fromDay, string toDay, int maxRows, CancellationToken token = default);

        /// <summary>
        /// Read hourly values of a range, ordered by hour then meter.
        /// </summary>
        /// <param name="fromUtc">Start (inclusive).</param>
        /// <param name="toUtc">End (exclusive).</param>
        /// <param name="maxRows">Most rows returned (at most 100000).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Values.</returns>
        Task<List<UsageHourlyValue>> ReadHourlyAsync(DateTime fromUtc, DateTime toUtc, int maxRows, CancellationToken token = default);

        /// <summary>
        /// Record that a member was active in the UTC hour containing a time (idempotent).
        /// </summary>
        /// <param name="userId">Member id.</param>
        /// <param name="activeUtc">Activity time.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        Task RecordActivityAsync(string userId, DateTime activeUtc, CancellationToken token = default);

        /// <summary>
        /// Ready, live videos whose rendition size is unmeasured (newest first) or measured before a time (oldest
        /// measurement first).
        /// </summary>
        /// <param name="staleBeforeUtc">Measurements older than this are refreshed.</param>
        /// <param name="maxResults">Most items (at most 1000).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Media items.</returns>
        Task<List<UsageMediaItem>> VideosToMeasureAsync(DateTime staleBeforeUtc, int maxResults, CancellationToken token = default);

        /// <summary>
        /// Store a video's measured rendition size.
        /// </summary>
        /// <param name="mediaId">Media id.</param>
        /// <param name="sizeBytes">Total bytes.</param>
        /// <param name="objectCount">Objects measured.</param>
        /// <param name="measuredUtc">Measurement time.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        Task WriteMediaSizeAsync(string mediaId, long sizeBytes, long objectCount, DateTime measuredUtc, CancellationToken token = default);

        /// <summary>
        /// Live, completed media originals with an id after a cursor, by id.
        /// </summary>
        /// <param name="afterId">Cursor, or null from the start.</param>
        /// <param name="maxResults">Most items (at most 1000).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Media items.</returns>
        Task<List<UsageMediaItem>> OriginalsAfterAsync(string? afterId, int maxResults, CancellationToken token = default);

        /// <summary>
        /// Delete expired usage rows in bounded batches: hourly rows, activity hours, measurements of media that no
        /// longer exist, and daily rows when a daily retention is set.
        /// </summary>
        /// <param name="hourlyBeforeUtc">Hourly rows starting before this are deleted.</param>
        /// <param name="activityBeforeUtc">Activity hours before this are deleted.</param>
        /// <param name="dailyBeforeUtc">Daily rows starting before this are deleted, or null to keep them.</param>
        /// <param name="maxRows">Most rows per table (at most 5000).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Rows deleted.</returns>
        Task<int> PruneAsync(DateTime hourlyBeforeUtc, DateTime activityBeforeUtc, DateTime? dailyBeforeUtc, int maxRows, CancellationToken token = default);
    }
}
