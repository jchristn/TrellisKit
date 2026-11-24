namespace TrellisKit.Core.Integrations.Implementations
{
    using System;
    using System.Linq;
    using System.Net;
    using System.Net.Http;
    using System.Net.Sockets;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core.Integrations.Interfaces;

    /// <summary>
    /// Builds HTTP handlers for configured outbound integrations (geocoders, the map tile proxy) that enforce an
    /// <see cref="ILinkPreviewAddressPolicy"/> on every connection: the host is resolved at connect time, every
    /// resolved address must be allowed, and the socket connects to the checked address (so DNS rebinding cannot swap
    /// it). No proxy, no cookies, no automatic redirects. Thread safe.
    /// </summary>
    public static class PolicyHttpHandlerFactory
    {
        #region Public-Methods

        /// <summary>
        /// Create a handler.
        /// </summary>
        /// <param name="policy">Address policy.</param>
        /// <param name="resolver">DNS resolver.</param>
        /// <param name="connectTimeout">Connect timeout.</param>
        /// <returns>Handler (dispose with its client).</returns>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        public static SocketsHttpHandler Create(ILinkPreviewAddressPolicy policy, IDnsResolver resolver, TimeSpan connectTimeout)
        {
            ArgumentNullException.ThrowIfNull(policy);
            ArgumentNullException.ThrowIfNull(resolver);
            return new SocketsHttpHandler
            {
                AllowAutoRedirect = false,
                UseCookies = false,
                UseProxy = false,
                ConnectTimeout = connectTimeout <= TimeSpan.Zero ? TimeSpan.FromSeconds(10) : connectTimeout,
                AutomaticDecompression = DecompressionMethods.All,
                PooledConnectionLifetime = TimeSpan.FromMinutes(2),
                ConnectCallback = async (context, ct) =>
                {
                    IPAddress address = await AllowedAddressAsync(policy, resolver, context.DnsEndPoint.Host, context.DnsEndPoint.Port, ct).ConfigureAwait(false);
                    Socket socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
                    try
                    {
                        await socket.ConnectAsync(new IPEndPoint(address, context.DnsEndPoint.Port), ct).ConfigureAwait(false);
                        return new NetworkStream(socket, true);
                    }
                    catch
                    {
                        socket.Dispose();
                        throw;
                    }
                }
            };
        }

        /// <summary>
        /// Resolve a host and check it against the policy.
        /// </summary>
        /// <param name="policy">Address policy.</param>
        /// <param name="resolver">DNS resolver.</param>
        /// <param name="host">Host name or address literal.</param>
        /// <param name="port">Port.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The address to connect to.</returns>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        /// <exception cref="HttpRequestException">The host does not resolve or an address or the port is not allowed.</exception>
        public static async Task<IPAddress> AllowedAddressAsync(ILinkPreviewAddressPolicy policy, IDnsResolver resolver, string host, int port, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(policy);
            ArgumentNullException.ThrowIfNull(resolver);
            if (String.IsNullOrWhiteSpace(host)) throw new ArgumentNullException(nameof(host));
            if (!policy.IsAllowedPort(port)) throw new HttpRequestException("Outbound port " + port + " is not allowed.");
            string bare = host.Trim().Trim('[', ']');
            IPAddress[] addresses = IPAddress.TryParse(bare, out IPAddress? literal) ? new[] { literal } : await resolver.ResolveAsync(bare, token).ConfigureAwait(false) ?? Array.Empty<IPAddress>();
            if (addresses.Length == 0) throw new HttpRequestException("The host " + bare + " did not resolve.");
            IPAddress[] normalized = addresses.Select(a => a.IsIPv4MappedToIPv6 ? a.MapToIPv4() : a).ToArray();
            if (normalized.Any(a => !policy.IsAllowedAddress(a))) throw new HttpRequestException("The host " + bare + " resolves to an address that outbound integrations may not reach.");
            return normalized[0];
        }

        #endregion
    }
}
