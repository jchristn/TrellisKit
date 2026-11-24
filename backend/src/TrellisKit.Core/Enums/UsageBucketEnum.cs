namespace TrellisKit.Core.Enums
{
    /// <summary>
    /// Bucket size of a usage series.
    /// </summary>
    public enum UsageBucketEnum
    {
        /// <summary>
        /// One UTC hour.
        /// </summary>
        Hour,

        /// <summary>
        /// One local day.
        /// </summary>
        Day,

        /// <summary>
        /// One local week starting on the property week start day.
        /// </summary>
        Week,

        /// <summary>
        /// One local calendar month.
        /// </summary>
        Month
    }
}
