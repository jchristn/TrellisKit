namespace TrellisKit.Core.Integrations.Implementations
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Net;
    using System.Net.Sockets;
    using TrellisKit.Core.Integrations.Interfaces;

    /// <summary>
    /// Production address policy for link preview fetches (contract section 5.9): only public internet addresses on
    /// ports 80 and 443. Refused IPv4: 0.0.0.0/8, 10.0.0.0/8, 100.64.0.0/10 (CGNAT), 127.0.0.0/8, 169.254.0.0/16
    /// (link-local and cloud metadata), 172.16.0.0/12, 192.0.0.0/24, 192.0.2.0/24, 192.88.99.0/24, 192.168.0.0/16,
    /// 198.18.0.0/15 (benchmarking), 198.51.100.0/24, 203.0.113.0/24, 224.0.0.0/4 (multicast), and 240.0.0.0/4
    /// (reserved, including the 255.255.255.255 broadcast). IPv6 must be global unicast (2000::/3), so loopback,
    /// unspecified, link-local fe80::/10, site-local, unique-local fc00::/7 (including fd00:ec2::254), and multicast
    /// ff00::/8 are refused, as are 2001::/23 (IETF assignments), 2001:db8::/32 and 3fff::/20 (documentation), and
    /// the IPv4-compatible ::/96 block. IPv4-mapped (::ffff:0:0/96), NAT64 (64:ff9b::/96), 6to4 (2002::/16), and
    /// Teredo (2001::/32, server and de-obfuscated client) addresses are checked by their embedded IPv4 addresses.
    /// Thread safety: immutable; safe to share.
    /// </summary>
    public class PublicAddressPolicy : ILinkPreviewAddressPolicy
    {
        #region Private-Members

        private static readonly List<IpNetwork> _BlockedV4 = new[]
        {
            "0.0.0.0/8", "10.0.0.0/8", "100.64.0.0/10", "127.0.0.0/8", "169.254.0.0/16", "172.16.0.0/12", "192.0.0.0/24", "192.0.2.0/24",
            "192.88.99.0/24", "192.168.0.0/16", "198.18.0.0/15", "198.51.100.0/24", "203.0.113.0/24", "224.0.0.0/4", "240.0.0.0/4"
        }.Select(IpNetwork.Parse).ToList();

        private static readonly List<IpNetwork> _BlockedV6 = new[]
        {
            "2001::/23", "2001:db8::/32", "3fff::/20"
        }.Select(IpNetwork.Parse).ToList();

        private static readonly IpNetwork _GlobalUnicast = IpNetwork.Parse("2000::/3");
        private static readonly IpNetwork _Nat64 = IpNetwork.Parse("64:ff9b::/96");
        private static readonly IpNetwork _SixToFour = IpNetwork.Parse("2002::/16");
        private static readonly IpNetwork _Teredo = IpNetwork.Parse("2001::/32");
        private static readonly IpNetwork _Compatible = IpNetwork.Parse("::/96");

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public virtual bool IsAllowedAddress(IPAddress address)
        {
            return IsPublic(address);
        }

        /// <inheritdoc />
        public virtual bool IsAllowedPort(int port)
        {
            return port == 80 || port == 443;
        }

        /// <summary>
        /// Whether an address is on the public internet under the rules in the class summary.
        /// </summary>
        /// <param name="address">Address, or null (never public).</param>
        /// <returns>True when public.</returns>
        public static bool IsPublic(IPAddress? address)
        {
            if (address == null) return false;
            if (address.AddressFamily == AddressFamily.InterNetwork) return IsPublicV4(address.GetAddressBytes());
            if (address.AddressFamily != AddressFamily.InterNetworkV6) return false;
            if (address.IsIPv4MappedToIPv6) return IsPublicV4(address.MapToIPv4().GetAddressBytes());
            byte[] bytes = address.GetAddressBytes();
            if (_Compatible.Contains(bytes)) return false;
            if (_Nat64.Contains(bytes)) return IsPublicV4(Slice(bytes, 12, false));
            if (_SixToFour.Contains(bytes)) return IsPublicV4(Slice(bytes, 2, false));
            if (_Teredo.Contains(bytes)) return IsPublicV4(Slice(bytes, 4, false)) && IsPublicV4(Slice(bytes, 12, true));
            if (!_GlobalUnicast.Contains(bytes)) return false;
            return !_BlockedV6.Any(n => n.Contains(bytes));
        }

        #endregion

        #region Private-Methods

        private static bool IsPublicV4(byte[] bytes)
        {
            return bytes.Length == 4 && !_BlockedV4.Any(n => n.Contains(bytes));
        }

        private static byte[] Slice(byte[] bytes, int offset, bool invert)
        {
            byte[] result = new byte[4];
            for (int i = 0; i < 4; i++) result[i] = invert ? (byte)~bytes[offset + i] : bytes[offset + i];
            return result;
        }

        #endregion
    }
}
