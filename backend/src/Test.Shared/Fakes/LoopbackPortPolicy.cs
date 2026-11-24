namespace Test.Shared.Fakes
{
    using System.Net;
    using TrellisKit.Core.Integrations.Implementations;

    /// <summary>
    /// Test-only address policy: the production rules plus 127.0.0.1, with ports 80, 443, and one test server port, so
    /// redirect tests can prove other ports are still refused.
    /// </summary>
    public sealed class LoopbackPortPolicy : PublicAddressPolicy
    {
        private readonly int _Port;

        /// <summary>
        /// Instantiate the policy.
        /// </summary>
        /// <param name="port">The loopback test server port.</param>
        public LoopbackPortPolicy(int port)
        {
            _Port = port;
        }

        /// <inheritdoc />
        public override bool IsAllowedAddress(IPAddress address)
        {
            return IPAddress.Loopback.Equals(address) || base.IsAllowedAddress(address);
        }

        /// <inheritdoc />
        public override bool IsAllowedPort(int port)
        {
            return port == _Port || base.IsAllowedPort(port);
        }
    }
}
