namespace TrellisKit.Core.Enums
{
    /// <summary>
    /// What runs a task role's executor (docs/INTEGRATION.md).
    /// </summary>
    public enum AiExecutorRuntimeEnum
    {
        /// <summary>
        /// A model endpoint called through PolyPrompt (API or open model); the gateway runs any tool loop.
        /// </summary>
        ApiEndpoint,

        /// <summary>
        /// The Claude Code CLI harness (worker instances only).
        /// </summary>
        ClaudeCode,

        /// <summary>
        /// The Codex CLI harness (worker instances only).
        /// </summary>
        Codex,

        /// <summary>
        /// The Gemini CLI harness (worker instances only).
        /// </summary>
        GeminiCli,

        /// <summary>
        /// The Cursor CLI harness (worker instances only).
        /// </summary>
        CursorCli,

        /// <summary>
        /// The Mux CLI harness, which fronts local and open models (worker instances only).
        /// </summary>
        Mux,

        /// <summary>
        /// The OpenCode CLI harness (worker instances only).
        /// </summary>
        OpenCode,

        /// <summary>
        /// The Pi CLI harness (worker instances only).
        /// </summary>
        Pi
    }
}
