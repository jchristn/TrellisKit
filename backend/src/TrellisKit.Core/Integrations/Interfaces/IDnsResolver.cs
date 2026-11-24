namespace TrellisKit.Core.Integrations.Interfaces
{
    using System;
    using System.Net;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// Resolves host names for outbound fetches. A seam so tests can script answers (including DNS rebinding).
    /// Thread safety: implementations are safe to call concurrently.
    /// </summary>
    public interface IDnsResolver
    {
        /// <summary>
        /// Resolve a host name to its addresses.
        /// </summary>
        /// <param name="host">Host name (IDN already converted to ASCII).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Addresses (empty when the name does not resolve).</returns>
        /// <exception cref="ArgumentNullException">The host is null or empty.</exception>
        Task<IPAddress[]> ResolveAsync(string host, CancellationToken token = default);
    }
}
