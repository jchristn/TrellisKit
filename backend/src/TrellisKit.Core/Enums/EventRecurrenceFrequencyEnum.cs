namespace TrellisKit.Core.Enums
{
    /// <summary>
    /// How often a recurring event repeats (resolved decision 11).
    /// </summary>
    public enum EventRecurrenceFrequencyEnum
    {
        /// <summary>
        /// Every interval days.
        /// </summary>
        Daily,

        /// <summary>
        /// Every interval weeks on the listed weekdays.
        /// </summary>
        Weekly,

        /// <summary>
        /// Every interval months on a day of the month or an ordinal weekday.
        /// </summary>
        Monthly,

        /// <summary>
        /// Every interval years on the date of the first occurrence.
        /// </summary>
        Yearly
    }
}
