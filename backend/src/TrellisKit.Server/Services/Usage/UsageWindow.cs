namespace TrellisKit.Server.Services.Usage
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// A resolved usage period: its bounds, the previous equivalent period, and its buckets (section 3 of
    /// docs/USAGE_METERING_CONTRACT.md).
    /// </summary>
    public sealed class UsageWindow
    {
        #region Public-Members

        /// <summary>
        /// Period.
        /// </summary>
        public UsagePeriodEnum Period { get; set; } = UsagePeriodEnum.LastMonth;

        /// <summary>
        /// Start (inclusive).
        /// </summary>
        public DateTime FromUtc { get; set; } = DateTime.MinValue;

        /// <summary>
        /// End (exclusive).
        /// </summary>
        public DateTime ToUtc { get; set; } = DateTime.MinValue;

        /// <summary>
        /// Previous equivalent period start.
        /// </summary>
        public DateTime PreviousFromUtc { get; set; } = DateTime.MinValue;

        /// <summary>
        /// Previous equivalent period end.
        /// </summary>
        public DateTime PreviousToUtc { get; set; } = DateTime.MinValue;

        /// <summary>
        /// Bucket size.
        /// </summary>
        public UsageBucketEnum Bucket { get; set; } = UsageBucketEnum.Day;

        /// <summary>
        /// Buckets (at most 400). Never null.
        /// </summary>
        public List<UsageWindowBucket> Buckets
        {
            get { return _Buckets; }
            set { _Buckets = value ?? new List<UsageWindowBucket>(); }
        }

        #endregion

        #region Private-Members

        private List<UsageWindowBucket> _Buckets = new List<UsageWindowBucket>();

        #endregion
    }
}
