namespace TrellisKit.Core.Enums
{
    /// <summary>
    /// How a usage meter combines daily or hourly values into a larger bucket or a period.
    /// </summary>
    public enum UsageAggregateEnum
    {
        /// <summary>
        /// Sum of the values.
        /// </summary>
        Sum,

        /// <summary>
        /// The latest value (gauges, and trailing distinct counts such as monthly active members).
        /// </summary>
        Last,

        /// <summary>
        /// Distinct members computed live from the activity sources.
        /// </summary>
        Distinct
    }
}
