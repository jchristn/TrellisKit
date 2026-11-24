namespace TrellisKit.Core.Integrations.Interfaces
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core.Integrations.Models;

    /// <summary>
    /// Forward geocoding of addresses and place names (docs/PHASE4_CONTRACT.md section 3.5). Implementations receive
    /// only the address or the typed place text, never a member identifier, and are safe for concurrent use.
    /// </summary>
    public interface IGeocoder
    {
        /// <summary>
        /// Provider name (cache key and telemetry label): None, Nominatim, AwsLocation, or Http.
        /// </summary>
        string Provider { get; }

        /// <summary>
        /// Whether the provider has its required configuration.
        /// </summary>
        bool IsConfigured { get; }

        /// <summary>
        /// Find candidates for a query, best first.
        /// </summary>
        /// <param name="query">Address or place text.</param>
        /// <param name="limit">Most candidates (1 to 10).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Candidates (empty when nothing matched).</returns>
        /// <exception cref="ArgumentNullException">The query is null or empty.</exception>
        /// <exception cref="TrellisKit.Core.Exceptions.IntegrationNotConfiguredException">The provider is not configured.</exception>
        /// <exception cref="System.Net.Http.HttpRequestException">The provider failed.</exception>
        Task<List<GeocodeCandidate>> SearchAsync(string query, int limit, CancellationToken token = default);
    }
}
