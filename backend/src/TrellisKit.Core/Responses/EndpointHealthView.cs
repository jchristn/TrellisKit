namespace TrellisKit.Core.Responses
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// Health state of an endpoint.
    /// </summary>
    public class EndpointHealthView
    {
        #region Public-Members

        /// <summary>
        /// Status.
        /// </summary>
        public ModelHealthStatusEnum Status { get; set; } = ModelHealthStatusEnum.Unknown;

        /// <summary>
        /// Last check.
        /// </summary>
        public DateTime? LastCheckedUtc { get; set; } = null;

        /// <summary>
        /// Last latency.
        /// </summary>
        public long? LastLatencyMs { get; set; } = null;

        /// <summary>
        /// Last error (sanitized).
        /// </summary>
        public string? LastError { get; set; } = null;

        /// <summary>
        /// Consecutive successes.
        /// </summary>
        public int ConsecutiveSuccesses { get; set; } = 0;

        /// <summary>
        /// Consecutive failures.
        /// </summary>
        public int ConsecutiveFailures { get; set; } = 0;

        /// <summary>
        /// Last healthy.
        /// </summary>
        public DateTime? LastHealthyUtc { get; set; } = null;

        /// <summary>
        /// Last unhealthy.
        /// </summary>
        public DateTime? LastUnhealthyUtc { get; set; } = null;

        /// <summary>
        /// Successful checks over all checks in the window.
        /// </summary>
        public double? UptimePercentage { get; set; } = null;

        /// <summary>
        /// Uptime window.
        /// </summary>
        public int UptimeWindowHours { get; set; } = 24;

        #endregion
    }
}
