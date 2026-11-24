namespace TrellisKit.Core.Integrations.Interfaces
{
    using System;
    using PolyPrompt.Clients;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Integrations.Implementations;
    using TrellisKit.Core.Integrations.Models;
    using TrellisKit.Core.Models;

    /// <summary>
    /// Builds PolyPrompt clients from model endpoints (docs/AI_CONTRACT.md section 3): one cached HTTP client per
    /// endpoint version with the endpoint's authentication applied by <see cref="EndpointAuthHandler"/>.
    /// </summary>
    public interface IModelClientFactory
    {
        /// <summary>
        /// Whether an API type supports a kind in this build.
        /// </summary>
        /// <param name="apiType">API type.</param>
        /// <param name="kind">Kind.</param>
        /// <returns>True when supported.</returns>
        bool Supports(ModelApiTypeEnum apiType, ModelEndpointKindEnum kind);

        /// <summary>
        /// Whether an API type can list provider models.
        /// </summary>
        /// <param name="apiType">API type.</param>
        /// <returns>True when supported.</returns>
        bool SupportsModelListing(ModelApiTypeEnum apiType);

        /// <summary>
        /// Whether an auth type is allowed for an API type (section 3.3).
        /// </summary>
        /// <param name="apiType">API type.</param>
        /// <param name="authType">Auth type.</param>
        /// <returns>True when allowed.</returns>
        bool IsAuthTypeAllowed(ModelApiTypeEnum apiType, ModelAuthTypeEnum authType);

        /// <summary>
        /// Decrypt an endpoint's credentials.
        /// </summary>
        /// <param name="endpoint">Endpoint.</param>
        /// <returns>Credentials.</returns>
        /// <exception cref="System.Security.Cryptography.CryptographicException">A secret cannot be decrypted.</exception>
        EndpointCredentials Credentials(ModelEndpoint endpoint);

        /// <summary>
        /// Create a completion client.
        /// </summary>
        /// <param name="endpoint">Endpoint.</param>
        /// <param name="credentials">Explicit credentials for an unsaved endpoint (uncached), or null to decrypt the stored ones.</param>
        /// <returns>Lease.</returns>
        /// <exception cref="NotSupportedException">The API type has no completion client.</exception>
        ModelClientLease<ProviderClientBase> CreateCompletion(ModelEndpoint endpoint, EndpointCredentials? credentials = null);

        /// <summary>
        /// Create an embedding client.
        /// </summary>
        /// <param name="endpoint">Endpoint.</param>
        /// <param name="credentials">Explicit credentials, or null.</param>
        /// <returns>Lease.</returns>
        /// <exception cref="NotSupportedException">The API type has no embedding client.</exception>
        ModelClientLease<ProviderClientBase> CreateEmbedding(ModelEndpoint endpoint, EndpointCredentials? credentials = null);

        /// <summary>
        /// Create a rerank client.
        /// </summary>
        /// <param name="endpoint">Endpoint.</param>
        /// <param name="credentials">Explicit credentials, or null.</param>
        /// <returns>Lease.</returns>
        /// <exception cref="NotSupportedException">The API type has no rerank client.</exception>
        ModelClientLease<ProviderClientBase> CreateRerank(ModelEndpoint endpoint, EndpointCredentials? credentials = null);

        /// <summary>
        /// Create a model listing client.
        /// </summary>
        /// <param name="endpoint">Endpoint.</param>
        /// <param name="credentials">Explicit credentials, or null.</param>
        /// <returns>Lease.</returns>
        /// <exception cref="NotSupportedException">The API type cannot list models.</exception>
        ModelClientLease<ProviderClientBase> CreateModelLister(ModelEndpoint endpoint, EndpointCredentials? credentials = null);

        /// <summary>
        /// The redactor of an endpoint's stored secrets.
        /// </summary>
        /// <param name="endpoint">Endpoint.</param>
        /// <returns>Redactor (a no-op one when the secrets cannot be decrypted).</returns>
        AiSecretRedactor RedactorFor(ModelEndpoint endpoint);

        /// <summary>
        /// Drop the cached HTTP client of an endpoint (after an edit or delete).
        /// </summary>
        /// <param name="propertyId">Property id.</param>
        /// <param name="endpointId">Endpoint id.</param>
        void Evict(string propertyId, string endpointId);
    }
}
