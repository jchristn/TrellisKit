namespace TrellisKit.Core.Enums
{
    /// <summary>
    /// How a job definition is scheduled (docs/INTEGRATION.md).
    /// </summary>
    public enum JobIntervalTypeEnum
    {
        /// <summary>
        /// Every IntervalValue minutes (5 to 1440).
        /// </summary>
        EveryNMinutes,

        /// <summary>
        /// Every IntervalValue hours (1 to 168).
        /// </summary>
        EveryNHours,

        /// <summary>
        /// Every IntervalValue days (1 to 90) at TimeOfDayUtc.
        /// </summary>
        EveryNDays,

        /// <summary>
        /// On the DaysOfWeek at TimeOfDayUtc.
        /// </summary>
        DaysOfWeek,

        /// <summary>
        /// Only when an administrator runs it.
        /// </summary>
        Manual
    }
}
