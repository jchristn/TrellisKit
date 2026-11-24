namespace TrellisKit.Core.Models
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// A model endpoint (docs/AI_CONTRACT.md section 1). Secrets are held in their stored (encrypted) form only.
    /// </summary>
    public class ModelEndpoint
    {
        #region Public-Members

        /// <summary>
        /// Owning property id.
        /// </summary>
        public string PropertyId { get; set; } = String.Empty;

        /// <summary>
        /// Identifier (mep_).
        /// </summary>
        public string Id { get; set; } = String.Empty;

        /// <summary>
        /// Property or Deployment.
        /// </summary>
        public ModelEndpointScopeEnum Scope { get; set; } = ModelEndpointScopeEnum.Property;

        /// <summary>
        /// Display name, unique per property (case-insensitive).
        /// </summary>
        public string Name { get; set; } = String.Empty;

        /// <summary>
        /// Optional description.
        /// </summary>
        public string? Description { get; set; } = null;

        /// <summary>
        /// Whether the endpoint may be called.
        /// </summary>
        public bool Enabled { get; set; } = true;

        /// <summary>
        /// Completion, Embedding, or Rerank.
        /// </summary>
        public ModelEndpointKindEnum Kind { get; set; } = ModelEndpointKindEnum.Completion;

        /// <summary>
        /// Provider API.
        /// </summary>
        public ModelApiTypeEnum ApiType { get; set; } = ModelApiTypeEnum.Ollama;

        /// <summary>
        /// Provider model name.
        /// </summary>
        public string Model { get; set; } = String.Empty;

        /// <summary>
        /// The model name sent on each call: the Azure OpenAI deployment (providerSettings.deployment) for AzureOpenAI,
        /// where model is informational; otherwise <see cref="Model"/>.
        /// </summary>
        public string CallModel
        {
            get { return ApiType == ModelApiTypeEnum.AzureOpenAI && !String.IsNullOrWhiteSpace(Deployment) ? Deployment.Trim() : Model; }
        }

        /// <summary>
        /// Base URL; empty uses the provider default.
        /// </summary>
        public string EndpointUrl { get; set; } = String.Empty;

        /// <summary>
        /// Authentication type.
        /// </summary>
        public ModelAuthTypeEnum AuthType { get; set; } = ModelAuthTypeEnum.None;

        /// <summary>
        /// Header name (Header auth).
        /// </summary>
        public string? AuthHeaderName { get; set; } = null;

        /// <summary>
        /// Query parameter name (QueryParameter auth).
        /// </summary>
        public string? AuthQueryParameter { get; set; } = null;

        /// <summary>
        /// Username (Basic auth).
        /// </summary>
        public string? AuthUsername { get; set; } = null;

        /// <summary>
        /// Encrypted auth value (stored form), or null when none.
        /// </summary>
        public string? AuthValueSecret { get; set; } = null;

        /// <summary>
        /// Extra headers with encrypted values, in order.
        /// </summary>
        public List<ModelEndpointHeader> Headers { get; set; } = new List<ModelEndpointHeader>();

        /// <summary>
        /// Provider region.
        /// </summary>
        public string? Region { get; set; } = null;

        /// <summary>
        /// Provider project.
        /// </summary>
        public string? Project { get; set; } = null;

        /// <summary>
        /// Provider API version.
        /// </summary>
        public string? ApiVersion { get; set; } = null;

        /// <summary>
        /// Provider deployment name.
        /// </summary>
        public string? Deployment { get; set; } = null;

        /// <summary>
        /// Provider access key id (not secret).
        /// </summary>
        public string? AccessKeyId { get; set; } = null;

        /// <summary>
        /// Sampling temperature, or null for the provider default.
        /// </summary>
        public double? Temperature { get; set; } = null;

        /// <summary>
        /// Nucleus sampling, or null.
        /// </summary>
        public double? TopP { get; set; } = null;

        /// <summary>
        /// Output token cap, or null.
        /// </summary>
        public int? MaxTokens { get; set; } = null;

        /// <summary>
        /// Context window in tokens, or null for the setting default.
        /// </summary>
        public int? ContextWindow { get; set; } = null;

        /// <summary>
        /// Reasoning effort, or null (not sent).
        /// </summary>
        public ModelReasoningEffortEnum? ReasoningEffort { get; set; } = null;

        /// <summary>
        /// Whether streaming calls are used.
        /// </summary>
        public bool Streaming { get; set; } = true;

        /// <summary>
        /// How assistant tools reach the model. Default Native.
        /// </summary>
        public ModelToolCallingEnum ToolCalling { get; set; } = ModelToolCallingEnum.Native;

        /// <summary>
        /// Whole-call timeout in milliseconds.
        /// </summary>
        public int TimeoutMs { get; set; } = 120000;

        /// <summary>
        /// Time to first token limit in milliseconds, or null.
        /// </summary>
        public int? FirstResponseTimeoutMs { get; set; } = null;

        /// <summary>
        /// Embedding dimensions, or null for the provider default.
        /// </summary>
        public int? Dimensions { get; set; } = null;

        /// <summary>
        /// In-flight calls per endpoint per API task.
        /// </summary>
        public int MaxConcurrentRequests { get; set; } = 2;

        /// <summary>
        /// Waiting calls beyond the concurrency.
        /// </summary>
        public int MaxQueueDepth { get; set; } = 20;

        /// <summary>
        /// Retries of retryable failures.
        /// </summary>
        public int MaxRetries { get; set; } = 2;

        /// <summary>
        /// Whether health checks run.
        /// </summary>
        public bool HealthCheckEnabled { get; set; } = true;

        /// <summary>
        /// Health check mode.
        /// </summary>
        public ModelHealthCheckModeEnum HealthCheckMode { get; set; } = ModelHealthCheckModeEnum.Connectivity;

        /// <summary>
        /// Milliseconds between checks.
        /// </summary>
        public int HealthCheckIntervalMs { get; set; } = 60000;

        /// <summary>
        /// Check timeout in milliseconds.
        /// </summary>
        public int HealthCheckTimeoutMs { get; set; } = 10000;

        /// <summary>
        /// Consecutive successes to become Healthy.
        /// </summary>
        public int HealthyThreshold { get; set; } = 2;

        /// <summary>
        /// Consecutive failures to become Unhealthy.
        /// </summary>
        public int UnhealthyThreshold { get; set; } = 2;

        /// <summary>
        /// Current health status.
        /// </summary>
        public ModelHealthStatusEnum HealthStatus { get; set; } = ModelHealthStatusEnum.Unknown;

        /// <summary>
        /// Last check time.
        /// </summary>
        public DateTime? LastCheckedUtc { get; set; } = null;

        /// <summary>
        /// Latency of the last check.
        /// </summary>
        public long? LastLatencyMs { get; set; } = null;

        /// <summary>
        /// Sanitized error of the last failed check.
        /// </summary>
        public string? LastError { get; set; } = null;

        /// <summary>
        /// Consecutive successful checks.
        /// </summary>
        public int ConsecutiveSuccesses { get; set; } = 0;

        /// <summary>
        /// Consecutive failed checks.
        /// </summary>
        public int ConsecutiveFailures { get; set; } = 0;

        /// <summary>
        /// Last time the endpoint was Healthy.
        /// </summary>
        public DateTime? LastHealthyUtc { get; set; } = null;

        /// <summary>
        /// Last time the endpoint was Unhealthy.
        /// </summary>
        public DateTime? LastUnhealthyUtc { get; set; } = null;

        /// <summary>
        /// When the next check is due.
        /// </summary>
        public DateTime? NextCheckUtc { get; set; } = null;

        /// <summary>
        /// Last Unhealthy notification time (at most one per hour).
        /// </summary>
        public DateTime? LastUnhealthyNotifiedUtc { get; set; } = null;

        /// <summary>
        /// Admin-entered price in US dollars per million input (prompt) tokens, or null.
        /// </summary>
        public double? InputPricePerMillion { get; set; } = null;

        /// <summary>
        /// Admin-entered price in US dollars per million output (completion) tokens, or null.
        /// </summary>
        public double? OutputPricePerMillion { get; set; } = null;

        /// <summary>
        /// Admin-entered price in US dollars per million input tokens read from the provider's prompt cache, or null to
        /// bill them at the input price.
        /// </summary>
        public double? CacheReadPricePerMillion { get; set; } = null;

        /// <summary>
        /// Admin-entered price in US dollars per million input tokens written to the provider's prompt cache, or null to
        /// bill them at the input price.
        /// </summary>
        public double? CacheWritePricePerMillion { get; set; } = null;

        /// <summary>
        /// Soft-delete time.
        /// </summary>
        public DateTime? DeletedUtc { get; set; } = null;

        /// <summary>
        /// Creator.
        /// </summary>
        public string? CreatedByUserId { get; set; } = null;

        /// <summary>
        /// Created time.
        /// </summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Updated time.
        /// </summary>
        public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;

        #endregion
    }
}
