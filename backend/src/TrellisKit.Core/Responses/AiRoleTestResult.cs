namespace TrellisKit.Core.Responses
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// The result of a test prompt through a task role's hierarchy.
    /// </summary>
    public class AiRoleTestResult
    {
        #region Public-Members

        /// <summary>
        /// Whether an executor answered.
        /// </summary>
        public bool Success { get; set; } = false;

        /// <summary>
        /// Reply text, or null.
        /// </summary>
        public string? Text { get; set; } = null;

        /// <summary>
        /// Position of the executor that answered, or null.
        /// </summary>
        public int? ExecutorPosition { get; set; } = null;

        /// <summary>
        /// Endpoint that answered, or null.
        /// </summary>
        public string? EndpointId { get; set; } = null;

        /// <summary>
        /// Model that answered, or null.
        /// </summary>
        public string? Model { get; set; } = null;

        /// <summary>
        /// One line per executor passed over, in order.
        /// </summary>
        public List<string> Escalations { get; set; } = new List<string>();

        /// <summary>
        /// Error code when no executor answered, or null.
        /// </summary>
        public string? ErrorCode { get; set; } = null;

        /// <summary>
        /// Total latency in milliseconds.
        /// </summary>
        public long LatencyMs { get; set; } = 0;

        /// <summary>
        /// Cost in US dollars.
        /// </summary>
        public double CostUsd { get; set; } = 0;

        #endregion
    }
}
