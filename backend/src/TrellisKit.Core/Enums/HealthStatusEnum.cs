namespace TrellisKit.Core.Enums
{
    /// <summary>
    /// Server health status.
    /// </summary>
    public enum HealthStatusEnum
    {
        /// <summary>
        /// All dependencies are reachable.
        /// </summary>
        Healthy,

        /// <summary>
        /// The server is serving requests but a non-critical dependency is impaired.
        /// </summary>
        Degraded,

        /// <summary>
        /// A critical dependency, such as the database, is unreachable.
        /// </summary>
        Unhealthy,

        /// <summary>
        /// The task is shutting down: it accepts no new WebSocket upgrades and closes its sockets (docs/PHASE5_CONTRACT.md section 4.11).
        /// </summary>
        Draining
    }
}
