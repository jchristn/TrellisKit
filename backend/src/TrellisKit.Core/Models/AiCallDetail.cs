namespace TrellisKit.Core.Models
{
    using System;

    /// <summary>
    /// The optional telemetry of one model call in the AI call ledger (docs/AI_TELEMETRY.md): timings, token detail,
    /// tool use, transport, and provider identifiers. Every value is null when the provider or the call path does not
    /// report it. Thread safety: not thread safe; one instance per call.
    /// </summary>
    public class AiCallDetail
    {
        #region Public-Members

        /// <summary>
        /// Milliseconds from sending the request to the first streamed text or reasoning token, or null when the call did not stream (for a harness: to its first text or reasoning event).
        /// </summary>
        public long? TimeToFirstTokenMs { get; set; } = null;

        /// <summary>
        /// Milliseconds from sending the request to the last token, or null when the call failed before any output.
        /// </summary>
        public long? TimeToLastTokenMs { get; set; } = null;

        /// <summary>
        /// Total streaming time in milliseconds (time to last token minus time to first token), or null when the call did not stream.
        /// </summary>
        public long? StreamingMs { get; set; } = null;

        /// <summary>
        /// Milliseconds the call waited for an endpoint concurrency slot, or null for calls that do not queue (harnesses).
        /// </summary>
        public long? QueueMs { get; set; } = null;

        /// <summary>
        /// Output tokens per second over the whole call as the provider client measured it, or derived from the completion tokens and the generation time; null when unknown.
        /// </summary>
        public double? TokensPerSecond { get; set; } = null;

        /// <summary>
        /// Total tokens the provider reported, or the sum of the reported input and output tokens; null when neither is known.
        /// </summary>
        public int? TotalTokens { get; set; } = null;

        /// <summary>
        /// Input tokens served from the provider's prompt cache (a cache read), or null when the provider does not report cache usage. A subset of the prompt tokens on OpenAI, Azure OpenAI, Gemini, Vertex AI, and Cohere; additional to them on Anthropic and Bedrock.
        /// </summary>
        public int? CachedReadTokens { get; set; } = null;

        /// <summary>
        /// Input tokens written into the provider's prompt cache (Anthropic, Bedrock, and the harnesses that report it), or null.
        /// </summary>
        public int? CacheCreationTokens { get; set; } = null;

        /// <summary>
        /// Text input tokens, where the provider splits input by modality (Gemini, Vertex AI), or null.
        /// </summary>
        public long? InputTextTokens { get; set; } = null;

        /// <summary>
        /// Image input tokens, where reported, or null.
        /// </summary>
        public long? InputImageTokens { get; set; } = null;

        /// <summary>
        /// Audio input tokens, where reported, or null.
        /// </summary>
        public long? InputAudioTokens { get; set; } = null;

        /// <summary>
        /// Video input tokens, where reported, or null.
        /// </summary>
        public long? InputVideoTokens { get; set; } = null;

        /// <summary>
        /// Tool calls this model call requested (for a harness: tool uses the CLI reported, or the job tool calls the engine served), or null when the call offered no tools.
        /// </summary>
        public int? ToolCalls { get; set; } = null;

        /// <summary>
        /// Milliseconds the engine spent executing the tool calls this model call requested (for a harness: the job tool handlers), or null when none ran.
        /// </summary>
        public long? ToolExecMs { get; set; } = null;

        /// <summary>
        /// Earlier attempts of this call on the same endpoint (0 for the first attempt), or null for calls that are not retried.
        /// </summary>
        public int? Retries { get; set; } = null;

        /// <summary>
        /// HTTP status of the provider's response, or null when no response arrived (and for harnesses).
        /// </summary>
        public int? StatusCode { get; set; } = null;

        /// <summary>
        /// The provider's finish reason (for example stop, length, tool_calls, end_turn), or null.
        /// </summary>
        public string? FinishReason { get; set; } = null;

        /// <summary>
        /// Whether the call streamed its output, or null when unknown.
        /// </summary>
        public bool? Streamed { get; set; } = null;

        /// <summary>
        /// Streamed chunks received, or null when the call did not stream.
        /// </summary>
        public int? Chunks { get; set; } = null;

        /// <summary>
        /// Bytes of the HTTP request bodies sent for this call, or null when not measured.
        /// </summary>
        public long? RequestBytes { get; set; } = null;

        /// <summary>
        /// Bytes of the HTTP response bodies, when the provider declares a length (streamed responses do not), or null.
        /// </summary>
        public long? ResponseBytes { get; set; } = null;

        /// <summary>
        /// The provider's response identifier (for a harness: its session identifier), or null.
        /// </summary>
        public string? ResponseId { get; set; } = null;

        /// <summary>
        /// The model the provider reported for the response, or null.
        /// </summary>
        public string? ProviderModel { get; set; } = null;

        /// <summary>
        /// The reasoning effort value actually sent to the provider (for example high, or a thinking budget), or null when none was sent.
        /// </summary>
        public string? ReasoningEffort { get; set; } = null;

        /// <summary>
        /// Model load time in milliseconds (Ollama), or null.
        /// </summary>
        public long? LoadMs { get; set; } = null;

        /// <summary>
        /// Prompt evaluation time in milliseconds (Ollama), or null.
        /// </summary>
        public long? PromptEvalMs { get; set; } = null;

        /// <summary>
        /// Token generation time in milliseconds (Ollama), or null.
        /// </summary>
        public long? EvalMs { get; set; } = null;

        /// <summary>
        /// Exit code of the harness process, or null for API calls.
        /// </summary>
        public int? ExitCode { get; set; } = null;

        #endregion

        #region Public-Methods

        /// <summary>
        /// Copy every value onto another instance.
        /// </summary>
        /// <param name="target">Instance to fill.</param>
        /// <exception cref="ArgumentNullException">target is null.</exception>
        public void CopyTo(AiCallDetail target)
        {
            ArgumentNullException.ThrowIfNull(target);
            target.TimeToFirstTokenMs = TimeToFirstTokenMs;
            target.TimeToLastTokenMs = TimeToLastTokenMs;
            target.StreamingMs = StreamingMs;
            target.QueueMs = QueueMs;
            target.TokensPerSecond = TokensPerSecond;
            target.TotalTokens = TotalTokens;
            target.CachedReadTokens = CachedReadTokens;
            target.CacheCreationTokens = CacheCreationTokens;
            target.InputTextTokens = InputTextTokens;
            target.InputImageTokens = InputImageTokens;
            target.InputAudioTokens = InputAudioTokens;
            target.InputVideoTokens = InputVideoTokens;
            target.ToolCalls = ToolCalls;
            target.ToolExecMs = ToolExecMs;
            target.Retries = Retries;
            target.StatusCode = StatusCode;
            target.FinishReason = FinishReason;
            target.Streamed = Streamed;
            target.Chunks = Chunks;
            target.RequestBytes = RequestBytes;
            target.ResponseBytes = ResponseBytes;
            target.ResponseId = ResponseId;
            target.ProviderModel = ProviderModel;
            target.ReasoningEffort = ReasoningEffort;
            target.LoadMs = LoadMs;
            target.PromptEvalMs = PromptEvalMs;
            target.EvalMs = EvalMs;
            target.ExitCode = ExitCode;
        }

        #endregion
    }
}
