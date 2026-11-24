namespace TrellisKit.Core.Responses
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// One time bucket of request history activity.
    /// </summary>
    public class RequestHistoryBucket
    {
        /// <summary>Aggregate observations have empty inline labels.</summary>
        public List<string> Labels { get; set; } = new List<string>();
        /// <summary>Aggregate observations have empty inline tags.</summary>
        public Dictionary<string,string> Tags { get; set; } = new Dictionary<string,string>();
        #region Public-Members

        /// <summary>
        /// Inclusive bucket start (UTC).
        /// </summary>
        public DateTime BucketStartUtc { get; set; }

        /// <summary>
        /// Exclusive bucket end (UTC).
        /// </summary>
        public DateTime BucketEndUtc { get; set; }

        /// <summary>
        /// Requests with a status code below 400.
        /// </summary>
        public long SuccessCount { get; set; } = 0;

        /// <summary>
        /// Requests with a status code of 400 or above.
        /// </summary>
        public long FailureCount { get; set; } = 0;

        /// <summary>
        /// Average duration in milliseconds, or 0 for an empty bucket.
        /// </summary>
        public double AverageDurationMs { get; set; } = 0;

        #endregion
    }
}
