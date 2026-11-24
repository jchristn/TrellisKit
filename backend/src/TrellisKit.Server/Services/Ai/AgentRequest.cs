namespace TrellisKit.Server.Services.Ai
{
    using System;
    using System.Collections.Generic;
    using PolyPrompt.Models;

    /// <summary>
    /// One agentic task for <see cref="TaskRoleService.RunAgentAsync"/>: the instructions, the job tools the agent hands
    /// its results back through, and the limits. The same request runs on a CLI agent harness (through the job-scoped
    /// MCP server) or on an API endpoint (through the engine's tool loop).
    /// </summary>
    public class AgentRequest
    {
        #region Public-Members

        /// <summary>
        /// Task instructions (treated as data under the scope guardrail). Required, at most
        /// <see cref="MaxInstructionCharacters"/> characters.
        /// </summary>
        public string Instructions { get; set; } = String.Empty;

        /// <summary>
        /// Extra system instructions placed after the scope guard and the engine's agent rules, or null.
        /// </summary>
        public string? SystemPrompt { get; set; } = null;

        /// <summary>
        /// The job tools (1 to <see cref="AgentToolSet.MaxTools"/>).
        /// </summary>
        public List<AgentTool> Tools
        {
            get { return _Tools; }
            set { _Tools = value ?? new List<AgentTool>(); }
        }

        /// <summary>
        /// Harness permission mode: Restricted (the default) or ReadOnly. Other modes are refused.
        /// </summary>
        public CliPermissionMode PermissionMode { get; set; } = CliPermissionMode.Restricted;

        /// <summary>
        /// Whether a harness may use its own web search and fetch tools (research roles). Default false. Claude Code then
        /// pre-approves WebSearch and WebFetch; other harnesses keep the access their permission mode gives.
        /// </summary>
        public bool AllowWebResearch { get; set; } = false;

        /// <summary>
        /// Most job tool calls per executor attempt, or null for jobs.harnessMaxToolCalls.
        /// </summary>
        public int? MaxToolCalls { get; set; } = null;

        /// <summary>
        /// Harness timeout in seconds (30 to 3600), or null for jobs.harnessTimeoutSeconds.
        /// </summary>
        public int? TimeoutSeconds { get; set; } = null;

        /// <summary>
        /// Most model rounds of the API tool loop. Default 8, minimum 1, maximum 20.
        /// </summary>
        public int MaxRounds
        {
            get { return _MaxRounds; }
            set { _MaxRounds = Math.Clamp(value, 1, 20); }
        }

        /// <summary>
        /// Output token cap per API round, or null for the endpoint's.
        /// </summary>
        public int? MaxTokens { get; set; } = null;

        /// <summary>
        /// Longest instructions accepted, in characters.
        /// </summary>
        public const int MaxInstructionCharacters = 100000;

        #endregion

        #region Private-Members

        private List<AgentTool> _Tools = new List<AgentTool>();
        private int _MaxRounds = 8;

        #endregion
    }
}
