namespace TrellisKit.Server.Services.Ai
{
    using System;
    using System.Text.Json;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// One validated job tool call as its handler sees it.
    /// </summary>
    public class AgentToolInvocation
    {
        #region Public-Members

        /// <summary>
        /// Tool name.
        /// </summary>
        public string ToolName { get; set; } = String.Empty;

        /// <summary>
        /// Arguments (a JSON object that matched the tool's schema). Valid only while the handler runs; clone it to keep
        /// it.
        /// </summary>
        public JsonElement Arguments { get; set; }

        /// <summary>
        /// Arguments as compact JSON text.
        /// </summary>
        public string ArgumentsJson { get; set; } = "{}";

        /// <summary>
        /// 1-based position of the call within the agent call.
        /// </summary>
        public int Sequence { get; set; } = 0;

        /// <summary>
        /// Executor runtime that made the call.
        /// </summary>
        public AiExecutorRuntimeEnum Runtime { get; set; } = AiExecutorRuntimeEnum.ApiEndpoint;

        /// <summary>
        /// Job key of the ambient job item, or null.
        /// </summary>
        public string? JobKey { get; set; } = null;

        /// <summary>
        /// Job run of the ambient job item, or null.
        /// </summary>
        public string? JobRunId { get; set; } = null;

        /// <summary>
        /// Job item the call belongs to, or null.
        /// </summary>
        public string? JobItemId { get; set; } = null;

        #endregion
    }
}
