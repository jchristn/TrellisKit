namespace TrellisKit.Core.Enums
{
    /// <summary>
    /// A usage period (docs/USAGE_METERING_CONTRACT.md section 3.2).
    /// </summary>
    public enum UsagePeriodEnum
    {
        /// <summary>
        /// The trailing 24 hours (hour aligned).
        /// </summary>
        LastDay,

        /// <summary>
        /// The trailing 7 local days including today.
        /// </summary>
        LastWeek,

        /// <summary>
        /// The trailing 30 local days including today.
        /// </summary>
        LastMonth,

        /// <summary>
        /// The trailing 90 local days including today.
        /// </summary>
        Last90Days,

        /// <summary>
        /// The whole previous calendar quarter (ends at the current quarter's first local midnight, not at now).
        /// </summary>
        PreviousQuarter,

        /// <summary>
        /// The trailing 365 local days including today.
        /// </summary>
        LastYear,

        /// <summary>
        /// The current local week to now.
        /// </summary>
        ThisWeek,

        /// <summary>
        /// The current local month to now.
        /// </summary>
        ThisMonth,

        /// <summary>
        /// The current calendar quarter to now.
        /// </summary>
        ThisQuarter,

        /// <summary>
        /// The current local year to now.
        /// </summary>
        ThisYear,

        /// <summary>
        /// An explicit range (from and to).
        /// </summary>
        Range
    }
}
