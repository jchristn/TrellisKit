namespace TrellisKit.Core.Requests
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// Body of POST and PUT /api/v1/admin/ai/endpoints (docs/AI_CONTRACT.md section 1.4). On update every field is optional;
    /// nested objects merge field by field and a null field resets it to its default.
    /// </summary>
    public class ModelEndpointRequest
    {
        #region Public-Members

        /// <summary>
        /// Name (1 to 100, unique per property).
        /// </summary>
        public string? Name { get; set; } = null;

        /// <summary>
        /// Description (up to 1000).
        /// </summary>
        public string? Description { get; set; } = null;

        /// <summary>
        /// Enabled.
        /// </summary>
        public bool? Enabled { get; set; } = null;

        /// <summary>
        /// Completion, Embedding, or Rerank (required on create).
        /// </summary>
        public ModelEndpointKindEnum? Kind { get; set; } = null;

        /// <summary>
        /// API type (required on create).
        /// </summary>
        public ModelApiTypeEnum? ApiType { get; set; } = null;

        /// <summary>
        /// Model (1 to 200, required on create).
        /// </summary>
        public string? Model { get; set; } = null;

        /// <summary>
        /// Absolute http or https URL without credentials or query string; empty for the provider default.
        /// </summary>
        public string? EndpointUrl { get; set; } = null;

        /// <summary>
        /// Auth type (default None).
        /// </summary>
        public ModelAuthTypeEnum? AuthType { get; set; } = null;

        /// <summary>
        /// Header name (Header auth).
        /// </summary>
        public string? AuthHeaderName { get; set; } = null;

        /// <summary>
        /// Query parameter (QueryParameter auth).
        /// </summary>
        public string? AuthQueryParameter { get; set; } = null;

        /// <summary>
        /// Username (Basic auth).
        /// </summary>
        public string? AuthUsername { get; set; } = null;

        /// <summary>
        /// Write-only secret: omitted keeps it, a value replaces it, empty or null clears it.
        /// </summary>
        public string? AuthValue { get; set; } = null;

        /// <summary>
        /// Replacement header list; an entry without value keeps the stored value of the same name.
        /// </summary>
        public List<EndpointHeaderRequest>? ExtraHeaders { get; set; } = null;

        /// <summary>
        /// region, project, apiVersion, deployment, accessKeyId.
        /// </summary>
        public Dictionary<string, JsonElement>? ProviderSettings { get; set; } = null;

        /// <summary>
        /// temperature, topP, maxTokens, contextWindow, reasoningEffort, streaming, timeoutMs, firstResponseTimeoutMs, dimensions.
        /// </summary>
        public Dictionary<string, JsonElement>? Parameters { get; set; } = null;

        /// <summary>
        /// maxConcurrentRequests, maxQueueDepth, maxRetries.
        /// </summary>
        public Dictionary<string, JsonElement>? Limits { get; set; } = null;

        /// <summary>
        /// enabled, mode, intervalMs, timeoutMs, healthyThreshold, unhealthyThreshold.
        /// </summary>
        public Dictionary<string, JsonElement>? HealthCheck { get; set; } = null;

        /// <summary>
        /// inputPerMillionTokens, outputPerMillionTokens (US dollars).
        /// </summary>
        public Dictionary<string, JsonElement>? Pricing { get; set; } = null;

        /// <summary>
        /// Property (Deployment is reserved for platform administrators).
        /// </summary>
        public ModelEndpointScopeEnum? Scope { get; set; } = null;

        /// <summary>
        /// Replacement labels, or null for unchanged.
        /// </summary>
        public List<string>? Labels { get; set; } = null;

        /// <summary>
        /// Replacement tags, or null for unchanged.
        /// </summary>
        public Dictionary<string, string>? Tags { get; set; } = null;

        #endregion
    }
}
