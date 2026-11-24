namespace TrellisKit.Core.Responses
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// Admin payload of a model endpoint (docs/AI_CONTRACT.md section 1.1). Secrets are never included: hasAuthValue and
    /// extraHeaders[].hasValue say whether they are set.
    /// </summary>
    public class ModelEndpointView
    {
        #region Public-Members

        /// <summary>
        /// Identifier.
        /// </summary>
        public string Id { get; set; } = String.Empty;

        /// <summary>
        /// Name.
        /// </summary>
        public string Name { get; set; } = String.Empty;

        /// <summary>
        /// Description.
        /// </summary>
        public string? Description { get; set; } = null;

        /// <summary>
        /// Enabled.
        /// </summary>
        public bool Enabled { get; set; } = false;

        /// <summary>
        /// Kind.
        /// </summary>
        public ModelEndpointKindEnum Kind { get; set; } = ModelEndpointKindEnum.Completion;

        /// <summary>
        /// API type.
        /// </summary>
        public ModelApiTypeEnum ApiType { get; set; } = ModelApiTypeEnum.Ollama;

        /// <summary>
        /// Model.
        /// </summary>
        public string Model { get; set; } = String.Empty;

        /// <summary>
        /// Base URL (empty for the provider default).
        /// </summary>
        public string EndpointUrl { get; set; } = String.Empty;

        /// <summary>
        /// Auth type.
        /// </summary>
        public ModelAuthTypeEnum AuthType { get; set; } = ModelAuthTypeEnum.None;

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
        /// Whether an auth value is stored.
        /// </summary>
        public bool HasAuthValue { get; set; } = false;

        /// <summary>
        /// Extra headers (names only).
        /// </summary>
        public List<EndpointHeaderView> ExtraHeaders { get; set; } = new List<EndpointHeaderView>();

        /// <summary>
        /// Provider settings.
        /// </summary>
        public EndpointProviderSettingsView ProviderSettings { get; set; } = new EndpointProviderSettingsView();

        /// <summary>
        /// Call parameters.
        /// </summary>
        public EndpointParametersView Parameters { get; set; } = new EndpointParametersView();

        /// <summary>
        /// Concurrency, queue, and retries.
        /// </summary>
        public EndpointLimitsView Limits { get; set; } = new EndpointLimitsView();

        /// <summary>
        /// Health check settings.
        /// </summary>
        public EndpointHealthCheckView HealthCheck { get; set; } = new EndpointHealthCheckView();

        /// <summary>
        /// Health state.
        /// </summary>
        public EndpointHealthView Health { get; set; } = new EndpointHealthView();

        /// <summary>
        /// Admin-entered prices.
        /// </summary>
        public EndpointPricingView Pricing { get; set; } = new EndpointPricingView();

        /// <summary>
        /// Property or Deployment.
        /// </summary>
        public ModelEndpointScopeEnum Scope { get; set; } = ModelEndpointScopeEnum.Property;

        /// <summary>
        /// Bounded preview of the assistants that reference the endpoint (single reads only).
        /// </summary>
        public EndpointUsedByView? UsedBy { get; set; } = null;

        /// <summary>
        /// Labels.
        /// </summary>
        public List<string> Labels { get; set; } = new List<string>();

        /// <summary>
        /// Tags.
        /// </summary>
        public Dictionary<string, string> Tags { get; set; } = new Dictionary<string, string>();

        /// <summary>
        /// Created.
        /// </summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Updated.
        /// </summary>
        public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;

        #endregion
    }
}
