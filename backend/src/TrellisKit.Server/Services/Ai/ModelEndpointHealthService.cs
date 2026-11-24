namespace TrellisKit.Server.Services.Ai
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Linq;
    using System.Security.Cryptography;
    using System.Threading;
    using System.Threading.Tasks;
    using PolyPrompt.Clients;
    using PolyPrompt.Models;
    using TrellisKit.Core;
    using TrellisKit.Core.Enumeration;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Helpers;
    using TrellisKit.Core.Integrations.Implementations;
    using TrellisKit.Core.Integrations.Models;
    using TrellisKit.Core.Models;
    using TrellisKit.Core.Observability;
    using TrellisKit.Core.Requests;
    using TrellisKit.Core.Responses;

    /// <summary>
    /// Endpoint health checks (docs/AI_CONTRACT.md section 4): runs a check in the endpoint's mode with its timeout and the
    /// same auth path as real calls, records it, and moves the status after the configured consecutive results (from
    /// Unknown, the first result decides). Records are kept for ai.healthHistoryDays and at most
    /// ai.healthHistoryMaxRecords per endpoint. Uptime is successes over checks in ai.uptimeWindowHours.
    /// Thread safety: safe to call concurrently.
    /// </summary>
    public class ModelEndpointHealthService
    {
        #region Private-Members

        private readonly AiServices _Ai;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the service.
        /// </summary>
        /// <param name="ai">AI services of the property.</param>
        /// <exception cref="ArgumentNullException">The services are null.</exception>
        public ModelEndpointHealthService(AiServices ai)
        {
            _Ai = ai ?? throw new ArgumentNullException(nameof(ai));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Health view of an endpoint.
        /// </summary>
        /// <param name="endpoint">Endpoint.</param>
        /// <param name="uptime">Compute the uptime percentage.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>View.</returns>
        public async Task<EndpointHealthView> HealthViewAsync(ModelEndpoint endpoint, bool uptime, CancellationToken token)
        {
            ArgumentNullException.ThrowIfNull(endpoint);
            int hours = _Ai.App.Settings.Ai.UptimeWindowHours;
            double? percentage = null;
            if (uptime)
            {
                long[] counts = await _Ai.App.Database.Ai.Endpoints.UptimeAsync(endpoint.Id, DateTime.UtcNow.AddHours(-hours), token).ConfigureAwait(false);
                if (counts[1] > 0) percentage = Math.Round(100.0 * counts[0] / counts[1], 2);
            }
            return new EndpointHealthView
            {
                Status = endpoint.HealthStatus,
                LastCheckedUtc = endpoint.LastCheckedUtc,
                LastLatencyMs = endpoint.LastLatencyMs,
                LastError = endpoint.LastError,
                ConsecutiveSuccesses = endpoint.ConsecutiveSuccesses,
                ConsecutiveFailures = endpoint.ConsecutiveFailures,
                LastHealthyUtc = endpoint.LastHealthyUtc,
                LastUnhealthyUtc = endpoint.LastUnhealthyUtc,
                UptimePercentage = percentage,
                UptimeWindowHours = hours
            };
        }

        /// <summary>
        /// GET /api/v1/admin/ai/endpoints/{id}/health.
        /// </summary>
        /// <param name="id">Endpoint id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Health with the 20 newest records.</returns>
        public async Task<EndpointHealthDetailView> DetailAsync(string id, CancellationToken token)
        {
            ModelEndpoint endpoint = await _Ai.Endpoints.RequireAsync(id, token).ConfigureAwait(false);
            EnumerationRequest request = _Ai.App.Enumeration.Prepare(new EnumerationQuery { MaxResults = 20 }, AiCollections.HealthChecks, true, endpoint.Id);
            EnumerationResult<ModelEndpointHealthCheck> page = await _Ai.App.Database.Ai.Endpoints.EnumerateHealthAsync(request, endpoint.Id, new AiEnumerationFilter(), token).ConfigureAwait(false);
            return new EndpointHealthDetailView
            {
                Health = await HealthViewAsync(endpoint, true, token).ConfigureAwait(false),
                Recent = new HealthRecentView
                {
                    Objects = page.Objects.Select(ToView).ToList(),
                    TotalRecords = page.TotalRecords ?? page.Objects.Count,
                    Enumerate = Constants.ApiPrefix + "/admin/ai/endpoints/" + endpoint.Id + "/health/enumerate"
                }
            };
        }

        /// <summary>
        /// POST /api/v1/admin/ai/endpoints/{id}/health/enumerate (filter success).
        /// </summary>
        /// <param name="id">Endpoint id.</param>
        /// <param name="query">Query.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Page.</returns>
        public async Task<EnumerationResult<HealthCheckRecordView>> EnumerateAsync(string id, EnumerationQuery? query, CancellationToken token)
        {
            ModelEndpoint endpoint = await _Ai.Endpoints.RequireAsync(id, token).ConfigureAwait(false);
            EnumerationRequest request = _Ai.App.Enumeration.Prepare(query, AiCollections.HealthChecks, true, endpoint.Id);
            Dictionary<string, string> errors = new Dictionary<string, string>();
            AiEnumerationFilter filter = new AiEnumerationFilter { Success = request.Query.FilterBool("success", errors) };
            if (errors.Count > 0) throw Core.Exceptions.ApiErrorException.Validation(errors);
            EnumerationResult<ModelEndpointHealthCheck> page = await _Ai.App.Database.Ai.Endpoints.EnumerateHealthAsync(request, endpoint.Id, filter, token).ConfigureAwait(false);
            return page.With(page.Objects.Select(ToView).ToList());
        }

        /// <summary>
        /// POST /api/v1/admin/ai/endpoints/{id}/health/check: run a check now (rate limited per admin).
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="id">Endpoint id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Record.</returns>
        public async Task<HealthCheckRecordView> CheckNowAsync(ContentActor actor, string id, CancellationToken token)
        {
            ArgumentNullException.ThrowIfNull(actor);
            ModelEndpoint endpoint = await _Ai.Endpoints.RequireAsync(id, token).ConfigureAwait(false);
            _Ai.App.Limits.Enforce("ai-admin-tests:" + (actor.UserId ?? "admin-token"), _Ai.App.Settings.Ai.AdminTestsPerHour, RateLimitService.Hour);
            ModelEndpointHealthCheck record = await CheckAsync(endpoint, token).ConfigureAwait(false);
            return ToView(record);
        }

        /// <summary>
        /// Run one check, record it, and update the endpoint's status.
        /// </summary>
        /// <param name="endpoint">Endpoint.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Record.</returns>
        public async Task<ModelEndpointHealthCheck> CheckAsync(ModelEndpoint endpoint, CancellationToken token)
        {
            ArgumentNullException.ThrowIfNull(endpoint);
            Stopwatch watch = Stopwatch.StartNew();
            bool success = false;
            int? status = null;
            string? error = null;
            AiSecretRedactor redactor = AiSecretRedactor.None;
            ResponseBase? modelResponse = null;
            TokenUsage? modelUsage = null;
            AiCallDetail detail = new AiCallDetail();
            using (CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(token))
            using (Activity? span = AiTelemetry.Span("ai.health_check"))
            using (ModelCallProbe probe = ModelCallProbe.Begin())
            {
                timeout.CancelAfter(Math.Clamp(endpoint.HealthCheckTimeoutMs, 1000, 120000));
                span?.SetTag("ai.endpoint.id", endpoint.Id);
                try
                {
                    switch (endpoint.HealthCheckMode)
                    {
                        case ModelHealthCheckModeEnum.Completion:
                            using (ModelClientLease<ProviderClientBase> lease = _Ai.Factory.CreateCompletion(endpoint))
                            {
                                redactor = lease.Redactor;
                                ChatResponse response = await lease.Client.ChatAsync("ping", ModelInvoker.OptionsFor(endpoint, 1, 0, null, _Ai.App.Settings.Ai.DefaultContextWindow), timeout.Token).ConfigureAwait(false);
                                modelResponse = response;
                                modelUsage = response.Usage;
                                success = response.Success;
                                status = response.StatusCode;
                                error = response.Error;
                            }
                            break;
                        case ModelHealthCheckModeEnum.Embedding:
                            using (ModelClientLease<ProviderClientBase> lease = _Ai.Factory.CreateEmbedding(endpoint))
                            {
                                redactor = lease.Redactor;
                                EmbeddingResponse response = await lease.Client.EmbedAsync("ping", new EmbeddingOptions { Model = endpoint.CallModel }, timeout.Token).ConfigureAwait(false);
                                modelResponse = response;
                                modelUsage = response.Usage ?? new TokenUsage { PromptTokens = AiCallDetails.EmbeddingTokens(response) };
                                success = response.Success && response.Embeddings.Count > 0;
                                status = response.StatusCode;
                                error = response.Error;
                            }
                            break;
                        case ModelHealthCheckModeEnum.Rerank:
                            using (ModelClientLease<ProviderClientBase> lease = _Ai.Factory.CreateRerank(endpoint))
                            {
                                redactor = lease.Redactor;
                                RerankResponse response = await lease.Client.RerankAsync("ping", new List<string> { "ping", "pong" }, null, timeout.Token).ConfigureAwait(false);
                                modelResponse = response;
                                modelUsage = new TokenUsage { PromptTokens = response.TotalTokens, TotalTokens = response.TotalTokens };
                                success = response.Success;
                                status = response.StatusCode;
                                error = response.Error;
                            }
                            break;
                        default:
                            success = await ConnectivityAsync(endpoint, timeout.Token).ConfigureAwait(false);
                            if (!success) error = "Connectivity check failed.";
                            break;
                    }
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    throw;
                }
                catch (OperationCanceledException)
                {
                    success = false;
                    error = "Timed out after " + endpoint.HealthCheckTimeoutMs + " ms.";
                }
                catch (CryptographicException)
                {
                    success = false;
                    error = "A stored secret cannot be decrypted with the configured keys.";
                }
                catch (Exception e) when (e is not OutOfMemoryException)
                {
                    success = false;
                    error = e.Message;
                }
                if (!success) span?.SetStatus(ActivityStatusCode.Error);
                detail = AiCallDetails.ForResponse(modelResponse, modelUsage, probe, null, false);
            }
            error = success ? null : Cut(redactor.Redact(error) ?? "Check failed.", 500);
            DateTime now = DateTime.UtcNow;
            ModelEndpointHealthCheck record = new ModelEndpointHealthCheck
            {
                Id = IdGenerator.Generate(AiConstants.HealthCheckIdPrefix),
                EndpointId = endpoint.Id,
                CheckedUtc = now,
                Success = success,
                LatencyMs = watch.ElapsedMilliseconds,
                StatusCode = status,
                Error = error,
                Mode = endpoint.HealthCheckMode
            };
            await _Ai.App.Database.Ai.Endpoints.AddHealthCheckAsync(record, token).ConfigureAwait(false);
            if (endpoint.HealthCheckMode != ModelHealthCheckModeEnum.Connectivity)
            {
                // docs/AI_TELEMETRY.md: a health check that calls the model is a model interaction, so it gets a ledger
                // row (purpose HealthCheck, test traffic, no usage rollup). Connectivity checks call no model.
                int prompt = Math.Max(0, modelUsage?.PromptTokens ?? 0);
                int completion = Math.Max(0, modelUsage?.CompletionTokens ?? 0);
                int reasoning = Math.Max(0, modelUsage?.ReasoningTokens ?? 0);
                detail.StatusCode ??= status;
                if (modelResponse is ChatResponse chat)
                {
                    detail.FinishReason = chat.FinishReason;
                    detail.ResponseId = chat.ResponseId;
                }
                if (success) detail.TimeToLastTokenMs = record.LatencyMs;
                await _Ai.Usage.RecordDirectAsync(new AiUsageRecord
                {
                    Purpose = AssistantPurposeEnum.HealthCheck,
                    Outcome = success ? AiUsageOutcomeEnum.Ok : error != null && error.StartsWith("Timed out", StringComparison.Ordinal) ? AiUsageOutcomeEnum.Timeout : AiUsageOutcomeEnum.Error,
                    PromptTokens = prompt,
                    CompletionTokens = completion,
                    ReasoningTokens = reasoning,
                    LatencyMs = record.LatencyMs,
                    CostMicros = ModelInvoker.Cost(endpoint, prompt, completion + reasoning, detail.CachedReadTokens, detail.CacheCreationTokens),
                    OccurredUtc = now,
                    Detail = detail
                }, endpoint, success ? null : "HealthCheckFailed").ConfigureAwait(false);
            }
            TrellisKitTelemetry.Add(AiTelemetry.HealthChecks, 1, "mode", AiTelemetry.Label(endpoint.HealthCheckMode.ToString()), "outcome", success ? "ok" : "error");
            Apply(endpoint, record, now);
            await _Ai.App.Database.Ai.Endpoints.UpdateHealthAsync(endpoint, token).ConfigureAwait(false);
            return record;
        }

        /// <summary>
        /// Check every due endpoint (at most 50 per pass), prune history, and publish the health gauge. Used by the
        /// health worker.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Checks run.</returns>
        public async Task<int> RunDueAsync(CancellationToken token)
        {
            if (!_Ai.IsAvailable) return 0;
            DateTime now = DateTime.UtcNow;
            List<ModelEndpoint> due = await _Ai.App.Database.Ai.Endpoints.ListDueForHealthAsync(now, 50, token).ConfigureAwait(false);
            int ran = 0;
            foreach (ModelEndpoint endpoint in due)
            {
                token.ThrowIfCancellationRequested();
                try
                {
                    await CheckAsync(endpoint, token).ConfigureAwait(false);
                    ran++;
                }
                catch (Exception e) when (e is not OperationCanceledException)
                {
                    _Ai.App.Logger.Warn("[ModelEndpointHealthService] check of " + endpoint.Id + " failed: " + e.Message);
                }
            }
            await _Ai.App.Database.Ai.Endpoints.PruneHealthAsync(now.AddDays(-_Ai.App.Settings.Ai.HealthHistoryDays), _Ai.App.Settings.Ai.HealthHistoryMaxRecords, token).ConfigureAwait(false);
            Dictionary<string, long> counts = await _Ai.App.Database.Ai.Endpoints.CountByHealthAsync(token).ConfigureAwait(false);
            AiTelemetry.SetHealthCounts(Count(counts, "Healthy"), Count(counts, "Unhealthy"), Count(counts, "Unknown"));
            return ran;
        }

        #endregion

        #region Private-Methods

        private async Task<bool> ConnectivityAsync(ModelEndpoint endpoint, CancellationToken token)
        {
            switch (endpoint.Kind)
            {
                case ModelEndpointKindEnum.Embedding:
                    using (ModelClientLease<ProviderClientBase> lease = _Ai.Factory.CreateEmbedding(endpoint))
                        return await lease.Client.ValidateConnectivityAsync(token).ConfigureAwait(false);
                case ModelEndpointKindEnum.Rerank:
                    using (ModelClientLease<ProviderClientBase> lease = _Ai.Factory.CreateRerank(endpoint))
                        return await lease.Client.ValidateConnectivityAsync(token).ConfigureAwait(false);
                default:
                    using (ModelClientLease<ProviderClientBase> lease = _Ai.Factory.CreateCompletion(endpoint))
                        return await lease.Client.ValidateConnectivityAsync(token).ConfigureAwait(false);
            }
        }

        private void Apply(ModelEndpoint e, ModelEndpointHealthCheck record, DateTime now)
        {
            e.LastCheckedUtc = now;
            e.LastLatencyMs = record.LatencyMs;
            e.LastError = record.Error;
            ModelHealthStatusEnum before = e.HealthStatus;
            if (record.Success)
            {
                e.ConsecutiveSuccesses++;
                e.ConsecutiveFailures = 0;
                if (e.HealthStatus == ModelHealthStatusEnum.Unknown || e.ConsecutiveSuccesses >= Math.Max(1, e.HealthyThreshold)) e.HealthStatus = ModelHealthStatusEnum.Healthy;
            }
            else
            {
                e.ConsecutiveFailures++;
                e.ConsecutiveSuccesses = 0;
                if (e.HealthStatus == ModelHealthStatusEnum.Unknown || e.ConsecutiveFailures >= Math.Max(1, e.UnhealthyThreshold)) e.HealthStatus = ModelHealthStatusEnum.Unhealthy;
            }
            if (e.HealthStatus == ModelHealthStatusEnum.Healthy) e.LastHealthyUtc = now;
            if (e.HealthStatus == ModelHealthStatusEnum.Unhealthy) e.LastUnhealthyUtc = now;
            if (before != ModelHealthStatusEnum.Unhealthy && e.HealthStatus == ModelHealthStatusEnum.Unhealthy)
            {
                TrellisKitTelemetry.Add(AiTelemetry.UnhealthyTransitions, 1);
                if (e.LastUnhealthyNotifiedUtc == null || e.LastUnhealthyNotifiedUtc < now.AddHours(-1))
                {
                    e.LastUnhealthyNotifiedUtc = now;
                    _Ai.App.Logger.Warn("[ModelEndpointHealthService] endpoint " + e.Id + " (" + e.Name + ") is Unhealthy: " + e.LastError);
                }
            }
            int interval = Math.Clamp(e.HealthCheckIntervalMs, 10000, 3600000);
            int jitter = Random.Shared.Next(0, Math.Max(1, interval / 10));
            e.NextCheckUtc = now.AddMilliseconds(interval + jitter);
        }

        private static long Count(Dictionary<string, long> counts, string key)
        {
            return counts.TryGetValue(key, out long value) ? value : 0;
        }

        private static string Cut(string text, int max)
        {
            return text.Length <= max ? text : text.Substring(0, max);
        }

        private static HealthCheckRecordView ToView(ModelEndpointHealthCheck record)
        {
            return new HealthCheckRecordView
            {
                Id = record.Id,
                CheckedUtc = record.CheckedUtc,
                Success = record.Success,
                LatencyMs = record.LatencyMs,
                StatusCode = record.StatusCode,
                Error = record.Error,
                Mode = record.Mode
            };
        }

        #endregion
    }
}
