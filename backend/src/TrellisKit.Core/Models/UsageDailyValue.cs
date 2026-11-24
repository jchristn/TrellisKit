namespace TrellisKit.Core.Models
{
    using System;

    /// <summary>
    /// A daily usage value to write (one meter, one local day).
    /// </summary>
    public sealed class UsageDailyValue
    {
        #region Public-Members

        /// <summary>
        /// Meter id.
        /// </summary>
        public string Meter { get; set; } = String.Empty;

        /// <summary>
        /// Local day (yyyy-MM-dd).
        /// </summary>
        public string Day { get; set; } = String.Empty;

        /// <summary>
        /// Time zone the day was computed in.
        /// </summary>
        public string TimeZone { get; set; } = "UTC";

        /// <summary>
        /// Local midnight starting the day (UTC).
        /// </summary>
        public DateTime PeriodStartUtc { get; set; } = DateTime.MinValue;

        /// <summary>
        /// Local midnight ending the day (UTC).
        /// </summary>
        public DateTime PeriodEndUtc { get; set; } = DateTime.MinValue;

        /// <summary>
        /// Value.
        /// </summary>
        public long Value { get; set; } = 0;

        /// <summary>
        /// Whether the row closes with this write.
        /// </summary>
        public bool Closed { get; set; } = false;

        #endregion
    }
}
