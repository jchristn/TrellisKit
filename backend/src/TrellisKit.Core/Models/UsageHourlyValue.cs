namespace TrellisKit.Core.Models
{
    using System;

    /// <summary>
    /// An hourly usage value (one meter, one UTC hour).
    /// </summary>
    public sealed class UsageHourlyValue
    {
        #region Public-Members

        /// <summary>
        /// Meter id.
        /// </summary>
        public string Meter { get; set; } = String.Empty;

        /// <summary>
        /// Start of the UTC hour.
        /// </summary>
        public DateTime PeriodStartUtc { get; set; } = DateTime.MinValue;

        /// <summary>
        /// Value.
        /// </summary>
        public long Value { get; set; } = 0;

        #endregion
    }
}
