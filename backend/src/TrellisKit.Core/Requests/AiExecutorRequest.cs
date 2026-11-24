namespace TrellisKit.Core.Requests
{
    /// <summary>
    /// One executor of a task role.
    /// </summary>
    public class AiExecutorRequest
    {
        #region Public-Members

        /// <summary>
        /// ApiEndpoint, ClaudeCode, Codex, GeminiCli, CursorCli, Mux, OpenCode, or Pi; required.
        /// </summary>
        public string? Runtime { get; set; } = null;

        /// <summary>
        /// Model endpoint identifier (required for ApiEndpoint) or Mux endpoint name, or null.
        /// </summary>
        public string? EndpointId { get; set; } = null;

        /// <summary>
        /// Model name for a harness, or null; at most 200 characters.
        /// </summary>
        public string? Model { get; set; } = null;

        /// <summary>
        /// Whether the executor is tried. Default true.
        /// </summary>
        public bool Enabled { get; set; } = true;

        #endregion
    }
}
