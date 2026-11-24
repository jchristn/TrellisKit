namespace TrellisKit.Core.Responses
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// GET /api/v1/admin/usage/summary (docs/USAGE_METERING_CONTRACT.md section 7.1). Fixed bound: at most 400 buckets and the meter catalog.
    /// </summary>
    public class UsageSummary
    {
        #region Public-Members

        /// <summary>
        /// Property id.
        /// </summary>
        public string PropertyId { get; set; } = String.Empty;

        /// <summary>
        /// Period.
        /// </summary>
        public UsagePeriodEnum Period { get; set; } = UsagePeriodEnum.LastMonth;

        /// <summary>
        /// Property time zone (IANA id).
        /// </summary>
        public string TimeZone { get; set; } = "UTC";

        /// <summary>
        /// First day of the week.
        /// </summary>
        public string WeekStartsOn { get; set; } = "Monday";

        /// <summary>
        /// Period start (inclusive).
        /// </summary>
        public DateTime FromUtc { get; set; } = DateTime.MinValue;

        /// <summary>
        /// Period end (exclusive).
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
        public List<UsageBucketView> Buckets { get; set; } = new List<UsageBucketView>();

        /// <summary>
        /// Meters (at most the catalog). Never null.
        /// </summary>
        public List<UsageMeterSeries> Meters { get; set; } = new List<UsageMeterSeries>();

        /// <summary>
        /// When the last rollup pass completed, or null before the first.
        /// </summary>
        public DateTime? DataThroughUtc { get; set; } = null;

        /// <summary>
        /// Whether the history backfill has finished.
        /// </summary>
        public bool BackfillComplete { get; set; } = false;

        /// <summary>
        /// Latest storage reconciliation.
        /// </summary>
        public UsageReconciliationView StorageReconciliation { get; set; } = new UsageReconciliationView();

        /// <summary>
        /// When the summary was computed.
        /// </summary>
        public DateTime GeneratedUtc { get; set; } = DateTime.UtcNow;

        #endregion
    }
}
