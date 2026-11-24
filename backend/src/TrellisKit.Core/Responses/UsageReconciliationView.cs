namespace TrellisKit.Core.Responses
{
    using System;

    /// <summary>
    /// The latest storage reconciliation batch (docs/USAGE_METERING_CONTRACT.md section 6).
    /// </summary>
    public class UsageReconciliationView
    {
        #region Public-Members

        /// <summary>
        /// When the batch ran, or null.
        /// </summary>
        public DateTime? CheckedUtc { get; set; } = null;

        /// <summary>
        /// Originals checked.
        /// </summary>
        public long Checked { get; set; } = 0;

        /// <summary>
        /// Originals missing from the object store.
        /// </summary>
        public long Missing { get; set; } = 0;

        /// <summary>
        /// Stored minus recorded bytes over the checked originals.
        /// </summary>
        public long DriftBytes { get; set; } = 0;

        #endregion
    }
}
