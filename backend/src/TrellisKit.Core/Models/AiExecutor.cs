namespace TrellisKit.Core.Models
{
    using System;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// One entry of a task role's executor hierarchy.
    /// </summary>
    public class AiExecutor
    {
        #region Public-Members

        /// <summary>
        /// Position in the hierarchy (0 is tried first).
        /// </summary>
        public int Position { get; set; } = 0;

        /// <summary>
        /// What runs the executor.
        /// </summary>
        public AiExecutorRuntimeEnum Runtime { get; set; } = AiExecutorRuntimeEnum.ApiEndpoint;

        /// <summary>
        /// Model endpoint identifier for ApiEndpoint, or the Mux endpoint name for Mux; null for other harnesses.
        /// </summary>
        public string? EndpointId { get; set; } = null;

        /// <summary>
        /// Model name for a harness, or null for the endpoint's or harness's default.
        /// </summary>
        public string? Model { get; set; } = null;

        /// <summary>
        /// Whether the executor is tried. Default true.
        /// </summary>
        public bool Enabled { get; set; } = true;

        #endregion
    }
}
