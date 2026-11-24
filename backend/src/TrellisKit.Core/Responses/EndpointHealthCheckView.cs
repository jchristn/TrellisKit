namespace TrellisKit.Core.Responses
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// Health check settings.
    /// </summary>
    public class EndpointHealthCheckView
    {
        #region Public-Members

        /// <summary>
        /// Enabled.
        /// </summary>
        public bool Enabled { get; set; } = true;

        /// <summary>
        /// Mode.
        /// </summary>
        public ModelHealthCheckModeEnum Mode { get; set; } = ModelHealthCheckModeEnum.Connectivity;

        /// <summary>
        /// Interval.
        /// </summary>
        public int IntervalMs { get; set; } = 60000;

        /// <summary>
        /// Timeout.
        /// </summary>
        public int TimeoutMs { get; set; } = 10000;

        /// <summary>
        /// Successes to Healthy.
        /// </summary>
        public int HealthyThreshold { get; set; } = 2;

        /// <summary>
        /// Failures to Unhealthy.
        /// </summary>
        public int UnhealthyThreshold { get; set; } = 2;

        #endregion
    }
}
