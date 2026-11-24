namespace TrellisKit.Server.Services.Ai
{
    using System;

    /// <summary>
    /// The outcome of one CLI agent harness call, from its Completed or Failed event.
    /// </summary>
    public class HarnessCallResult
    {
        #region Public-Members

        /// <summary>
        /// Whether the harness completed.
        /// </summary>
        public bool Success { get; set; } = false;

        /// <summary>
        /// Final text (empty on failure).
        /// </summary>
        public string Text { get; set; } = String.Empty;

        /// <summary>
        /// HarnessesDisabled, NoJobScope, NotSupported, InvalidPermissionMode, MissingScopeGuard, or Harness{FailureKind}
        /// (for example HarnessNotInstalled, HarnessRateLimited, HarnessTimedOut), or null.
        /// </summary>
        public string? ErrorCode { get; set; } = null;

        /// <summary>
        /// Error message (sanitized by PolyPrompt), or null.
        /// </summary>
        public string? Error { get; set; } = null;

        /// <summary>
        /// Whether the failure was the harness being busy or rate limited.
        /// </summary>
        public bool Busy { get; set; } = false;

        /// <summary>
        /// Model the harness reported, or the requested model, or null.
        /// </summary>
        public string? Model { get; set; } = null;

        /// <summary>
        /// Prompt tokens.
        /// </summary>
        public int PromptTokens { get; set; } = 0;

        /// <summary>
        /// Completion tokens.
        /// </summary>
        public int CompletionTokens { get; set; } = 0;

        /// <summary>
        /// Reasoning tokens.
        /// </summary>
        public int ReasoningTokens { get; set; } = 0;

        /// <summary>
        /// Cost reported by the harness, in micro-dollars (0 when it reports none).
        /// </summary>
        public long CostMicros { get; set; } = 0;

        /// <summary>
        /// Latency in milliseconds.
        /// </summary>
        public long LatencyMs { get; set; } = 0;

        /// <summary>
        /// The call's working directory (deleted when the call ends), or null when none was created.
        /// </summary>
        public string? WorkingDirectory { get; set; } = null;

        /// <summary>
        /// Whether the call's directory was removed.
        /// </summary>
        public bool DirectoryRemoved { get; set; } = false;

        #endregion
    }
}
