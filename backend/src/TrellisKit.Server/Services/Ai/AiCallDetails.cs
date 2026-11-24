namespace TrellisKit.Server.Services.Ai
{
    using System;
    using System.Text.Json;
    using PolyPrompt.Models;
    using TrellisKit.Core.Integrations.Models;
    using TrellisKit.Core.Models;

    /// <summary>
    /// Fills the optional telemetry of a ledger row (docs/AI_TELEMETRY.md) from what PolyPrompt reports: token detail
    /// from <see cref="TokenUsage"/>, transport sizes from the call's <see cref="ModelCallProbe"/>, and the fields
    /// every response carries. A value the provider does not report stays null.
    /// Thread safety: stateless.
    /// </summary>
    public static class AiCallDetails
    {
        #region Public-Methods

        /// <summary>
        /// Copy the token detail of a usage block: total, cache read and cache creation tokens, input tokens by modality,
        /// and Ollama's load, prompt evaluation, and generation times.
        /// </summary>
        /// <param name="detail">Detail to fill.</param>
        /// <param name="usage">Usage the provider reported, or null (nothing changes).</param>
        /// <exception cref="ArgumentNullException">detail is null.</exception>
        public static void ApplyUsage(AiCallDetail detail, TokenUsage? usage)
        {
            ArgumentNullException.ThrowIfNull(detail);
            if (usage == null) return;
            detail.TotalTokens = usage.TotalTokens ?? detail.TotalTokens;
            detail.CachedReadTokens = usage.CachedPromptTokens ?? detail.CachedReadTokens;
            detail.CacheCreationTokens = usage.CacheCreationTokens ?? detail.CacheCreationTokens;
            if (usage.InputByModality != null)
            {
                detail.InputTextTokens = usage.InputByModality.Text;
                detail.InputImageTokens = usage.InputByModality.Image;
                detail.InputAudioTokens = usage.InputByModality.Audio;
                detail.InputVideoTokens = usage.InputByModality.Video;
            }
            detail.LoadMs = Milliseconds(usage.LoadDurationNs) ?? detail.LoadMs;
            detail.PromptEvalMs = Milliseconds(usage.PromptEvalDurationNs) ?? detail.PromptEvalMs;
            detail.EvalMs = Milliseconds(usage.EvalDurationNs) ?? detail.EvalMs;
        }

        /// <summary>
        /// Copy the request and response sizes a probe measured.
        /// </summary>
        /// <param name="detail">Detail to fill.</param>
        /// <param name="probe">Probe of the call, or null (nothing changes).</param>
        /// <exception cref="ArgumentNullException">detail is null.</exception>
        public static void ApplyProbe(AiCallDetail detail, ModelCallProbe? probe)
        {
            ArgumentNullException.ThrowIfNull(detail);
            if (probe == null) return;
            detail.RequestBytes = probe.RequestBytes;
            detail.ResponseBytes = probe.ResponseBytes;
        }

        /// <summary>
        /// The detail of a call that returns one whole response (embeddings, rerank, tests, health checks).
        /// </summary>
        /// <param name="response">Response, or null when the call threw.</param>
        /// <param name="usage">Usage of the response, or null.</param>
        /// <param name="probe">Probe of the call, or null.</param>
        /// <param name="queueMs">Wait for a concurrency slot in milliseconds, or null when the call did not queue.</param>
        /// <param name="streamed">Whether the call streamed.</param>
        /// <returns>Detail.</returns>
        public static AiCallDetail ForResponse(ResponseBase? response, TokenUsage? usage, ModelCallProbe? probe, long? queueMs, bool streamed)
        {
            AiCallDetail detail = new AiCallDetail
            {
                QueueMs = queueMs,
                Streamed = streamed,
                Retries = 0,
                StatusCode = response?.StatusCode,
                ProviderModel = response?.Model,
                ReasoningEffort = response?.AppliedReasoningEffort
            };
            ApplyUsage(detail, usage);
            ApplyProbe(detail, probe);
            return detail;
        }

        /// <summary>
        /// Input tokens of an embedding response. PolyPrompt 5.0.1 does not fill <see cref="EmbeddingResponse.Usage"/>, so
        /// the count is read from the provider's own response JSON when it carries one: usage.prompt_tokens or
        /// usage.total_tokens (OpenAI wire, Voyage AI), prompt_eval_count (Ollama), meta.billed_units.input_tokens
        /// (Cohere), or usageMetadata.promptTokenCount (Gemini).
        /// </summary>
        /// <param name="response">Embedding response, or null.</param>
        /// <returns>Tokens, or null when the provider reports none.</returns>
        public static int? EmbeddingTokens(EmbeddingResponse? response)
        {
            if (response == null) return null;
            int? reported = response.Usage?.PromptTokens ?? response.Usage?.TotalTokens;
            if (reported != null) return reported;
            if (response.RawResponse == null || response.RawResponse.Value.ValueKind != JsonValueKind.Object) return null;
            JsonElement root = response.RawResponse.Value;
            return Number(root, "usage", "prompt_tokens") ?? Number(root, "usage", "total_tokens") ?? Number(root, "prompt_eval_count") ??
                Number(root, "meta", "billed_units", "input_tokens") ?? Number(root, "usageMetadata", "promptTokenCount");
        }

        /// <summary>
        /// Output tokens per second: over the provider's generation time when it reports one (Ollama), else over the whole
        /// call.
        /// </summary>
        /// <param name="outputTokens">Output tokens.</param>
        /// <param name="evalMs">Generation time the provider reported, or null.</param>
        /// <param name="latencyMs">Whole-call latency, or null when the call failed.</param>
        /// <returns>Tokens per second rounded to 3 places, or null when it cannot be derived.</returns>
        public static double? TokensPerSecond(int outputTokens, long? evalMs, long? latencyMs)
        {
            if (outputTokens <= 0) return null;
            long? over = evalMs != null && evalMs.Value > 0 ? evalMs : latencyMs;
            if (over == null || over.Value <= 0) return null;
            return Math.Round(outputTokens * 1000.0 / over.Value, 3);
        }

        #endregion

        #region Private-Methods

        private static int? Number(JsonElement root, params string[] path)
        {
            JsonElement current = root;
            foreach (string name in path)
            {
                if (current.ValueKind != JsonValueKind.Object || !current.TryGetProperty(name, out current)) return null;
            }
            return current.ValueKind == JsonValueKind.Number && current.TryGetInt32(out int value) && value >= 0 ? value : null;
        }

        private static long? Milliseconds(long? nanoseconds)
        {
            return nanoseconds == null ? null : nanoseconds.Value / 1000000;
        }

        #endregion
    }
}
