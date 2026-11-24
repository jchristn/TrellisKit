namespace TrellisKit.Core.Enums
{
    /// <summary>
    /// Kind of a usage meter (docs/USAGE_METERING_CONTRACT.md section 1).
    /// </summary>
    public enum UsageMeterKindEnum
    {
        /// <summary>
        /// Things that happened in a bucket; summed over a period.
        /// </summary>
        Count,

        /// <summary>
        /// Distinct members in a bucket; not summable.
        /// </summary>
        Distinct,

        /// <summary>
        /// A level as of an instant; the value of a period is the value at its end.
        /// </summary>
        Gauge
    }
}
