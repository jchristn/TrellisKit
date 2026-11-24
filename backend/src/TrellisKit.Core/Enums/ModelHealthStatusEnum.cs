namespace TrellisKit.Core.Enums
{
    /// <summary>
    /// Health status of a model endpoint.
    /// </summary>
    public enum ModelHealthStatusEnum
    {
        /// <summary>
        /// Not checked yet, or health checks are off.
        /// </summary>
        Unknown,

        /// <summary>
        /// Passing health checks.
        /// </summary>
        Healthy,

        /// <summary>
        /// Failing health checks.
        /// </summary>
        Unhealthy
    }
}
