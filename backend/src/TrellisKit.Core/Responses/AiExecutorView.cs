namespace TrellisKit.Core.Responses
{
    using System;

    /// <summary>
    /// One executor of a task role, with its endpoint's name and health.
    /// </summary>
    public class AiExecutorView
    {
        #region Public-Members

        /// <summary>
        /// Position (0 is first).
        /// </summary>
        public int Position { get; set; } = 0;

        /// <summary>
        /// Runtime.
        /// </summary>
        public string Runtime { get; set; } = "ApiEndpoint";

        /// <summary>
        /// Endpoint identifier or Mux endpoint name, or null.
        /// </summary>
        public string? EndpointId { get; set; } = null;

        /// <summary>
        /// Endpoint name, or null.
        /// </summary>
        public string? EndpointName { get; set; } = null;

        /// <summary>
        /// Endpoint model, or the harness model, or null.
        /// </summary>
        public string? Model { get; set; } = null;

        /// <summary>
        /// Endpoint health (Unknown, Healthy, Unhealthy), or null for harnesses.
        /// </summary>
        public string? Health { get; set; } = null;

        /// <summary>
        /// Whether the executor is tried.
        /// </summary>
        public bool Enabled { get; set; } = true;

        /// <summary>
        /// Why the executor cannot run now (missing or disabled endpoint, harness on a non-worker instance), or null.
        /// </summary>
        public string? Problem { get; set; } = null;

        #endregion
    }
}
