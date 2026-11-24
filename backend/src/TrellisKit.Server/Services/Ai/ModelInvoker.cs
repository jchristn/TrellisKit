namespace TrellisKit.Server.Services.Ai
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using PolyPrompt.Clients;
    using PolyPrompt.Models;
    using PolyPrompt.Options;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Exceptions;
    using TrellisKit.Core.Integrations.Interfaces;
    using TrellisKit.Core.Integrations.Models;
    using TrellisKit.Core.Models;
    using TrellisKit.Core.Observability;
    using TrellisKit.Core.Settings;

    /// <summary>
    /// Every model call of the AI feature goes through here (docs/AI_CONTRACT.md sections 3.4 to 3.6 and 16): endpoint
    /// concurrency and queueing, retries with backoff for 408, 429, 5xx, connection failures, and first-token timeouts
    /// (never after the first streamed token), the chat fallback endpoint, the first-token timeout, cost from the
    /// endpoint's admin-entered prices, usage rollups, metrics, and spans. Provider errors are sanitized by the
    /// endpoint's redactor before they are logged or returned.
    /// Thread safety: safe to call concurrently.
    /// </summary>
    public class ModelInvoker
    {
        /// <summary>
        /// Returns the scope guard (docs/INTEGRATION.md owner requirement R7) that leads the system message of every call,
        /// or null for none. Set by the AI services; every completion, tool round, and classifier call carries it.
        /// </summary>
        public Func<CancellationToken, Task<string?>>? GuardProvider { get; set; } = null;

        #region Private-Members

        private static readonly int[] _BackoffMs = new[] { 500, 2000, 4000, 8000, 8000 };
        private readonly IModelClientFactory _Factory;
        private readonly EndpointConcurrencyGate _Gate;
        private readonly AiUsageService _Usage;
        private readonly AiSettings _Settings;
        private readonly AppLogger _Logger;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the invoker.
        /// </summary>
        /// <param name="factory">Client factory.</param>
        /// <param name="gate">Concurrency gate.</param>
        /// <param name="usage">Usage recorder.</param>
        /// <param name="settings">AI settings (read live).</param>
        /// <param name="logger">Logger.</param>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        public ModelInvoker(IModelClientFactory factory, EndpointConcurrencyGate gate, AiUsageService usage, AiSettings settings, AppLogger logger)
        {
            _Factory = factory ?? throw new ArgumentNullException(nameof(factory));
            _Gate = gate ?? throw new ArgumentNullException(nameof(gate));
            _Usage = usage ?? throw new ArgumentNullException(nameof(usage));
            _Settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _Logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Whether an endpoint may be routed to: enabled, and not Unhealthy (endpoints without health checks are treated
        /// as healthy).
        /// </summary>
        /// <param name="endpoint">Endpoint, or null.</param>
        /// <returns>True when usable.</returns>
        public static bool IsRoutable(ModelEndpoint? endpoint)
        {
            if (endpoint == null || !endpoint.Enabled || endpoint.DeletedUtc != null) return false;
            return !endpoint.HealthCheckEnabled || endpoint.HealthStatus != ModelHealthStatusEnum.Unhealthy;
        }

        /// <summary>
        /// Cost of a call in millionths of a US dollar from the endpoint's prices per million tokens.
        /// </summary>
        /// <param name="endpoint">Endpoint.</param>
        /// <param name="promptTokens">Prompt tokens.</param>
        /// <param name="completionTokens">Completion tokens (reasoning tokens are billed as output by providers and included by the caller).</param>
        /// <returns>Cost.</returns>
        public static long Cost(ModelEndpoint? endpoint, int promptTokens, int completionTokens)
        {
            return Cost(endpoint, promptTokens, completionTokens, null, null);
        }

        /// <summary>
        /// Cost of a call in millionths of a US dollar, with prompt cache tokens priced separately when the endpoint has
        /// cache prices (docs/AI_TELEMETRY.md, "Cost"). Cache read tokens are billed at the endpoint's cache read price
        /// and cache creation tokens at its cache write price; each falls back to the input price. Anthropic and Bedrock
        /// report cached tokens in addition to the prompt tokens, so they are added; every other provider counts them
        /// inside the prompt tokens, so they are taken out of the part billed at the input price. An endpoint without
        /// prices costs 0.
        /// </summary>
        /// <param name="endpoint">Endpoint, or null (cost 0).</param>
        /// <param name="promptTokens">Prompt tokens as the provider reported them.</param>
        /// <param name="completionTokens">Completion tokens (reasoning tokens included by the caller).</param>
        /// <param name="cachedReadTokens">Cache read tokens, or null.</param>
        /// <param name="cacheCreationTokens">Cache creation tokens, or null.</param>
        /// <returns>Cost, 0 or more.</returns>
        public static long Cost(ModelEndpoint? endpoint, int promptTokens, int completionTokens, int? cachedReadTokens, int? cacheCreationTokens)
        {
            if (endpoint == null) return 0;
            double input = endpoint.InputPricePerMillion ?? 0;
            int read = Math.Max(0, cachedReadTokens ?? 0);
            int written = Math.Max(0, cacheCreationTokens ?? 0);
            bool additive = endpoint.ApiType == ModelApiTypeEnum.Anthropic || endpoint.ApiType == ModelApiTypeEnum.Bedrock;
            int uncached = additive ? Math.Max(0, promptTokens) : Math.Max(0, promptTokens - read - written);
            double micros = uncached * input + read * (endpoint.CacheReadPricePerMillion ?? input) + written * (endpoint.CacheWritePricePerMillion ?? input) +
                Math.Max(0, completionTokens) * (endpoint.OutputPricePerMillion ?? 0);
            return (long)Math.Round(micros, MidpointRounding.AwayFromZero);
        }

        /// <summary>
        /// Run a multi-turn completion (ToolChatStreamingAsync or ToolChatAsync with no tools), with fallback.
        /// </summary>
        /// <param name="call">Call.</param>
        /// <param name="onText">Receives text deltas in order (streaming), or the whole text once (non-streaming), or null.</param>
        /// <param name="onReasoning">Receives reasoning deltas, or null.</param>
        /// <param name="token">Cancellation token (the explicit cancel route).</param>
        /// <returns>Outcome; never throws for provider failures.</returns>
        /// <exception cref="ArgumentNullException">The call is null.</exception>
        /// <exception cref="OperationCanceledException">The token was cancelled.</exception>
        public async Task<CompletionOutcome> CompleteAsync(CompletionCall call, Func<string, Task>? onText, Func<string, Task>? onReasoning, CancellationToken token)
        {
            ArgumentNullException.ThrowIfNull(call);
            Stopwatch total = Stopwatch.StartNew();
            List<ModelEndpoint> candidates = new List<ModelEndpoint> { call.Endpoint };
            if (call.Fallback != null && call.Fallback.Id != call.Endpoint.Id) candidates.Add(call.Fallback);
            CompletionOutcome last = new CompletionOutcome { ErrorCode = "ModelUnavailable", Endpoint = call.Endpoint };
            for (int i = 0; i < candidates.Count; i++)
            {
                ModelEndpoint endpoint = candidates[i];
                bool fallback = i > 0;
                if (!IsRoutable(endpoint))
                {
                    last = new CompletionOutcome { ErrorCode = "ModelUnavailable", Endpoint = endpoint, UsedFallback = fallback };
                    await RecordAsync(call, endpoint, last, fallback, AiUsageOutcomeEnum.Unavailable).ConfigureAwait(false);
                    continue;
                }
                last = await AttemptAsync(call, endpoint, fallback, onText, onReasoning, token).ConfigureAwait(false);
                if (last.Success || last.StreamedAny || last.ErrorCode == "Cancelled") break;
            }
            last.LatencyMs = total.ElapsedMilliseconds;
            return last;
        }

        /// <summary>
        /// Single-prompt completion (titles, query rewriting, moderation classification) with ChatAsync.
        /// </summary>
        /// <param name="endpoint">Completion endpoint.</param>
        /// <param name="prompt">User prompt.</param>
        /// <param name="systemPrompt">System prompt, or null.</param>
        /// <param name="maxTokens">Output cap.</param>
        /// <param name="temperature">Temperature, or null.</param>
        /// <param name="purpose">Purpose.</param>
        /// <param name="assistantId">Assistant, or null.</param>
        /// <param name="isTest">Test traffic.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Outcome.</returns>
        /// <exception cref="ArgumentNullException">The endpoint or prompt is null.</exception>
        public Task<CompletionOutcome> PromptAsync(ModelEndpoint endpoint, string prompt, string? systemPrompt, int maxTokens, double? temperature, AssistantPurposeEnum purpose, string? assistantId, bool isTest, CancellationToken token)
        {
            ArgumentNullException.ThrowIfNull(endpoint);
            ArgumentNullException.ThrowIfNull(prompt);
            List<ChatMessage> messages = new List<ChatMessage>();
            if (!String.IsNullOrWhiteSpace(systemPrompt)) messages.Add(ChatMessage.System(systemPrompt));
            messages.Add(ChatMessage.User(prompt));
            CompletionCall call = new CompletionCall
            {
                Endpoint = endpoint,
                Messages = messages,
                MaxTokens = maxTokens,
                Temperature = temperature,
                Stream = false,
                Purpose = purpose,
                AssistantId = assistantId,
                IsTest = isTest
            };
            return CompleteAsync(call, null, null, token);
        }

        /// <summary>
        /// Embed inputs in batches of ai.embeddingBatchSize.
        /// </summary>
        /// <param name="endpoint">Embedding endpoint.</param>
        /// <param name="inputs">Inputs.</param>
        /// <param name="assistantId">Assistant, or null.</param>
        /// <param name="isTest">Test traffic.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>One vector per input, in order.</returns>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        /// <exception cref="ApiErrorException">503 ModelUnavailable, 503 ModelBusy, or 502 ModelError.</exception>
        public async Task<List<float[]>> EmbedAsync(ModelEndpoint endpoint, List<string> inputs, string? assistantId, bool isTest, CancellationToken token)
        {
            ArgumentNullException.ThrowIfNull(endpoint);
            ArgumentNullException.ThrowIfNull(inputs);
            if (!IsRoutable(endpoint)) throw AiErrors.ModelUnavailable();
            List<float[]> vectors = new List<float[]>();
            int batchSize = Math.Max(1, _Settings.EmbeddingBatchSize);
            for (int offset = 0; offset < inputs.Count; offset += batchSize)
            {
                List<string> batch = inputs.Skip(offset).Take(batchSize).ToList();
                Stopwatch queued = Stopwatch.StartNew();
                EmbeddingResponse? response = null;
                Exception? failure = null;
                using (IDisposable slot = await _Gate.EnterAsync(endpoint, token).ConfigureAwait(false))
                using (ModelClientLease<ProviderClientBase> lease = _Factory.CreateEmbedding(endpoint))
                using (Activity? span = Span("ai.embedding", endpoint, AssistantPurposeEnum.Embedding, assistantId))
                using (ModelCallProbe probe = ModelCallProbe.Begin())
                {
                    long queueMs = queued.ElapsedMilliseconds;
                    Stopwatch watch = Stopwatch.StartNew();
                    try
                    {
                        EmbeddingOptions options = EmbeddingOptionsFor(endpoint);
                        response = await lease.Client.EmbedAsync(batch, options, token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (token.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception e)
                    {
                        failure = e;
                    }
                    bool ok = failure == null && response != null && response.Success && response.Embeddings.Count == batch.Count;
                    int promptTokens = AiCallDetails.EmbeddingTokens(response) ?? 0;
                    AiUsageRecord record = new AiUsageRecord
                    {
                        AssistantId = assistantId,
                        EndpointId = endpoint.Id,
                        Purpose = AssistantPurposeEnum.Embedding,
                        Outcome = ok ? AiUsageOutcomeEnum.Ok : AiUsageOutcomeEnum.Error,
                        IsTest = isTest,
                        PromptTokens = promptTokens,
                        LatencyMs = watch.ElapsedMilliseconds,
                        CostMicros = Cost(endpoint, promptTokens, 0, response?.Usage?.CachedPromptTokens, response?.Usage?.CacheCreationTokens),
                        OccurredUtc = DateTime.UtcNow,
                        Detail = AiCallDetails.ForResponse(response, response?.Usage, probe, queueMs, false)
                    };
                    await _Usage.RecordAsync(record, endpoint, false, null, ok ? null : "ModelError").ConfigureAwait(false);
                    if (!ok)
                    {
                        string error = lease.Redactor.Redact(failure?.Message ?? response?.Error ?? "Embedding failed.") ?? String.Empty;
                        _Logger.Warn("[ModelInvoker] embedding failed on endpoint " + endpoint.Id + " (" + endpoint.Name + "): " + error);
                        span?.SetStatus(ActivityStatusCode.Error);
                        throw AiErrors.ModelError();
                    }
                    foreach (EmbeddingResult result in response!.Embeddings.OrderBy(e => e.Index)) vectors.Add(result.Embedding);
                }
            }
            return vectors;
        }

        /// <summary>
        /// Rerank documents.
        /// </summary>
        /// <param name="endpoint">Rerank endpoint.</param>
        /// <param name="query">Query.</param>
        /// <param name="documents">Documents.</param>
        /// <param name="assistantId">Assistant, or null.</param>
        /// <param name="isTest">Test traffic.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Results, highest score first.</returns>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        /// <exception cref="ApiErrorException">503 ModelUnavailable, 503 ModelBusy, or 502 ModelError.</exception>
        public async Task<List<RerankResult>> RerankAsync(ModelEndpoint endpoint, string query, List<string> documents, string? assistantId, bool isTest, CancellationToken token)
        {
            ArgumentNullException.ThrowIfNull(endpoint);
            ArgumentNullException.ThrowIfNull(query);
            ArgumentNullException.ThrowIfNull(documents);
            if (documents.Count == 0) return new List<RerankResult>();
            if (!IsRoutable(endpoint)) throw AiErrors.ModelUnavailable();
            Stopwatch queued = Stopwatch.StartNew();
            using (IDisposable slot = await _Gate.EnterAsync(endpoint, token).ConfigureAwait(false))
            using (ModelClientLease<ProviderClientBase> lease = _Factory.CreateRerank(endpoint))
            using (Activity? span = Span("ai.rerank", endpoint, AssistantPurposeEnum.Rerank, assistantId))
            using (ModelCallProbe probe = ModelCallProbe.Begin())
            {
                long queueMs = queued.ElapsedMilliseconds;
                Stopwatch watch = Stopwatch.StartNew();
                RerankResponse? response = null;
                Exception? failure = null;
                try
                {
                    response = await lease.Client.RerankAsync(query, documents, null, token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception e)
                {
                    failure = e;
                }
                bool ok = failure == null && response != null && response.Success;
                AiCallDetail rerankDetail = AiCallDetails.ForResponse(response, null, probe, queueMs, false);
                rerankDetail.ResponseId = response?.ResponseId;
                rerankDetail.TotalTokens = response?.TotalTokens;
                await _Usage.RecordAsync(new AiUsageRecord
                {
                    AssistantId = assistantId,
                    EndpointId = endpoint.Id,
                    Purpose = AssistantPurposeEnum.Rerank,
                    Outcome = ok ? AiUsageOutcomeEnum.Ok : AiUsageOutcomeEnum.Error,
                    IsTest = isTest,
                    PromptTokens = response?.TotalTokens ?? 0,
                    LatencyMs = watch.ElapsedMilliseconds,
                    CostMicros = Cost(endpoint, response?.TotalTokens ?? 0, 0),
                    OccurredUtc = DateTime.UtcNow,
                    Detail = rerankDetail
                }, endpoint, false, null, ok ? null : "ModelError").ConfigureAwait(false);
                if (!ok)
                {
                    _Logger.Warn("[ModelInvoker] rerank failed on endpoint " + endpoint.Id + ": " + lease.Redactor.Redact(failure?.Message ?? response?.Error));
                    throw AiErrors.ModelError();
                }
                return response!.Results.OrderByDescending(r => r.Score).ToList();
            }
        }

        /// <summary>
        /// Completion options for an endpoint and call (Ollama gets the context length).
        /// </summary>
        /// <param name="endpoint">Endpoint.</param>
        /// <param name="maxTokens">Output cap, or null for the endpoint's.</param>
        /// <param name="temperature">Temperature override.</param>
        /// <param name="topP">Top p override.</param>
        /// <param name="defaultContextWindow">ai.defaultContextWindow.</param>
        /// <returns>Options.</returns>
        public static CompletionOptions OptionsFor(ModelEndpoint endpoint, int? maxTokens, double? temperature, double? topP, int defaultContextWindow)
        {
            return OptionsFor(endpoint, maxTokens, temperature, topP, defaultContextWindow, null);
        }

        /// <summary>
        /// Completion options for an endpoint with an optional reasoning effort that overrides the endpoint's. Anthropic
        /// requests never carry temperature or top_p (CLAUDE.md decisions).
        /// </summary>
        /// <param name="endpoint">Endpoint.</param>
        /// <param name="maxTokens">Output cap, or null.</param>
        /// <param name="temperature">Temperature override.</param>
        /// <param name="topP">Top p override.</param>
        /// <param name="defaultContextWindow">ai.defaultContextWindow.</param>
        /// <param name="effort">Reasoning effort, or null for the endpoint's.</param>
        /// <returns>Options.</returns>
        /// <exception cref="ArgumentNullException">endpoint is null.</exception>
        public static CompletionOptions OptionsFor(ModelEndpoint endpoint, int? maxTokens, double? temperature, double? topP, int defaultContextWindow, ModelReasoningEffortEnum? effort)
        {
            ArgumentNullException.ThrowIfNull(endpoint);
            CompletionOptions options;
            if (endpoint.ApiType == ModelApiTypeEnum.Ollama || endpoint.ApiType == ModelApiTypeEnum.OllamaCloud)
                options = new OllamaCompletionOptions { ContextLength = endpoint.ContextWindow ?? defaultContextWindow };
            else
                options = new CompletionOptions();
            options.Model = endpoint.CallModel;
            int? cap = endpoint.MaxTokens;
            if (maxTokens != null) cap = cap == null ? maxTokens : Math.Min(cap.Value, maxTokens.Value);
            if (cap != null) options.MaxTokens = cap;
            bool anthropic = endpoint.ApiType == ModelApiTypeEnum.Anthropic;
            double? t = temperature ?? endpoint.Temperature;
            if (t != null && !anthropic) options.Temperature = t;
            double? p = topP ?? endpoint.TopP;
            if (p != null && !anthropic) options.TopP = p;
            ModelReasoningEffortEnum? level = effort ?? endpoint.ReasoningEffort;
            if (level != null) options.ReasoningEffort = new ReasoningEffort((ReasoningEffortLevel)Enum.Parse(typeof(ReasoningEffortLevel), level.Value.ToString()));
            return options;
        }

        #endregion

        #region Private-Methods

        private async Task<CompletionOutcome> AttemptAsync(CompletionCall call, ModelEndpoint endpoint, bool fallback, Func<string, Task>? onText, Func<string, Task>? onReasoning, CancellationToken token)
        {
            int maxRetries = Math.Clamp(endpoint.MaxRetries, 0, 5);
            CompletionOutcome outcome = new CompletionOutcome { Endpoint = endpoint, UsedFallback = fallback };
            for (int attempt = 0; attempt <= maxRetries; attempt++)
            {
                IDisposable slot;
                Stopwatch queued = Stopwatch.StartNew();
                try
                {
                    slot = await _Gate.EnterAsync(endpoint, token).ConfigureAwait(false);
                }
                catch (ApiErrorException e) when (e.Error == "ModelBusy")
                {
                    outcome = new CompletionOutcome { Endpoint = endpoint, UsedFallback = fallback, ErrorCode = "ModelBusy", Retries = attempt };
                    outcome.Detail.QueueMs = queued.ElapsedMilliseconds;
                    await RecordAsync(call, endpoint, outcome, fallback, AiUsageOutcomeEnum.Busy).ConfigureAwait(false);
                    return outcome;
                }
                long queueMs = queued.ElapsedMilliseconds;
                using (slot)
                {
                    outcome = await CallOnceAsync(call, endpoint, fallback, onText, onReasoning, null, token).ConfigureAwait(false);
                    if (IsToolRejection(call, endpoint, outcome))
                    {
                        // docs/AI_CONTRACT.md section 8.3: a Native endpoint whose model refuses tools (HTTP 400 naming
                        // tools, before any text) is retried once with the prompt protocol instead of failing the reply.
                        // The refused call is a model interaction too, so it gets its own ledger row.
                        _Logger.Warn("[ModelInvoker] endpoint " + endpoint.Id + " (" + endpoint.Name + ") rejected native tools; retrying with the prompt tool protocol");
                        outcome.Retries = attempt;
                        outcome.Detail.QueueMs = queueMs;
                        await RecordAsync(call, endpoint, outcome, fallback, AiUsageOutcomeEnum.Error, "native").ConfigureAwait(false);
                        outcome = await CallOnceAsync(call, endpoint, fallback, onText, onReasoning, ModelToolCallingEnum.Prompt, token).ConfigureAwait(false);
                    }
                }
                outcome.Retries = attempt;
                outcome.Detail.QueueMs = queueMs;
                AiUsageOutcomeEnum kind = outcome.Success ? AiUsageOutcomeEnum.Ok
                    : outcome.ErrorCode == "ModelTimeout" ? AiUsageOutcomeEnum.Timeout
                    : outcome.ErrorCode == "Cancelled" ? AiUsageOutcomeEnum.Cancelled
                    : AiUsageOutcomeEnum.Error;
                await RecordAsync(call, endpoint, outcome, fallback, kind).ConfigureAwait(false);
                if (outcome.Success || outcome.StreamedAny || outcome.ErrorCode == "Cancelled" || !IsRetryable(outcome) || attempt == maxRetries) return outcome;
                string reason = outcome.ErrorCode == "ModelTimeout" ? "first_token_timeout" : outcome.StatusCode == 429 ? "status_429" : outcome.StatusCode >= 500 ? "status_5xx" : "network";
                TrellisKitTelemetry.Add(AiTelemetry.Retries, 1, "api_type", AiTelemetry.Label(endpoint.ApiType.ToString()), "reason", reason);
                int delay = _BackoffMs[Math.Min(attempt, _BackoffMs.Length - 1)] + Random.Shared.Next(0, 250);
                await Task.Delay(delay, token).ConfigureAwait(false);
            }
            return outcome;
        }

        private async Task<CompletionOutcome> CallOnceAsync(CompletionCall call, ModelEndpoint endpoint, bool fallback, Func<string, Task>? onText, Func<string, Task>? onReasoning, ModelToolCallingEnum? modeOverride, CancellationToken token)
        {
            ModelToolCallingEnum mode = modeOverride ?? endpoint.ToolCalling;
            CompletionOutcome outcome = new CompletionOutcome { Endpoint = endpoint, UsedFallback = fallback, ToolMode = mode };
            Stopwatch watch = Stopwatch.StartNew();
            bool offer = call.Tools.Count > 0 && mode != ModelToolCallingEnum.Off;
            bool promptOffer = offer && mode == ModelToolCallingEnum.Prompt;
            List<ChatMessage> messages = call.Messages;
            if (GuardProvider != null) messages = ScopeGuard.Apply(messages, await GuardProvider(token).ConfigureAwait(false));
            bool toolHistory = messages.Any(m => String.Equals(m.Role, "tool", StringComparison.OrdinalIgnoreCase) || (m.ToolCalls != null && m.ToolCalls.Count > 0));
            // Tool history goes out natively only while tools are offered to a Native endpoint. Otherwise (Prompt and Off
            // endpoints, and the final round without tools) it is rewritten as plain messages, because providers such as
            // Bedrock and Anthropic reject tool blocks in a request that defines no tools.
            if (promptOffer || (toolHistory && (!offer || mode != ModelToolCallingEnum.Native))) messages = ToolPromptProtocol.Rewrite(messages, promptOffer ? call.Tools : new List<ToolDefinition>());
            // A prompt-protocol round is read whole: its text may be a tool request, which must never reach the member.
            bool stream = call.Stream && endpoint.Streaming && !promptOffer;
            Func<string, Task>? emit = promptOffer ? null : onText;
            using (ModelClientLease<ProviderClientBase> lease = _Factory.CreateCompletion(endpoint))
            using (Activity? span = Span("ai.completion", endpoint, call.Purpose, call.AssistantId))
            using (CancellationTokenSource firstToken = new CancellationTokenSource())
            using (CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(token, firstToken.Token))
            using (ModelCallProbe probe = ModelCallProbe.Begin())
            {
                ChatRequest request = new ChatRequest
                {
                    Messages = messages,
                    Tools = offer && mode == ModelToolCallingEnum.Native ? call.Tools : new List<ToolDefinition>(),
                    ToolChoice = offer && mode == ModelToolCallingEnum.Native ? "auto" : null,
                    Options = OptionsFor(endpoint, call.MaxTokens, call.Temperature, call.TopP, _Settings.DefaultContextWindow, call.ReasoningEffort)
                };
                bool firstSeen = false;
                if (stream && endpoint.FirstResponseTimeoutMs != null) firstToken.CancelAfter(endpoint.FirstResponseTimeoutMs.Value);
                try
                {
                    if (stream)
                    {
                        ChatStreamingResponse response = await lease.Client.ChatStreamingAsync(request, linked.Token).ConfigureAwait(false);
                        outcome.StatusCode = response.StatusCode;
                        outcome.Detail.ReasoningEffort = response.AppliedReasoningEffort;
                        if (!response.Success)
                        {
                            outcome.ErrorCode = "ModelError";
                            outcome.Error = lease.Redactor.Redact(response.Error);
                        }
                        else
                        {
                            System.Text.StringBuilder text = new System.Text.StringBuilder();
                            System.Text.StringBuilder reasoning = new System.Text.StringBuilder();
                            await foreach (ChatStreamingChunk chunk in response.Chunks.WithCancellation(linked.Token).ConfigureAwait(false))
                            {
                                if (!firstSeen && (!String.IsNullOrEmpty(chunk.Text) || !String.IsNullOrEmpty(chunk.ReasoningText)))
                                {
                                    firstSeen = true;
                                    firstToken.CancelAfter(Timeout.Infinite);
                                    outcome.TimeToFirstTokenMs = watch.ElapsedMilliseconds;
                                }
                                if (!String.IsNullOrEmpty(chunk.ReasoningText))
                                {
                                    reasoning.Append(chunk.ReasoningText);
                                    if (onReasoning != null) await onReasoning(chunk.ReasoningText).ConfigureAwait(false);
                                }
                                if (!String.IsNullOrEmpty(chunk.Text))
                                {
                                    text.Append(chunk.Text);
                                    outcome.StreamedAny = true;
                                    if (emit != null) await emit(chunk.Text).ConfigureAwait(false);
                                }
                            }
                            outcome.Text = text.ToString();
                            outcome.Reasoning = reasoning.Length == 0 ? null : reasoning.ToString();
                            outcome.FinishReason = response.FinishReason;
                            outcome.TimeToLastTokenMs = response.TimeToLastTokenMs >= 0 ? response.TimeToLastTokenMs : watch.ElapsedMilliseconds;
                            outcome.TokensPerSecond = response.OverallTokensPerSecond > 0 ? response.OverallTokensPerSecond : null;
                            ApplyUsage(outcome, response.Usage, outcome.Text);
                            outcome.Detail.Chunks = response.ChunkCount;
                            outcome.Detail.ResponseId = response.ResponseId;
                            outcome.Detail.ProviderModel = response.Model;
                            outcome.Detail.ReasoningEffort = response.AppliedReasoningEffort;
                            if (offer && mode == ModelToolCallingEnum.Native && response.ToolCalls.Count > 0)
                            {
                                outcome.ToolCalls = response.ToolCalls.ToList();
                                outcome.AssistantMessage = response.ToAssistantMessage();
                            }
                            if (response.State == StreamingState.Failed)
                            {
                                outcome.ErrorCode = "ModelError";
                                outcome.Error = lease.Redactor.Redact(response.StreamError ?? response.Error);
                            }
                            else
                            {
                                outcome.Success = true;
                            }
                        }
                    }
                    else
                    {
                        ChatResponse response = await lease.Client.ChatAsync(request, linked.Token).ConfigureAwait(false);
                        outcome.StatusCode = response.StatusCode;
                        outcome.Detail.ReasoningEffort = response.AppliedReasoningEffort;
                        if (!response.Success)
                        {
                            outcome.ErrorCode = "ModelError";
                            outcome.Error = lease.Redactor.Redact(response.Error);
                        }
                        else
                        {
                            outcome.Success = true;
                            outcome.Text = response.Text ?? String.Empty;
                            outcome.Reasoning = response.Reasoning;
                            outcome.FinishReason = response.FinishReason;
                            outcome.TimeToLastTokenMs = watch.ElapsedMilliseconds;
                            ApplyUsage(outcome, response.Usage, outcome.Text);
                            outcome.Detail.ResponseId = response.ResponseId;
                            outcome.Detail.ProviderModel = response.Model;
                            if (offer && mode == ModelToolCallingEnum.Native && response.ToolCalls != null && response.ToolCalls.Count > 0)
                            {
                                outcome.ToolCalls = response.ToolCalls.ToList();
                                outcome.AssistantMessage = response.ToAssistantMessage();
                            }
                            else if (promptOffer && ToolPromptProtocol.TryParse(outcome.Text, out List<ToolCall> requested))
                            {
                                outcome.ToolCalls = requested;
                                outcome.AssistantMessage = ChatMessage.AssistantToolCalls(requested);
                                outcome.Text = String.Empty;
                            }
                            if (onReasoning != null && !String.IsNullOrEmpty(outcome.Reasoning)) await onReasoning(outcome.Reasoning).ConfigureAwait(false);
                            if (onText != null && outcome.Text.Length > 0)
                            {
                                outcome.StreamedAny = true;
                                await onText(outcome.Text).ConfigureAwait(false);
                            }
                        }
                    }
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    outcome.ErrorCode = "Cancelled";
                }
                catch (OperationCanceledException) when (firstToken.IsCancellationRequested)
                {
                    outcome.ErrorCode = "ModelTimeout";
                    outcome.Error = "No first token within " + endpoint.FirstResponseTimeoutMs + " ms.";
                }
                catch (TimeoutException e)
                {
                    outcome.ErrorCode = "ModelTimeout";
                    outcome.Error = lease.Redactor.Redact(e.Message);
                }
                catch (Exception e) when (e is not OutOfMemoryException)
                {
                    outcome.ErrorCode = e is OperationCanceledException ? "ModelTimeout" : "ModelError";
                    outcome.Error = lease.Redactor.Redact(e.Message);
                }
                if (!outcome.Success && outcome.ErrorCode == "ModelError" && outcome.Error != null && outcome.Error.Contains("timed out", StringComparison.OrdinalIgnoreCase)) outcome.ErrorCode = "ModelTimeout";
                outcome.LatencyMs = watch.ElapsedMilliseconds;
                outcome.CostMicros = Cost(endpoint, outcome.PromptTokens, outcome.CompletionTokens + outcome.ReasoningTokens, outcome.Detail.CachedReadTokens, outcome.Detail.CacheCreationTokens);
                AiCallDetail detail = outcome.Detail;
                detail.Streamed = stream;
                detail.StatusCode = outcome.StatusCode;
                detail.FinishReason = outcome.FinishReason;
                detail.TimeToFirstTokenMs = outcome.TimeToFirstTokenMs;
                detail.TimeToLastTokenMs = outcome.TimeToLastTokenMs;
                if (stream && outcome.TimeToFirstTokenMs != null && outcome.TimeToLastTokenMs != null) detail.StreamingMs = Math.Max(0, outcome.TimeToLastTokenMs.Value - outcome.TimeToFirstTokenMs.Value);
                detail.TokensPerSecond = outcome.TokensPerSecond ?? AiCallDetails.TokensPerSecond(outcome.CompletionTokens + outcome.ReasoningTokens, detail.EvalMs, outcome.Success ? outcome.LatencyMs : null);
                if (offer) detail.ToolCalls = outcome.ToolCalls.Count;
                AiCallDetails.ApplyProbe(detail, probe);
                if (span != null)
                {
                    span.SetTag("gen_ai.usage.input_tokens", outcome.PromptTokens);
                    span.SetTag("gen_ai.usage.output_tokens", outcome.CompletionTokens);
                    span.SetTag("ai.fallback", fallback);
                    if (!outcome.Success) span.SetStatus(ActivityStatusCode.Error, outcome.ErrorCode);
                }
                if (!outcome.Success && outcome.ErrorCode != "Cancelled")
                    _Logger.Warn("[ModelInvoker] " + call.Purpose + " call failed on endpoint " + endpoint.Id + " (" + endpoint.Name + "): " + outcome.ErrorCode + " " + outcome.StatusCode + " " + outcome.Error);
            }
            return outcome;
        }

        private static bool IsToolRejection(CompletionCall call, ModelEndpoint endpoint, CompletionOutcome outcome)
        {
            if (outcome.Success || outcome.StreamedAny || call.Tools.Count == 0 || endpoint.ToolCalling != ModelToolCallingEnum.Native || outcome.ToolMode != ModelToolCallingEnum.Native) return false;
            if (outcome.StatusCode != 400) return false;
            return outcome.Error != null && outcome.Error.Contains("tool", StringComparison.OrdinalIgnoreCase);
        }

        private static void ApplyUsage(CompletionOutcome outcome, TokenUsage? usage, string text)
        {
            outcome.PromptTokens = usage?.PromptTokens ?? 0;
            outcome.CompletionTokens = usage?.CompletionTokens ?? (text.Length + 3) / 4;
            outcome.ReasoningTokens = usage?.ReasoningTokens ?? 0;
            AiCallDetails.ApplyUsage(outcome.Detail, usage);
        }

        private static bool IsRetryable(CompletionOutcome outcome)
        {
            if (outcome.ErrorCode == "ModelTimeout") return true;
            if (outcome.StatusCode == null || outcome.StatusCode == 0) return outcome.ErrorCode == "ModelError";
            int status = outcome.StatusCode.Value;
            return status == 408 || status == 429 || status == 500 || status == 502 || status == 503 || status == 504;
        }

        private async Task RecordAsync(CompletionCall call, ModelEndpoint endpoint, CompletionOutcome outcome, bool fallback, AiUsageOutcomeEnum kind, string? guardSuffix = null)
        {
            outcome.Detail.Retries = outcome.Retries;
            outcome.Detail.StatusCode ??= outcome.StatusCode;
            AiUsageRecord record = new AiUsageRecord
            {
                AssistantId = call.AssistantId,
                EndpointId = endpoint.Id,
                Purpose = fallback && call.Purpose == AssistantPurposeEnum.Chat ? AssistantPurposeEnum.ChatFallback : call.Purpose,
                Outcome = kind,
                IsTest = call.IsTest,
                PromptTokens = outcome.PromptTokens,
                CompletionTokens = outcome.CompletionTokens,
                ReasoningTokens = outcome.ReasoningTokens,
                LatencyMs = outcome.LatencyMs,
                TimeToFirstTokenMs = outcome.TimeToFirstTokenMs,
                CostMicros = outcome.CostMicros,
                OccurredUtc = DateTime.UtcNow,
                Detail = outcome.Detail
            };
            string? guard = call.GuardKey == null ? null : call.GuardKey + ":" + endpoint.Id + ":" + outcome.Retries + ":" + kind + (guardSuffix == null ? String.Empty : ":" + guardSuffix);
            outcome.CallId = await _Usage.RecordAsync(record, endpoint, fallback, guard, outcome.Success ? null : outcome.ErrorCode).ConfigureAwait(false);
        }

        private static EmbeddingOptions EmbeddingOptionsFor(ModelEndpoint endpoint)
        {
            if ((endpoint.ApiType == ModelApiTypeEnum.OpenAI || endpoint.ApiType == ModelApiTypeEnum.OpenAICompatible) && endpoint.Dimensions != null)
                return new OpenAiEmbeddingOptions { Model = endpoint.CallModel, Dimensions = endpoint.Dimensions };
            return new EmbeddingOptions { Model = endpoint.CallModel };
        }

        private static Activity? Span(string name, ModelEndpoint endpoint, AssistantPurposeEnum purpose, string? assistantId)
        {
            Activity? span = AiTelemetry.Span(name);
            if (span == null) return null;
            span.SetTag("ai.endpoint.id", endpoint.Id);
            span.SetTag("ai.api_type", endpoint.ApiType.ToString());
            span.SetTag("ai.model", endpoint.Model);
            span.SetTag("ai.purpose", purpose.ToString());
            if (assistantId != null) span.SetTag("ai.assistant.id", assistantId);
            return span;
        }

        #endregion
    }
}
