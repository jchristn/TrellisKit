namespace TrellisKit.Core.Responses
{
    using System.Collections.Generic;

    /// <summary>
    /// Time-bucketed request history summary for charts. Every bucket in the range is present, including empty ones.
    /// </summary>
    public class RequestHistorySummary
    {
        #region Public-Members

        /// <summary>
        /// Total requests in range.
        /// </summary>
        public long TotalCount { get; set; } = 0;

        /// <summary>
        /// Requests with a status code below 400.
        /// </summary>
        public long TotalSuccess { get; set; } = 0;

        /// <summary>
        /// Requests with a status code of 400 or above.
        /// </summary>
        public long TotalFailure { get; set; } = 0;

        /// <summary>
        /// Average duration in milliseconds across the range, or 0 when empty.
        /// </summary>
        public double AverageDurationMs { get; set; } = 0;

        /// <summary>
        /// Buckets in chronological order. Never null.
        /// </summary>
        public List<RequestHistoryBucket> Buckets
        {
            get { return _Buckets; }
            set { _Buckets = value ?? new List<RequestHistoryBucket>(); }
        }

        #endregion

        #region Private-Members

        private List<RequestHistoryBucket> _Buckets = new List<RequestHistoryBucket>();

        #endregion
    }
}
