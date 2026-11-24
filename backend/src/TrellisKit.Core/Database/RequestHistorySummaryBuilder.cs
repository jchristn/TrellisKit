namespace TrellisKit.Core.Database
{
    using System;
    using System.Collections.Generic;
    using System.Data;
    using TrellisKit.Core.Requests;
    using TrellisKit.Core.Responses;

    /// <summary>
    /// Builds a gap-filled <see cref="RequestHistorySummary"/> from per-bucket aggregate rows computed in SQL.
    /// Expected columns: bucket (bucket index from FromUtc), successcount, failurecount, durationsum.
    /// </summary>
    public static class RequestHistorySummaryBuilder
    {
        #region Public-Members

        /// <summary>
        /// Maximum buckets a single summary may return. Default 2000. Minimum 10, maximum 100000.
        /// </summary>
        public static int MaxBuckets
        {
            get { return _MaxBuckets; }
            set { _MaxBuckets = Math.Clamp(value, 10, 100000); }
        }

        #endregion

        #region Private-Members

        private static int _MaxBuckets = 2000;

        #endregion

        #region Public-Methods

        /// <summary>
        /// Validate a summary filter and return its normalized UTC range.
        /// </summary>
        /// <param name="filter">Filter.</param>
        /// <param name="fromUtc">Normalized start.</param>
        /// <param name="toUtc">Normalized end.</param>
        /// <exception cref="ArgumentNullException">The filter is null.</exception>
        /// <exception cref="ArgumentException">The range is missing, inverted, or would exceed <see cref="MaxBuckets"/>.</exception>
        public static void ValidateRange(RequestHistoryFilter filter, out DateTime fromUtc, out DateTime toUtc)
        {
            ArgumentNullException.ThrowIfNull(filter);
            if (!filter.FromUtc.HasValue || !filter.ToUtc.HasValue)
                throw new ArgumentException("Summary requires both fromUtc and toUtc.", nameof(filter));

            fromUtc = filter.FromUtc.Value.ToUniversalTime();
            toUtc = filter.ToUtc.Value.ToUniversalTime();
            if (toUtc <= fromUtc)
                throw new ArgumentException("toUtc must be later than fromUtc.", nameof(filter));

            double buckets = Math.Ceiling((toUtc - fromUtc).TotalMinutes / filter.BucketMinutes);
            if (buckets > _MaxBuckets)
                throw new ArgumentException(
                    "The range would produce " + buckets + " buckets; the maximum is " + _MaxBuckets + ". Increase bucketMinutes or narrow the range.",
                    nameof(filter));
        }

        /// <summary>
        /// Build the summary.
        /// </summary>
        /// <param name="fromUtc">Range start (UTC).</param>
        /// <param name="toUtc">Range end (UTC).</param>
        /// <param name="bucketMinutes">Bucket width in minutes.</param>
        /// <param name="aggregates">Aggregate rows.</param>
        /// <returns>Summary.</returns>
        /// <exception cref="ArgumentNullException">Aggregates are null.</exception>
        public static RequestHistorySummary Build(DateTime fromUtc, DateTime toUtc, int bucketMinutes, DataTable aggregates)
        {
            ArgumentNullException.ThrowIfNull(aggregates);

            List<RequestHistoryBucket> buckets = new List<RequestHistoryBucket>();
            List<double> durationSums = new List<double>();
            DateTime cursor = fromUtc;
            while (cursor < toUtc)
            {
                DateTime end = cursor.AddMinutes(bucketMinutes);
                if (end > toUtc) end = toUtc;
                buckets.Add(new RequestHistoryBucket { BucketStartUtc = cursor, BucketEndUtc = end });
                durationSums.Add(0);
                cursor = end;
            }

            long totalSuccess = 0;
            long totalFailure = 0;
            double totalDuration = 0;

            foreach (DataRow row in aggregates.Rows)
            {
                long index = RowReader.GetLong(row, "bucket");
                if (index < 0 || index >= buckets.Count) continue;
                long success = RowReader.GetLong(row, "successcount");
                long failure = RowReader.GetLong(row, "failurecount");
                double durationSum = RowReader.GetDouble(row, "durationsum");

                RequestHistoryBucket bucket = buckets[(int)index];
                bucket.SuccessCount += success;
                bucket.FailureCount += failure;
                durationSums[(int)index] += durationSum;

                totalSuccess += success;
                totalFailure += failure;
                totalDuration += durationSum;
            }

            for (int i = 0; i < buckets.Count; i++)
            {
                long count = buckets[i].SuccessCount + buckets[i].FailureCount;
                buckets[i].AverageDurationMs = count == 0 ? 0 : durationSums[i] / count;
            }

            long total = totalSuccess + totalFailure;
            return new RequestHistorySummary
            {
                TotalCount = total,
                TotalSuccess = totalSuccess,
                TotalFailure = totalFailure,
                AverageDurationMs = total == 0 ? 0 : totalDuration / total,
                Buckets = buckets
            };
        }

        #endregion
    }
}
