namespace TrellisKit.Server.Services.Ai
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// The result of a task role call through its executor hierarchy.
    /// </summary>
    public class RoleOutcome
    {
        #region Public-Members

        /// <summary>
        /// Whether an executor answered.
        /// </summary>
        public bool Success { get; set; } = false;

        /// <summary>
        /// Reply text (empty on failure).
        /// </summary>
        public string Text { get; set; } = String.Empty;

        /// <summary>
        /// Position of the executor that answered, or null.
        /// </summary>
        public int? ExecutorPosition { get; set; } = null;

        /// <summary>
        /// Runtime of the executor that answered (ApiEndpoint, ClaudeCode, ...), or null.
        /// </summary>
        public string? Runtime { get; set; } = null;

        /// <summary>
        /// Endpoint (or Mux endpoint name) that answered, or null.
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
        /// NotAvailable, UnknownRole, NoExecutors, or ExecutorsExhausted when no executor answered, or null.
        /// </summary>
        public string? ErrorCode { get; set; } = null;

        /// <summary>
        /// For a failure: whether trying again later may succeed (exhaustion policy Defer, or a busy hierarchy).
        /// </summary>
        public bool Retryable { get; set; } = false;

        /// <summary>
        /// Prompt tokens of the answering call.
        /// </summary>
        public int PromptTokens { get; set; } = 0;

        /// <summary>
        /// Completion tokens of the answering call.
        /// </summary>
        public int CompletionTokens { get; set; } = 0;

        /// <summary>
        /// Cost of every call made, in micro-dollars.
        /// </summary>
        public long CostMicros { get; set; } = 0;

        /// <summary>
        /// Total latency in milliseconds.
        /// </summary>
        public long LatencyMs { get; set; } = 0;

        #endregion
    }
}
