namespace TrellisKit.Core.Responses
{
    using System;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// Server health.
    /// </summary>
    public class HealthResponse
    {
        #region Public-Members

        /// <summary>
        /// Overall status.
        /// </summary>
        public HealthStatusEnum Status { get; set; } = HealthStatusEnum.Healthy;

        /// <summary>
        /// Server version.
        /// </summary>
        public string Version { get; set; } = String.Empty;

        /// <summary>
        /// UTC time the server started.
        /// </summary>
        public DateTime StartedUtc { get; set; }

        /// <summary>
        /// Seconds since the server started.
        /// </summary>
        public double UptimeSeconds { get; set; } = 0;

        /// <summary>
        /// Database provider in use.
        /// </summary>
        public DatabaseTypeEnum Database { get; set; } = DatabaseTypeEnum.Postgresql;

        #endregion
    }
}
