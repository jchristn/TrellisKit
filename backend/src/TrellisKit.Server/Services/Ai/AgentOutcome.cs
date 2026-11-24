namespace TrellisKit.Server.Services.Ai
{
    using System;
    using System.Collections.Generic;
    using System.Linq;

    /// <summary>
    /// The result of <see cref="TaskRoleService.RunAgentAsync"/>: which executor finished the task, its closing text, and
    /// every job tool call made on the way (including calls from executors that later failed, since their handlers may
    /// already have stored results).
    /// </summary>
    public class AgentOutcome
    {
        #region Public-Members

        /// <summary>
        /// Whether an executor finished the task.
        /// </summary>
        public bool Success { get; set; } = false;

        /// <summary>
        /// The agent's closing text (empty on failure).
        /// </summary>
        public string Text { get; set; } = String.Empty;

        /// <summary>
        /// Runtime of the executor that finished (ApiEndpoint, ClaudeCode, ...), or null.
        /// </summary>
        public string? Runtime { get; set; } = null;

        /// <summary>
        /// Position of the executor that finished, or null.
        /// </summary>
        public int? ExecutorPosition { get; set; } = null;

        /// <summary>
        /// Endpoint (API executors) or Mux endpoint name that finished, or null.
        /// </summary>
        public string? EndpointId { get; set; } = null;

        /// <summary>
        /// Model that finished, as reported, or null.
        /// </summary>
        public string? Model { get; set; } = null;

        /// <summary>
        /// One line per executor passed over, in order.
        /// </summary>
        public List<string> Escalations { get; set; } = new List<string>();

        /// <summary>
        /// NotAvailable, UnknownRole, NotAgentic, InvalidRequest, NoExecutors, or ExecutorsExhausted, or null.
        /// </summary>
        public string? ErrorCode { get; set; } = null;

        /// <summary>
        /// For a failure: whether trying again later may succeed.
        /// </summary>
        public bool Retryable { get; set; } = false;

        /// <summary>
        /// Every job tool call, in order.
        /// </summary>
        public List<AgentToolRecord> ToolCalls { get; set; } = new List<AgentToolRecord>();

        /// <summary>
        /// The accepted calls, in order.
        /// </summary>
        public List<AgentToolRecord> Accepted
        {
            get { return ToolCalls.Where(c => c.Accepted).ToList(); }
        }

        /// <summary>
        /// Prompt tokens of the finishing executor.
        /// </summary>
        public int PromptTokens { get; set; } = 0;

        /// <summary>
        /// Completion tokens of the finishing executor.
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
