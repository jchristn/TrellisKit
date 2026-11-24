namespace TrellisKit.Core.Settings
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Net;

    /// <summary>
    /// HTTP listener settings. Changes require a restart.
    /// </summary>
    public class RestSettings
    {
        #region Public-Members

        /// <summary>
        /// Hostname or address to bind. Default "127.0.0.1". Use "0.0.0.0" or "*" inside containers.
        /// </summary>
        /// <exception cref="ArgumentNullException">The value is null or empty.</exception>
        public string Hostname
        {
            get
            {
                return _Hostname;
            }
            set
            {
                if (String.IsNullOrWhiteSpace(value)) throw new ArgumentNullException(nameof(Hostname));
                _Hostname = value.Trim();
            }
        }

        /// <summary>
        /// Port to bind. Default 8000. Minimum 1, maximum 65535.
        /// </summary>
        public int Port
        {
            get
            {
                return _Port;
            }
            set
            {
                _Port = Math.Clamp(value, 1, 65535);
            }
        }

        /// <summary>
        /// Whether to terminate TLS in the server. Default false; TLS is terminated by the load balancer.
        /// </summary>
        public bool Ssl { get; set; } = false;

        /// <summary>
        /// Socket peers (IP addresses or CIDR ranges) whose X-Forwarded-For header is trusted to name the client. The
        /// client IP is the right-most X-Forwarded-For address that is not itself a trusted proxy. Default: the private
        /// and loopback ranges 10.0.0.0/8, 172.16.0.0/12, 192.168.0.0/16, 127.0.0.0/8, ::1, and fc00::/7. An empty list
        /// ignores X-Forwarded-For. Null is treated as empty.
        /// </summary>
        /// <exception cref="ArgumentException">An entry is not an IP address or CIDR range.</exception>
        public List<string> TrustedProxies
        {
            get
            {
                return _TrustedProxies;
            }
            set
            {
                List<string> entries = (value ?? new List<string>()).Where(v => !String.IsNullOrWhiteSpace(v)).Select(v => v.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                foreach (string entry in entries)
                {
                    if (!IsAddressOrRange(entry)) throw new ArgumentException("'" + entry + "' is not an IP address or CIDR range.", nameof(TrustedProxies));
                }
                _TrustedProxies = entries;
            }
        }

        /// <summary>
        /// Additional trusted proxy addresses and CIDR ranges, combined with <see cref="TrustedProxies"/>, for example
        /// the CDN's origin-facing ranges when a CDN sits in front of the load balancer. Default empty. Null is treated
        /// as empty.
        /// </summary>
        /// <exception cref="ArgumentException">An entry is not an IP address or CIDR range.</exception>
        public List<string> AdditionalTrustedProxies
        {
            get
            {
                return _AdditionalTrustedProxies;
            }
            set
            {
                List<string> entries = (value ?? new List<string>()).Where(v => !String.IsNullOrWhiteSpace(v)).Select(v => v.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                foreach (string entry in entries)
                {
                    if (!IsAddressOrRange(entry)) throw new ArgumentException("'" + entry + "' is not an IP address or CIDR range.", nameof(AdditionalTrustedProxies));
                }
                _AdditionalTrustedProxies = entries;
            }
        }

        /// <summary>
        /// Header a trusted CDN sets to the viewer's address (ip:port, IPv6 possible). When the socket peer is a trusted
        /// proxy and this header holds a valid address, it names the client before X-Forwarded-For is consulted. Default
        /// CloudFront-Viewer-Address. Empty disables it.
        /// </summary>
        public string ViewerAddressHeader
        {
            get
            {
                return _ViewerAddressHeader;
            }
            set
            {
                _ViewerAddressHeader = (value ?? String.Empty).Trim();
            }
        }

        /// <summary>
        /// Every trusted proxy entry: <see cref="TrustedProxies"/> plus <see cref="AdditionalTrustedProxies"/>.
        /// </summary>
        public IReadOnlyList<string> AllTrustedProxies
        {
            get { return _TrustedProxies.Concat(_AdditionalTrustedProxies).Distinct(StringComparer.OrdinalIgnoreCase).ToList(); }
        }

        /// <summary>
        /// The default trusted proxy ranges.
        /// </summary>
        public static readonly IReadOnlyList<string> DefaultTrustedProxies = new[] { "10.0.0.0/8", "172.16.0.0/12", "192.168.0.0/16", "127.0.0.0/8", "::1", "fc00::/7" };

        #endregion

        #region Private-Members

        private string _Hostname = "127.0.0.1";
        private int _Port = 8000;
        private List<string> _TrustedProxies = DefaultTrustedProxies.ToList();
        private List<string> _AdditionalTrustedProxies = new List<string>();
        private string _ViewerAddressHeader = "CloudFront-Viewer-Address";

        #endregion

        #region Private-Methods

        private static bool IsAddressOrRange(string entry)
        {
            if (entry.Contains('/', StringComparison.Ordinal)) return IPNetwork.TryParse(entry, out IPNetwork _);
            return IPAddress.TryParse(entry, out IPAddress? _);
        }

        #endregion
    }
}
