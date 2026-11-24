namespace TrellisKit.Server.Services
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Net;
    using System.Net.Sockets;
    using TrellisKit.Core.Settings;
    using WatsonWebserver.Core;

    /// <summary>
    /// Resolves the client IP of a request. Forwarding headers are honored only when the socket peer is a trusted proxy
    /// (settings rest.trustedProxies plus rest.additionalTrustedProxies). The client is then the CDN viewer address
    /// header (rest.viewerAddressHeader, default CloudFront-Viewer-Address, ip:port) when it holds a valid address, else
    /// the right-most X-Forwarded-For address that is not itself trusted, so a client cannot spoof its address by
    /// sending its own headers to an untrusted listener.
    /// Thread safety: stateless; safe to call concurrently.
    /// </summary>
    public static class ClientAddressResolver
    {
        #region Public-Methods

        /// <summary>
        /// Resolve the client IP of an HTTP request.
        /// </summary>
        /// <param name="ctx">HTTP context.</param>
        /// <param name="trustedProxies">Trusted proxy addresses and CIDR ranges, or null for none.</param>
        /// <returns>Client IP, or null when the socket peer is unknown.</returns>
        /// <exception cref="ArgumentNullException">The context is null.</exception>
        public static string? Resolve(HttpContextBase ctx, IReadOnlyList<string>? trustedProxies)
        {
            ArgumentNullException.ThrowIfNull(ctx);
            return Resolve(ctx.Request.Source?.IpAddress, ctx.Request.RetrieveHeaderValue("X-Forwarded-For"), trustedProxies);
        }

        /// <summary>
        /// Resolve the client IP of an HTTP request with the listener settings: every trusted proxy entry and the viewer
        /// address header.
        /// </summary>
        /// <param name="ctx">HTTP context.</param>
        /// <param name="rest">Listener settings, or null for no trusted proxies.</param>
        /// <returns>Client IP, or null when the socket peer is unknown.</returns>
        /// <exception cref="ArgumentNullException">The context is null.</exception>
        public static string? Resolve(HttpContextBase ctx, RestSettings? rest)
        {
            ArgumentNullException.ThrowIfNull(ctx);
            if (rest == null) return Resolve(ctx.Request.Source?.IpAddress, null, null, null);
            string? viewer = String.IsNullOrEmpty(rest.ViewerAddressHeader) ? null : ctx.Request.RetrieveHeaderValue(rest.ViewerAddressHeader);
            return Resolve(ctx.Request.Source?.IpAddress, ctx.Request.RetrieveHeaderValue("X-Forwarded-For"), viewer, rest.AllTrustedProxies);
        }

        /// <summary>
        /// Resolve the client IP from a socket peer, an X-Forwarded-For value, and a CDN viewer address value.
        /// </summary>
        /// <param name="peer">Socket peer address, or null.</param>
        /// <param name="forwardedFor">X-Forwarded-For header value (comma-separated), or null.</param>
        /// <param name="viewerAddress">Viewer address header value (ip:port, IPv4 or IPv6, brackets optional), or null.</param>
        /// <param name="trustedProxies">Trusted proxy addresses and CIDR ranges, or null for none.</param>
        /// <returns>Client IP, or null when the peer is null.</returns>
        public static string? Resolve(string? peer, string? forwardedFor, string? viewerAddress, IReadOnlyList<string>? trustedProxies)
        {
            if (String.IsNullOrWhiteSpace(peer)) return null;
            string current = Normalize(peer.Trim());
            if (trustedProxies == null || trustedProxies.Count == 0) return current;
            if (!IsTrusted(current, Parse(trustedProxies))) return current;
            string? viewer = ParseViewer(viewerAddress);
            if (viewer != null) return viewer;
            return Resolve(peer, forwardedFor, trustedProxies);
        }

        /// <summary>
        /// Parse a viewer address header value: an address with an optional port (198.51.100.10:46532,
        /// 2001:db8::1:46532, or [2001:db8::1]:46532).
        /// </summary>
        /// <param name="value">Header value, or null.</param>
        /// <returns>Normalized address, or null when the value is missing or malformed.</returns>
        public static string? ParseViewer(string? value)
        {
            if (String.IsNullOrWhiteSpace(value)) return null;
            string text = value.Trim().Trim('"');
            if (text.Contains(',', StringComparison.Ordinal) || text.Length > 64) return null;
            if (text.StartsWith("[", StringComparison.Ordinal)) return Clean(text);
            if (IPAddress.TryParse(text, out IPAddress? whole) && (whole.AddressFamily == AddressFamily.InterNetworkV6 || !text.Contains(':', StringComparison.Ordinal)))
            {
                // An IPv6 text with a trailing port also parses as an address; CloudFront always appends the port, so an
                // IPv6 value is read as address plus port when the remainder is a valid address and the tail a port.
                if (whole.AddressFamily == AddressFamily.InterNetworkV6)
                {
                    string? split = SplitPort(text);
                    if (split != null) return split;
                }
                return whole.IsIPv4MappedToIPv6 ? whole.MapToIPv4().ToString() : whole.ToString();
            }
            return SplitPort(text);
        }

        /// <summary>
        /// Resolve the client IP from a socket peer and an X-Forwarded-For value.
        /// </summary>
        /// <param name="peer">Socket peer address, or null.</param>
        /// <param name="forwardedFor">X-Forwarded-For header value (comma-separated), or null.</param>
        /// <param name="trustedProxies">Trusted proxy addresses and CIDR ranges, or null for none.</param>
        /// <returns>Client IP, or null when the peer is null.</returns>
        public static string? Resolve(string? peer, string? forwardedFor, IReadOnlyList<string>? trustedProxies)
        {
            if (String.IsNullOrWhiteSpace(peer)) return null;
            string current = Normalize(peer.Trim());
            if (trustedProxies == null || trustedProxies.Count == 0 || String.IsNullOrWhiteSpace(forwardedFor)) return current;
            List<IPNetwork> networks = Parse(trustedProxies);
            if (!IsTrusted(current, networks)) return current;

            string[] hops = forwardedFor.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            for (int i = hops.Length - 1; i >= 0; i--)
            {
                string? hop = Clean(hops[i]);
                if (hop == null) return current;
                current = hop;
                if (!IsTrusted(current, networks)) return current;
            }
            return current;
        }

        /// <summary>
        /// Whether an address is inside one of the trusted proxy entries.
        /// </summary>
        /// <param name="address">Address.</param>
        /// <param name="trustedProxies">Trusted proxy addresses and CIDR ranges, or null for none.</param>
        /// <returns>True when trusted.</returns>
        public static bool IsTrusted(string? address, IReadOnlyList<string>? trustedProxies)
        {
            if (String.IsNullOrWhiteSpace(address) || trustedProxies == null || trustedProxies.Count == 0) return false;
            return IsTrusted(Normalize(address.Trim()), Parse(trustedProxies));
        }

        #endregion

        #region Private-Methods

        private static List<IPNetwork> Parse(IReadOnlyList<string> entries)
        {
            List<IPNetwork> networks = new List<IPNetwork>();
            foreach (string entry in entries)
            {
                if (String.IsNullOrWhiteSpace(entry)) continue;
                string text = entry.Trim();
                if (text.Contains('/', StringComparison.Ordinal))
                {
                    if (IPNetwork.TryParse(text, out IPNetwork network)) networks.Add(network);
                }
                else if (IPAddress.TryParse(text, out IPAddress? address))
                {
                    networks.Add(new IPNetwork(address, address.AddressFamily == AddressFamily.InterNetwork ? 32 : 128));
                }
            }
            return networks;
        }

        private static bool IsTrusted(string address, List<IPNetwork> networks)
        {
            if (!IPAddress.TryParse(address, out IPAddress? ip)) return false;
            foreach (IPNetwork network in networks)
            {
                if (network.BaseAddress.AddressFamily == ip.AddressFamily && network.Contains(ip)) return true;
            }
            return false;
        }

        private static string? SplitPort(string text)
        {
            int colon = text.LastIndexOf(':');
            if (colon <= 0 || colon == text.Length - 1) return null;
            string port = text.Substring(colon + 1);
            if (!port.All(Char.IsAsciiDigit) || port.Length > 5) return null;
            string host = text.Substring(0, colon).Trim('[', ']');
            if (!IPAddress.TryParse(host, out IPAddress? ip)) return null;
            return ip.IsIPv4MappedToIPv6 ? ip.MapToIPv4().ToString() : ip.ToString();
        }

        private static string Normalize(string address)
        {
            if (IPAddress.TryParse(address, out IPAddress? ip) && ip.IsIPv4MappedToIPv6) return ip.MapToIPv4().ToString();
            return address;
        }

        private static string? Clean(string hop)
        {
            string text = hop.Trim().Trim('"');
            if (text.StartsWith("[", StringComparison.Ordinal))
            {
                int close = text.IndexOf(']');
                if (close > 0) text = text.Substring(1, close - 1);
            }
            else if (text.Count(c => c == ':') == 1)
            {
                text = text.Substring(0, text.IndexOf(':'));
            }
            if (!IPAddress.TryParse(text, out IPAddress? ip)) return null;
            return ip.IsIPv4MappedToIPv6 ? ip.MapToIPv4().ToString() : ip.ToString();
        }

        #endregion
    }
}
