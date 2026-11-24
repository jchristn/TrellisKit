namespace TrellisKit.Server.Services.Ai
{
    using System;
    using PolyPrompt.Models;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// One CLI agent harness call for <see cref="HarnessService.RunAsync"/>.
    /// </summary>
    public class HarnessCall
    {
        #region Public-Members

        /// <summary>
        /// Harness runtime (not ApiEndpoint).
        /// </summary>
        public AiExecutorRuntimeEnum Runtime { get; set; } = AiExecutorRuntimeEnum.ClaudeCode;

        /// <summary>
        /// Model in the harness's own naming (the executor's model), or null for the harness default.
        /// </summary>
        public string? Model { get; set; } = null;

        /// <summary>
        /// Mux endpoint name (the executor's endpoint), or null.
        /// </summary>
        public string? MuxEndpoint { get; set; } = null;

        /// <summary>
        /// Prompt (standard input).
        /// </summary>
        public string Prompt { get; set; } = String.Empty;

        /// <summary>
        /// System instructions. They must start with the scope guard; <see cref="HarnessService"/> refuses a call whose
        /// system prompt does not.
        /// </summary>
        public string SystemPrompt { get; set; } = String.Empty;

        /// <summary>
        /// Permission mode: Restricted (default) or ReadOnly.
        /// </summary>
        public CliPermissionMode PermissionMode { get; set; } = CliPermissionMode.Restricted;

        /// <summary>
        /// Timeout in milliseconds (killed after). Default 600000.
        /// </summary>
        public int TimeoutMs { get; set; } = 600000;

        /// <summary>
        /// Job tools served over the job-scoped MCP server, or null for a plain prompt.
        /// </summary>
        public AgentToolSet? Tools { get; set; } = null;

        /// <summary>
        /// Whether Claude Code pre-approves WebSearch and WebFetch.
        /// </summary>
        public bool AllowWebResearch { get; set; } = false;

        /// <summary>
        /// Test traffic (ledger flag).
        /// </summary>
        public bool IsTest { get; set; } = false;

        #endregion
    }
}
