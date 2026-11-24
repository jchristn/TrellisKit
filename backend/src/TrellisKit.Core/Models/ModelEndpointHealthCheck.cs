namespace TrellisKit.Core.Models
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// One health check record of a model endpoint.
    /// </summary>
    public class ModelEndpointHealthCheck
    {
        #region Public-Members

        /// <summary>
        /// Identifier (mhc_).
        /// </summary>
        public string Id { get; set; } = String.Empty;

        /// <summary>
        /// Endpoint id.
        /// </summary>
        public string EndpointId { get; set; } = String.Empty;

        /// <summary>
        /// Check time.
        /// </summary>
        public DateTime CheckedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Whether the check passed.
        /// </summary>
        public bool Success { get; set; } = false;

        /// <summary>
        /// Latency.
        /// </summary>
        public long LatencyMs { get; set; } = 0;

        /// <summary>
        /// Provider HTTP status, when known.
        /// </summary>
        public int? StatusCode { get; set; } = null;

        /// <summary>
        /// Sanitized error.
        /// </summary>
        public string? Error { get; set; } = null;

        /// <summary>
        /// Mode used.
        /// </summary>
        public ModelHealthCheckModeEnum Mode { get; set; } = ModelHealthCheckModeEnum.Connectivity;

        #endregion
    }
}
