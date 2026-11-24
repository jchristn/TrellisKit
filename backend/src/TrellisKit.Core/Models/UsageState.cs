namespace TrellisKit.Core.Models
{
    using System;

    /// <summary>
    /// Rollup progress of one property (usagestate).
    /// </summary>
    public class UsageState
    {
        #region Public-Members

        /// <summary>
        /// Time zone the open daily rows were computed in.
        /// </summary>
        public string TimeZone { get; set; } = "UTC";

        /// <summary>
        /// Local midnight before the oldest source row (UTC), or null before the first pass.
        /// </summary>
        public DateTime? BackfillStartUtc { get; set; } = null;

        /// <summary>
        /// Local midnight of the next day to backfill (UTC), or null before the first pass.
        /// </summary>
        public DateTime? BackfillCursorUtc { get; set; } = null;

        /// <summary>
        /// Next hour to backfill (UTC), or null before the first pass.
        /// </summary>
        public DateTime? HourlyCursorUtc { get; set; } = null;

        /// <summary>
        /// When the backfill finished, or null.
        /// </summary>
        public DateTime? BackfilledUtc { get; set; } = null;

        /// <summary>
        /// When the last rollup pass completed, or null.
        /// </summary>
        public DateTime? LastRollupUtc { get; set; } = null;

        /// <summary>
        /// When recorded counters were last flushed, or null.
        /// </summary>
        public DateTime? LastFlushUtc { get; set; } = null;

        /// <summary>
        /// When the last storage reconciliation batch ran, or null.
        /// </summary>
        public DateTime? StorageScanUtc { get; set; } = null;

        /// <summary>
        /// Media id after which the next reconciliation batch starts, or null to start over.
        /// </summary>
        public string? StorageCursor { get; set; } = null;

        /// <summary>
        /// Originals checked by the last reconciliation batch.
        /// </summary>
        public long StorageMeasured { get; set; } = 0;

        /// <summary>
        /// Byte difference (stored minus recorded) found by the last reconciliation batch.
        /// </summary>
        public long StorageDriftBytes { get; set; } = 0;

        /// <summary>
        /// Originals missing from the object store in the last reconciliation batch.
        /// </summary>
        public long StorageMissing { get; set; } = 0;

        #endregion
    }
}
