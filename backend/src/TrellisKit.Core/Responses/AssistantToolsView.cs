namespace TrellisKit.Core.Responses
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// An assistant's tool policy (docs/AI_CONTRACT.md sections 5.1 and 8).
    /// </summary>
    public class AssistantToolsView
    {
        #region Public-Members

        /// <summary>
        /// Whether the assistant may call tools.
        /// </summary>
        public bool Enabled { get; set; } = false;

        /// <summary>
        /// Canonical names of the registered tools the assistant may use.
        /// </summary>
        public List<string> Allowed { get; set; } = new List<string>();

        /// <summary>
        /// Most model rounds that may request tools in one turn.
        /// </summary>
        public int MaxToolIterations { get; set; } = 4;

        /// <summary>
        /// Most tool calls executed in one turn.
        /// </summary>
        public int MaxToolCallsPerTurn { get; set; } = 6;

        /// <summary>
        /// Time limit of one tool call in milliseconds.
        /// </summary>
        public int ToolCallTimeoutMs { get; set; } = 15000;

        /// <summary>
        /// Most characters of one tool output handed to the model.
        /// </summary>
        public int MaxToolOutputCharacters { get; set; } = 8000;

        /// <summary>
        /// Token budget of one turn across every model round (0 = no extra cap).
        /// </summary>
        public int MaxTokensPerTurn { get; set; } = 32000;

        /// <summary>
        /// Whether the stream emits tool activity events.
        /// </summary>
        public bool ShowToolActivity { get; set; } = true;

        #endregion
    }
}
