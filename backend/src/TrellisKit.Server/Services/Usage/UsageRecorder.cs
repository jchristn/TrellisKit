namespace TrellisKit.Server.Services.Usage
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core.Database;
    using TrellisKit.Core.Models;

    /// <summary>
    /// In-memory counters of the recorded usage meters of one property on this server instance
    /// (docs/USAGE_METERING_CONTRACT.md section 4.4). Counting is lock-free; a flush swaps the counters out atomically,
    /// adds them to the hourly rows in one statement, and puts them back when the write fails, so counts are never
    /// added twice. Thread safety: safe to call concurrently.
    /// </summary>
    public sealed class UsageRecorder
    {
        #region Public-Members

        /// <summary>
        /// Whether requests are counted. Default true.
        /// </summary>
        public bool Enabled { get; set; } = true;

        /// <summary>
        /// Counter cells currently held (meter and hour pairs).
        /// </summary>
        public int Pending
        {
            get { return _Counts.Count; }
        }

        #endregion

        #region Private-Members

        private ConcurrentDictionary<string, long> _Counts = new ConcurrentDictionary<string, long>(StringComparer.Ordinal);
        private readonly SemaphoreSlim _FlushLock = new SemaphoreSlim(1, 1);

        #endregion

        #region Public-Methods

        /// <summary>
        /// Count one API request (api.requests, and api.errors for status 500 or above).
        /// </summary>
        /// <param name="statusCode">Response status.</param>
        /// <param name="atUtc">Request time.</param>
        public void RecordRequest(int statusCode, DateTime atUtc)
        {
            if (!Enabled) return;
            Add("api.requests", atUtc, 1);
            if (statusCode >= 500) Add("api.errors", atUtc, 1);
        }

        /// <summary>
        /// Add to a recorded meter.
        /// </summary>
        /// <param name="meter">Recorded meter id.</param>
        /// <param name="atUtc">Time.</param>
        /// <param name="amount">Amount (ignored when not positive).</param>
        /// <exception cref="ArgumentNullException">The meter is null or empty.</exception>
        public void Add(string meter, DateTime atUtc, long amount)
        {
            if (String.IsNullOrEmpty(meter)) throw new ArgumentNullException(nameof(meter));
            if (amount <= 0) return;
            DateTime hour = UsagePeriodCalculator.FloorHour(atUtc.Kind == DateTimeKind.Utc ? atUtc : atUtc.ToUniversalTime());
            string key = meter + "|" + hour.Ticks.ToString(CultureInfo.InvariantCulture);
            _Counts.AddOrUpdate(key, amount, (k, v) => v + amount);
        }

        /// <summary>
        /// Add every pending count to the hourly rows of the property.
        /// </summary>
        /// <param name="database">Property-bound driver.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Cells written.</returns>
        /// <exception cref="ArgumentNullException">The driver is null.</exception>
        public async Task<int> FlushAsync(DatabaseDriverBase database, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(database);
            await _FlushLock.WaitAsync(token).ConfigureAwait(false);
            try
            {
                ConcurrentDictionary<string, long> taken = Interlocked.Exchange(ref _Counts, new ConcurrentDictionary<string, long>(StringComparer.Ordinal));
                if (taken.IsEmpty) return 0;
                List<KeyValuePair<string, long>> batch = taken.Take(5000).ToList();
                foreach (KeyValuePair<string, long> pair in taken.Skip(5000)) _Counts.AddOrUpdate(pair.Key, pair.Value, (k, v) => v + pair.Value);
                List<UsageHourlyValue> values = batch.Select(pair =>
                {
                    int bar = pair.Key.IndexOf('|');
                    long ticks = Int64.Parse(pair.Key.Substring(bar + 1), CultureInfo.InvariantCulture);
                    return new UsageHourlyValue { Meter = pair.Key.Substring(0, bar), PeriodStartUtc = new DateTime(ticks, DateTimeKind.Utc), Value = pair.Value };
                }).ToList();
                try
                {
                    // One statement: it either adds every cell or none, so a retry after a failure never double counts.
                    return await database.Usage.AddHourlyAsync(values, CancellationToken.None).ConfigureAwait(false);
                }
                catch
                {
                    foreach (KeyValuePair<string, long> pair in batch) _Counts.AddOrUpdate(pair.Key, pair.Value, (k, v) => v + pair.Value);
                    throw;
                }
            }
            finally
            {
                _FlushLock.Release();
            }
        }

        #endregion
    }
}
