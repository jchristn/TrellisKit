namespace TrellisKit.Core.Observability
{
    using System;
    using System.Diagnostics;
    using System.Diagnostics.Metrics;
    using System.Threading;

    /// <summary>
    /// Instruments of the AI models and assistants feature (docs/AI_CONTRACT.md section 16), on the engine meter
    /// "TrellisKit" (exported as trelliskit_ai_*). Labels are low cardinality: purpose, api_type, kind, outcome, and
    /// similar bounded values; endpoint and assistant ids are never metric labels (they appear on spans only).
    /// </summary>
    public static class AiTelemetry
    {
        #region Public-Members

        /// <summary>
        /// Model calls by purpose, api_type, outcome, fallback, test.
        /// </summary>
        public static readonly Counter<long> Requests = TrellisKitTelemetry.Meter.CreateCounter<long>("trelliskit.ai.requests", "{request}", "Model calls.");

        /// <summary>
        /// Model call duration in seconds by purpose, api_type, outcome.
        /// </summary>
        public static readonly Histogram<double> RequestDuration = TrellisKitTelemetry.Meter.CreateHistogram<double>("trelliskit.ai.request.duration", "s", "Model call duration.");

        /// <summary>
        /// Time to first token in seconds by api_type.
        /// </summary>
        public static readonly Histogram<double> TimeToFirstToken = TrellisKitTelemetry.Meter.CreateHistogram<double>("trelliskit.ai.time_to_first_token", "s", "Time to first streamed token.");

        /// <summary>
        /// Tokens by purpose, api_type, direction.
        /// </summary>
        public static readonly Counter<long> Tokens = TrellisKitTelemetry.Meter.CreateCounter<long>("trelliskit.ai.tokens", "{token}", "Tokens used.");

        /// <summary>
        /// Cost in millionths of a US dollar by purpose, api_type.
        /// </summary>
        public static readonly Counter<long> CostMicros = TrellisKitTelemetry.Meter.CreateCounter<long>("trelliskit.ai.cost.micros", "{microdollar}", "Model spend in millionths of a US dollar.");

        /// <summary>
        /// trelliskit.ai.time_to_last_token (seconds). Labels: api_type.
        /// </summary>
        public static readonly Histogram<double> TimeToLastToken = TrellisKitTelemetry.Meter.CreateHistogram<double>("trelliskit.ai.time_to_last_token", "s", "Time to the last token of a model call.");

        /// <summary>
        /// trelliskit.ai.stream.duration (seconds, first token to last token). Labels: api_type.
        /// </summary>
        public static readonly Histogram<double> StreamDuration = TrellisKitTelemetry.Meter.CreateHistogram<double>("trelliskit.ai.stream.duration", "s", "Streaming time of a model call, first token to last token.");

        /// <summary>
        /// trelliskit.ai.tokens_per_second. Labels: api_type.
        /// </summary>
        public static readonly Histogram<double> TokensPerSecond = TrellisKitTelemetry.Meter.CreateHistogram<double>("trelliskit.ai.tokens_per_second", "{token}/s", "Output tokens per second of a model call.");

        /// <summary>
        /// trelliskit.ai.call.tool_duration (seconds the engine spent executing the tools one model call requested). Labels: api_type.
        /// </summary>
        public static readonly Histogram<double> CallToolDuration = TrellisKitTelemetry.Meter.CreateHistogram<double>("trelliskit.ai.call.tool_duration", "s", "Tool execution time attributed to the model call that requested the tools.");

        /// <summary>
        /// trelliskit.ai.call.tool_calls (tool calls one model call requested). Labels: api_type.
        /// </summary>
        public static readonly Histogram<double> CallToolCalls = TrellisKitTelemetry.Meter.CreateHistogram<double>("trelliskit.ai.call.tool_calls", "{call}", "Tool calls requested by one model call.");

        /// <summary>
        /// trelliskit.ai.request.bytes. Labels: api_type.
        /// </summary>
        public static readonly Histogram<double> RequestBytes = TrellisKitTelemetry.Meter.CreateHistogram<double>("trelliskit.ai.request.bytes", "By", "Request body size of a model call.");

        /// <summary>
        /// trelliskit.ai.response.bytes (responses that declare a length). Labels: api_type.
        /// </summary>
        public static readonly Histogram<double> ResponseBytes = TrellisKitTelemetry.Meter.CreateHistogram<double>("trelliskit.ai.response.bytes", "By", "Response body size of a model call.");

        /// <summary>
        /// Endpoint queue wait in seconds by api_type.
        /// </summary>
        public static readonly Histogram<double> QueueWait = TrellisKitTelemetry.Meter.CreateHistogram<double>("trelliskit.ai.queue.wait", "s", "Wait for an endpoint concurrency slot.");

        /// <summary>
        /// Calls rejected because the endpoint queue was full, by api_type.
        /// </summary>
        public static readonly Counter<long> QueueRejected = TrellisKitTelemetry.Meter.CreateCounter<long>("trelliskit.ai.queue.rejected", "{request}", "Calls rejected by a full endpoint queue.");

        /// <summary>
        /// Retries by api_type and reason.
        /// </summary>
        public static readonly Counter<long> Retries = TrellisKitTelemetry.Meter.CreateCounter<long>("trelliskit.ai.retries", "{retry}", "Model call retries.");

        /// <summary>
        /// Health checks by mode and outcome.
        /// </summary>
        public static readonly Counter<long> HealthChecks = TrellisKitTelemetry.Meter.CreateCounter<long>("trelliskit.ai.health_checks", "{check}", "Endpoint health checks.");

        /// <summary>
        /// Transitions to Unhealthy.
        /// </summary>
        public static readonly Counter<long> UnhealthyTransitions = TrellisKitTelemetry.Meter.CreateCounter<long>("trelliskit.ai.endpoint.unhealthy_transitions", "{transition}", "Endpoints that became Unhealthy.");

        /// <summary>
        /// Messages by role, surface, audience.
        /// </summary>
        public static readonly Counter<long> Messages = TrellisKitTelemetry.Meter.CreateCounter<long>("trelliskit.ai.messages", "{message}", "Conversation messages.");

        /// <summary>
        /// Conversations created by surface.
        /// </summary>
        public static readonly Counter<long> ConversationsCreated = TrellisKitTelemetry.Meter.CreateCounter<long>("trelliskit.ai.conversations.created", "{conversation}", "Conversations created.");

        /// <summary>
        /// Feedback by rating.
        /// </summary>
        public static readonly Counter<long> Feedback = TrellisKitTelemetry.Meter.CreateCounter<long>("trelliskit.ai.feedback", "{feedback}", "Reply feedback.");

        /// <summary>
        /// Moderation verdicts by stage, verdict, source.
        /// </summary>
        public static readonly Counter<long> Moderation = TrellisKitTelemetry.Meter.CreateCounter<long>("trelliskit.ai.moderation", "{verdict}", "Moderation verdicts.");

        /// <summary>
        /// Knowledge retrieval duration in seconds by mode.
        /// </summary>
        public static readonly Histogram<double> RetrievalDuration = TrellisKitTelemetry.Meter.CreateHistogram<double>("trelliskit.ai.knowledge.retrieval.duration", "s", "Knowledge retrieval duration.");

        /// <summary>
        /// Knowledge items placed in the context, by mode.
        /// </summary>
        public static readonly Histogram<double> RetrievalItems = TrellisKitTelemetry.Meter.CreateHistogram<double>("trelliskit.ai.knowledge.items", "{item}", "Knowledge items in the context.");

        /// <summary>
        /// Knowledge chunks ingested, by outcome.
        /// </summary>
        public static readonly Counter<long> IngestedChunks = TrellisKitTelemetry.Meter.CreateCounter<long>("trelliskit.ai.knowledge.ingested", "{chunk}", "Knowledge chunks embedded and stored.");

        /// <summary>
        /// Limit hits by kind (daily, rate, conversation_full, budget).
        /// </summary>
        public static readonly Counter<long> LimitHits = TrellisKitTelemetry.Meter.CreateCounter<long>("trelliskit.ai.limit_hits", "{hit}", "Member limit hits.");

        /// <summary>
        /// Secret reveals.
        /// </summary>
        public static readonly Counter<long> SecretReveals = TrellisKitTelemetry.Meter.CreateCounter<long>("trelliskit.ai.secret_reveals", "{reveal}", "Endpoint secret reveals.");

        /// <summary>
        /// Administrator conversation reads.
        /// </summary>
        public static readonly Counter<long> AdminConversationReads = TrellisKitTelemetry.Meter.CreateCounter<long>("trelliskit.ai.admin_conversation_reads", "{read}", "Conversations opened by administrators.");

        /// <summary>
        /// Assistant tool calls by tool (registry name) and status (succeeded, failed, timedout, rejected).
        /// </summary>
        public static readonly Counter<long> ToolCalls = TrellisKitTelemetry.Meter.CreateCounter<long>("trelliskit.ai.tool_calls", "{call}", "Assistant tool calls.");

        /// <summary>
        /// Assistant tool call duration by tool and status.
        /// </summary>
        public static readonly Histogram<double> ToolCallDuration = TrellisKitTelemetry.Meter.CreateHistogram<double>("trelliskit.ai.tool_call.duration", "s", "Assistant tool call duration.");

        /// <summary>
        /// Characters of tool output handed to the model, by tool.
        /// </summary>
        public static readonly Histogram<double> ToolResultSize = TrellisKitTelemetry.Meter.CreateHistogram<double>("trelliskit.ai.tool_result.characters", "{character}", "Assistant tool output size.");

        /// <summary>
        /// Model rounds of one reply that used tools, by outcome (answered, limit).
        /// </summary>
        public static readonly Counter<long> ToolRounds = TrellisKitTelemetry.Meter.CreateCounter<long>("trelliskit.ai.tool_rounds", "{round}", "Model rounds that requested tools.");

        /// <summary>
        /// Budget alerts by scope and level.
        /// </summary>
        public static readonly Counter<long> BudgetAlerts = TrellisKitTelemetry.Meter.CreateCounter<long>("trelliskit.ai.budget_alerts", "{alert}", "Budget alert emails.");

        /// <summary>
        /// Open SSE streams.
        /// </summary>
        public static readonly ObservableGauge<long> OpenStreams = TrellisKitTelemetry.Meter.CreateObservableGauge<long>("trelliskit.ai.sse.streams", () => Interlocked.Read(ref _OpenStreams), "{stream}", "Open SSE streams.");

        /// <summary>
        /// Endpoints per health status.
        /// </summary>
        public static readonly ObservableGauge<long> EndpointHealth = TrellisKitTelemetry.Meter.CreateObservableGauge<long>("trelliskit.ai.endpoint.health", ObserveHealth, "{endpoint}", "Enabled endpoints per health status.");

        #endregion

        #region Private-Members

        private static long _OpenStreams = 0;
        private static long _Healthy = 0;
        private static long _Unhealthy = 0;
        private static long _Unknown = 0;

        #endregion

        #region Public-Methods

        /// <summary>
        /// Start a span on the engine activity source.
        /// </summary>
        /// <param name="name">Span name, for example ai.chat.</param>
        /// <returns>Activity, or null when nothing listens.</returns>
        public static Activity? Span(string name)
        {
            return TrellisKitTelemetry.ActivitySource.StartActivity(name, ActivityKind.Internal);
        }

        /// <summary>
        /// Record a stream opening (+1) or closing (-1).
        /// </summary>
        /// <param name="delta">+1 or -1.</param>
        public static void StreamDelta(int delta)
        {
            Interlocked.Add(ref _OpenStreams, delta);
        }

        /// <summary>
        /// Publish the latest endpoint counts per health status (summed across properties by the caller).
        /// </summary>
        /// <param name="healthy">Healthy endpoints.</param>
        /// <param name="unhealthy">Unhealthy endpoints.</param>
        /// <param name="unknown">Unknown endpoints.</param>
        public static void SetHealthCounts(long healthy, long unhealthy, long unknown)
        {
            Interlocked.Exchange(ref _Healthy, Math.Max(0, healthy));
            Interlocked.Exchange(ref _Unhealthy, Math.Max(0, unhealthy));
            Interlocked.Exchange(ref _Unknown, Math.Max(0, unknown));
        }

        /// <summary>
        /// Lowercase snake form of an enum or name for metric labels, for example OpenAICompatible becomes
        /// openaicompatible.
        /// </summary>
        /// <param name="value">Value.</param>
        /// <returns>Label.</returns>
        public static string Label(string? value)
        {
            return String.IsNullOrEmpty(value) ? "none" : value.ToLowerInvariant();
        }

        #endregion

        #region Private-Methods

        private static Measurement<long>[] ObserveHealth()
        {
            return new[]
            {
                new Measurement<long>(Interlocked.Read(ref _Healthy), new System.Collections.Generic.KeyValuePair<string, object?>("status", "healthy")),
                new Measurement<long>(Interlocked.Read(ref _Unhealthy), new System.Collections.Generic.KeyValuePair<string, object?>("status", "unhealthy")),
                new Measurement<long>(Interlocked.Read(ref _Unknown), new System.Collections.Generic.KeyValuePair<string, object?>("status", "unknown"))
            };
        }

        #endregion
    }
}
