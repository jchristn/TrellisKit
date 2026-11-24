namespace TrellisKit.Core.Enums
{
    /// <summary>
    /// What a task role does when an executor is busy, unhealthy, or failing.
    /// </summary>
    public enum AiAvailabilityPolicyEnum
    {
        /// <summary>
        /// Try the next executor in the hierarchy.
        /// </summary>
        FallThrough,

        /// <summary>
        /// Retry the same executor briefly before moving on.
        /// </summary>
        Wait
    }
}
