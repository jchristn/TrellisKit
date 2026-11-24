namespace TrellisKit.Core.Responses
{
    using System;

    /// <summary>
    /// One bucket of a usage series (docs/USAGE_METERING_CONTRACT.md section 7.1).
    /// </summary>
    public class UsageBucketView
    {
        #region Public-Members

        /// <summary>
        /// Start (UTC, clipped to the period).
        /// </summary>
        public DateTime StartUtc { get; set; } = DateTime.MinValue;

        /// <summary>
        /// End (UTC, exclusive, clipped to the period).
        /// </summary>
        public DateTime EndUtc { get; set; } = DateTime.MinValue;

        /// <summary>
        /// Local label: yyyy-MM-ddTHH:00 (hour), yyyy-MM-dd (day or week start), or yyyy-MM (month).
        /// </summary>
        public string Label { get; set; } = String.Empty;

        #endregion
    }
}
