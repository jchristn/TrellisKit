namespace Test.Shared.Fakes
{
    using System.Net;
    using TrellisKit.Core.Integrations.Implementations;

    /// <summary>
    /// Test-only address policy: the production public-address rules plus 127.0.0.1 on any port, so a fetch can reach
    /// a loopback test server. Production code never uses it.
    /// </summary>
    public sealed class LoopbackTestAddressPolicy : PublicAddressPolicy
    {
        /// <inheritdoc />
        public override bool IsAllowedAddress(IPAddress address)
        {
            return IPAddress.Loopback.Equals(address) || base.IsAllowedAddress(address);
        }

        /// <inheritdoc />
        public override bool IsAllowedPort(int port)
        {
            return port > 0;
        }
    }
}
