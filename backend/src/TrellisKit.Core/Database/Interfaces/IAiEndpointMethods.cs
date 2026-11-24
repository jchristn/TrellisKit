namespace TrellisKit.Core.Database.Interfaces
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core.Enumeration;
    using TrellisKit.Core.Exceptions;
    using TrellisKit.Core.Models;
    using TrellisKit.Core.Requests;
    using TrellisKit.Core.Responses;

    /// <summary>
    /// Model endpoint data access (aiendpoints, aiendpointheaders, aiendpointhealthchecks). Implementations are bound to
    /// one property and safe to call concurrently.
    /// </summary>
    public interface IAiEndpointMethods
    {
        /// <summary>
        /// Create an endpoint with its headers.
        /// </summary>
        /// <param name="endpoint">Endpoint.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The stored endpoint.</returns>
        /// <exception cref="ArgumentNullException">The endpoint is null.</exception>
        /// <exception cref="DuplicateRecordException">The name is taken.</exception>
        Task<ModelEndpoint> CreateAsync(ModelEndpoint endpoint, CancellationToken token = default);

        /// <summary>
        /// Read a live (not deleted) endpoint with its headers.
        /// </summary>
        /// <param name="id">Endpoint id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Endpoint, or null.</returns>
        Task<ModelEndpoint?> ReadAsync(string id, CancellationToken token = default);

        /// <summary>
        /// Read live endpoints by id (at most 50 ids), with headers.
        /// </summary>
        /// <param name="ids">Ids.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Endpoints found.</returns>
        Task<List<ModelEndpoint>> ReadManyAsync(IEnumerable<string> ids, CancellationToken token = default);

        /// <summary>
        /// Update every column and replace the headers.
        /// </summary>
        /// <param name="endpoint">Endpoint.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The endpoint.</returns>
        /// <exception cref="DuplicateRecordException">The name is taken.</exception>
        Task<ModelEndpoint> UpdateAsync(ModelEndpoint endpoint, CancellationToken token = default);

        /// <summary>
        /// Update only the health columns.
        /// </summary>
        /// <param name="endpoint">Endpoint with the new health state.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        Task UpdateHealthAsync(ModelEndpoint endpoint, CancellationToken token = default);

        /// <summary>
        /// Soft-delete an endpoint and remove its headers (health history is kept).
        /// </summary>
        /// <param name="id">Endpoint id.</param>
        /// <param name="nowUtc">Deletion time.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True when deleted.</returns>
        Task<bool> DeleteAsync(string id, DateTime nowUtc, CancellationToken token = default);

        /// <summary>
        /// Enumerate live endpoints (collection AiCollections.Endpoints), without headers.
        /// </summary>
        /// <param name="request">Prepared request.</param>
        /// <param name="filter">Filters.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Page.</returns>
        Task<EnumerationResult<ModelEndpoint>> EnumerateAsync(EnumerationRequest request, AiEnumerationFilter filter, CancellationToken token = default);

        /// <summary>
        /// Enabled live endpoints with health checks on whose next check is due, with headers (at most limit).
        /// </summary>
        /// <param name="nowUtc">Now.</param>
        /// <param name="limit">Most rows. Minimum 1, maximum 500.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Endpoints.</returns>
        Task<List<ModelEndpoint>> ListDueForHealthAsync(DateTime nowUtc, int limit, CancellationToken token = default);

        /// <summary>
        /// Live endpoints whose stored secrets use another key, with headers (at most limit), for re-encryption.
        /// </summary>
        /// <param name="limit">Most rows. Minimum 1, maximum 500.</param>
        /// <param name="afterId">Return ids after this one, or null.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Endpoints.</returns>
        Task<List<ModelEndpoint>> ListPageAsync(int limit, string? afterId, CancellationToken token = default);

        /// <summary>
        /// Counts of live endpoints per health status.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Status name to count.</returns>
        Task<Dictionary<string, long>> CountByHealthAsync(CancellationToken token = default);

        /// <summary>
        /// Enumerate the assistants that reference an endpoint (collection AiCollections.EndpointUsage).
        /// </summary>
        /// <param name="request">Prepared request.</param>
        /// <param name="endpointId">Endpoint id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Page.</returns>
        Task<EnumerationResult<EndpointUsageItem>> EnumerateUsageAsync(EnumerationRequest request, string endpointId, CancellationToken token = default);

        /// <summary>
        /// Up to limit live assistant ids that reference an endpoint.
        /// </summary>
        /// <param name="endpointId">Endpoint id.</param>
        /// <param name="limit">Most ids.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Ids.</returns>
        Task<List<string>> ReferencingAssistantsAsync(string endpointId, int limit, CancellationToken token = default);

        /// <summary>
        /// Add a health check record.
        /// </summary>
        /// <param name="record">Record.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        Task AddHealthCheckAsync(ModelEndpointHealthCheck record, CancellationToken token = default);

        /// <summary>
        /// Enumerate an endpoint's health records (collection AiCollections.HealthChecks).
        /// </summary>
        /// <param name="request">Prepared request.</param>
        /// <param name="endpointId">Endpoint id.</param>
        /// <param name="filter">Filters.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Page.</returns>
        Task<EnumerationResult<ModelEndpointHealthCheck>> EnumerateHealthAsync(EnumerationRequest request, string endpointId, AiEnumerationFilter filter, CancellationToken token = default);

        /// <summary>
        /// Successes and total checks of an endpoint since a time.
        /// </summary>
        /// <param name="endpointId">Endpoint id.</param>
        /// <param name="sinceUtc">Window start.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Two numbers: index 0 successes, index 1 total.</returns>
        Task<long[]> UptimeAsync(string endpointId, DateTime sinceUtc, CancellationToken token = default);

        /// <summary>
        /// Delete health records older than a time, and beyond the newest maxPerEndpoint of each endpoint.
        /// </summary>
        /// <param name="olderThanUtc">Age cutoff.</param>
        /// <param name="maxPerEndpoint">Records kept per endpoint.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Rows deleted.</returns>
        Task<int> PruneHealthAsync(DateTime olderThanUtc, int maxPerEndpoint, CancellationToken token = default);
    }
}
