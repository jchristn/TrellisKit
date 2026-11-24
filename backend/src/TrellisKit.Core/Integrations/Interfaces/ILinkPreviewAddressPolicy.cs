namespace TrellisKit.Core.Integrations.Interfaces
{
    using System.Net;

    /// <summary>
    /// Decides which destination addresses and ports a link preview fetch may connect to. Production uses
    /// PublicAddressPolicy (public internet addresses, ports 80 and 443); tests may substitute a policy that also
    /// allows a loopback test server. Thread safety: implementations are safe to call concurrently.
    /// </summary>
    public interface ILinkPreviewAddressPolicy
    {
        /// <summary>
        /// Whether a resolved address may be connected to.
        /// </summary>
        /// <param name="address">Address.</param>
        /// <returns>True when allowed.</returns>
        bool IsAllowedAddress(IPAddress address);

        /// <summary>
        /// Whether a destination port may be connected to.
        /// </summary>
        /// <param name="port">Port.</param>
        /// <returns>True when allowed.</returns>
        bool IsAllowedPort(int port);
    }
}
