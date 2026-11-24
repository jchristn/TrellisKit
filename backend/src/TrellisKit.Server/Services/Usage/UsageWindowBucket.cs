namespace TrellisKit.Server.Services.Usage
{
    using System;

    /// <summary>
    /// One bucket of a usage window: its displayed bounds (clipped to the period) and the aligned bounds its rows are
    /// selected by.
    /// </summary>
    public sealed class UsageWindowBucket
    {
        #region Public-Members

        /// <summary>
        /// Unique key (the aligned start in ISO 8601 UTC).
        /// </summary>
        public string Key { get; set; } = String.Empty;

        /// <summary>
        /// Local label.
        /// </summary>
        public string Label { get; set; } = String.Empty;

        /// <summary>
        /// Displayed start (clipped).
        /// </summary>
        public DateTime StartUtc { get; set; } = DateTime.MinValue;

        /// <summary>
        /// Displayed end (clipped, exclusive).
        /// </summary>
        public DateTime EndUtc { get; set; } = DateTime.MinValue;

        /// <summary>
        /// Aligned start (hour, local midnight, week start, or first of month).
        /// </summary>
        public DateTime SelectStartUtc { get; set; } = DateTime.MinValue;

        /// <summary>
        /// Aligned end (exclusive).
        /// </summary>
        public DateTime SelectEndUtc { get; set; } = DateTime.MinValue;

        #endregion
    }
}
