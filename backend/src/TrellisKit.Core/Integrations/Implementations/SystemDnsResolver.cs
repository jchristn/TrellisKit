namespace TrellisKit.Core.Integrations.Implementations
{
    using System;
    using System.Net;
    using System.Net.Sockets;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core.Integrations.Interfaces;

    /// <summary>
    /// Resolves host names with the operating system resolver.
    /// Thread safety: stateless; safe to call concurrently.
    /// </summary>
    public class SystemDnsResolver : IDnsResolver
    {
        #region Public-Methods

        /// <inheritdoc />
        public async Task<IPAddress[]> ResolveAsync(string host, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(host)) throw new ArgumentNullException(nameof(host));
            try
            {
                return await Dns.GetHostAddressesAsync(host, token).ConfigureAwait(false);
            }
            catch (SocketException)
            {
                return Array.Empty<IPAddress>();
            }
        }

        #endregion
    }
}
