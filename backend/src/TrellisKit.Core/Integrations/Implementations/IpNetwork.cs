namespace TrellisKit.Core.Integrations.Implementations
{
    using System;
    using System.Globalization;
    using System.Net;

    /// <summary>
    /// An IPv4 or IPv6 network in CIDR form, for address range checks.
    /// Thread safety: immutable; safe to share.
    /// </summary>
    public sealed class IpNetwork
    {
        #region Public-Members

        /// <summary>
        /// Network address bytes (4 for IPv4, 16 for IPv6).
        /// </summary>
        public byte[] Network { get; }

        /// <summary>
        /// Prefix length in bits.
        /// </summary>
        public int PrefixLength { get; }

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate from an address and prefix length.
        /// </summary>
        /// <param name="network">Network address.</param>
        /// <param name="prefixLength">Prefix length (0 to 32 for IPv4, 0 to 128 for IPv6).</param>
        /// <exception cref="ArgumentNullException">The address is null.</exception>
        /// <exception cref="ArgumentOutOfRangeException">The prefix length is out of range.</exception>
        public IpNetwork(IPAddress network, int prefixLength)
        {
            ArgumentNullException.ThrowIfNull(network);
            Network = network.GetAddressBytes();
            if (prefixLength < 0 || prefixLength > Network.Length * 8) throw new ArgumentOutOfRangeException(nameof(prefixLength), "Prefix length " + prefixLength + " is out of range for " + network + ".");
            PrefixLength = prefixLength;
        }

        /// <summary>
        /// Parse CIDR text such as 10.0.0.0/8 or fc00::/7.
        /// </summary>
        /// <param name="cidr">CIDR text.</param>
        /// <returns>Network.</returns>
        /// <exception cref="ArgumentNullException">The text is null or empty.</exception>
        /// <exception cref="FormatException">The text is not valid CIDR.</exception>
        public static IpNetwork Parse(string cidr)
        {
            if (String.IsNullOrWhiteSpace(cidr)) throw new ArgumentNullException(nameof(cidr));
            int slash = cidr.IndexOf('/');
            if (slash <= 0) throw new FormatException("Not CIDR: " + cidr);
            if (!IPAddress.TryParse(cidr.Substring(0, slash), out IPAddress? address)) throw new FormatException("Not an address: " + cidr);
            if (!Int32.TryParse(cidr.Substring(slash + 1), NumberStyles.Integer, CultureInfo.InvariantCulture, out int prefix)) throw new FormatException("Not a prefix: " + cidr);
            return new IpNetwork(address, prefix);
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Whether the address bytes fall inside the network (same family only).
        /// </summary>
        /// <param name="address">Address bytes.</param>
        /// <returns>True when contained.</returns>
        public bool Contains(byte[] address)
        {
            if (address == null || address.Length != Network.Length) return false;
            int full = PrefixLength / 8;
            for (int i = 0; i < full; i++)
            {
                if (address[i] != Network[i]) return false;
            }
            int rest = PrefixLength % 8;
            if (rest == 0) return true;
            int mask = (0xFF << (8 - rest)) & 0xFF;
            return (address[full] & mask) == (Network[full] & mask);
        }

        /// <inheritdoc />
        public override string ToString()
        {
            return new IPAddress(Network) + "/" + PrefixLength.ToString(CultureInfo.InvariantCulture);
        }

        #endregion
    }
}
