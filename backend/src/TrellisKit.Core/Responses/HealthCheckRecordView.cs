namespace TrellisKit.Core.Responses
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// A health check record.
    /// </summary>
    public class HealthCheckRecordView
    {
        #region Public-Members

        /// <summary>
        /// Identifier.
        /// </summary>
        public string Id { get; set; } = String.Empty;

        /// <summary>
        /// Check time.
        /// </summary>
        public DateTime CheckedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Passed.
        /// </summary>
        public bool Success { get; set; } = false;

        /// <summary>
        /// Latency.
        /// </summary>
        public long LatencyMs { get; set; } = 0;

        /// <summary>
        /// Provider status.
        /// </summary>
        public int? StatusCode { get; set; } = null;

        /// <summary>
        /// Error (sanitized).
        /// </summary>
        public string? Error { get; set; } = null;

        /// <summary>
        /// Mode.
        /// </summary>
        public ModelHealthCheckModeEnum Mode { get; set; } = ModelHealthCheckModeEnum.Connectivity;

        /// <summary>
        /// Labels.
        /// </summary>
        public List<string> Labels { get; set; } = new List<string>();

        /// <summary>
        /// Tags.
        /// </summary>
        public Dictionary<string, string> Tags { get; set; } = new Dictionary<string, string>();

        #endregion
    }
}
