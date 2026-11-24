namespace TrellisKit.Core.Responses
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// One meter of a usage summary: the period total, the previous equivalent period, the change, and one value per bucket.
    /// </summary>
    public class UsageMeterSeries
    {
        #region Public-Members

        /// <summary>
        /// Meter id.
        /// </summary>
        public string Meter { get; set; } = String.Empty;

        /// <summary>
        /// Meter group.
        /// </summary>
        public string Group { get; set; } = String.Empty;

        /// <summary>
        /// Kind.
        /// </summary>
        public UsageMeterKindEnum Kind { get; set; } = UsageMeterKindEnum.Count;

        /// <summary>
        /// Unit: count, bytes, tokens, or usdMicros.
        /// </summary>
        public string Unit { get; set; } = "count";

        /// <summary>
        /// Period total (section 3.4).
        /// </summary>
        public long Total { get; set; } = 0;

        /// <summary>
        /// Total of the previous equivalent period.
        /// </summary>
        public long PreviousTotal { get; set; } = 0;

        /// <summary>
        /// (Total - PreviousTotal) / PreviousTotal, or null when PreviousTotal is 0.
        /// </summary>
        public double? Change { get; set; } = null;

        /// <summary>
        /// False when the total is a lower bound (distinct members beyond the activity retention).
        /// </summary>
        public bool Exact { get; set; } = true;

        /// <summary>
        /// One value per bucket (null where the meter has no value at that bucket size). Never null.
        /// </summary>
        public List<long?> Values { get; set; } = new List<long?>();

        #endregion
    }
}
