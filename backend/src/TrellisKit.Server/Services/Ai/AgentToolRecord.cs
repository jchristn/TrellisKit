namespace TrellisKit.Server.Services.Ai
{
    using System;

    /// <summary>
    /// One job tool call made during an agent call, accepted or not, in the order the agent made it.
    /// </summary>
    public class AgentToolRecord
    {
        #region Public-Members

        /// <summary>
        /// 1-based position of the call.
        /// </summary>
        public int Sequence { get; set; } = 0;

        /// <summary>
        /// Position of the executor that made the call.
        /// </summary>
        public int ExecutorPosition { get; set; } = 0;

        /// <summary>
        /// Tool name the agent asked for.
        /// </summary>
        public string ToolName { get; set; } = String.Empty;

        /// <summary>
        /// Arguments as the agent sent them (truncated to the argument limit).
        /// </summary>
        public string ArgumentsJson { get; set; } = "{}";

        /// <summary>
        /// Whether the handler accepted the call.
        /// </summary>
        public bool Accepted { get; set; } = false;

        /// <summary>
        /// Why the engine refused the call before the handler ran (UnknownTool, InvalidJson, ArgumentsTooLarge,
        /// InvalidArguments, ToolLimit, HandlerFailed), or null when the handler decided.
        /// </summary>
        public string? Error { get; set; } = null;

        /// <summary>
        /// Message returned to the agent.
        /// </summary>
        public string Message { get; set; } = String.Empty;

        /// <summary>
        /// Structured data the handler kept, or null.
        /// </summary>
        public string? DataJson { get; set; } = null;

        /// <summary>
        /// When the call arrived (UTC).
        /// </summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Milliseconds the tool's handler ran (0 for a call refused before its handler).
        /// </summary>
        public long DurationMs { get; set; } = 0;

                #endregion
    }
}
