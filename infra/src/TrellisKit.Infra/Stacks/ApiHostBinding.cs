namespace TrellisKit.Infra.Stacks
{
    using System;
    using TrellisKit.Infra.Config;

    /// <summary>
    /// One property API host served by the API load balancer: its regional certificate and alias record live in the
    /// property's hosted zone. The per-property topology has one binding; the shared topology has one per property.
    /// </summary>
    public class ApiHostBinding
    {
        private DnsStack? _Dns;
        private EnvironmentConfig? _Property;
        private string _ConstructSuffix = String.Empty;

        /// <summary>
        /// The property's hosted zones and API certificate.
        /// </summary>
        /// <exception cref="InvalidOperationException">Read before it was set.</exception>
        /// <exception cref="ArgumentNullException">The value is null.</exception>
        public DnsStack Dns
        {
            get { return _Dns ?? throw new InvalidOperationException("Dns is not set."); }
            set { _Dns = value ?? throw new ArgumentNullException(nameof(Dns)); }
        }

        /// <summary>
        /// The property's configuration (API host name).
        /// </summary>
        /// <exception cref="InvalidOperationException">Read before it was set.</exception>
        /// <exception cref="ArgumentNullException">The value is null.</exception>
        public EnvironmentConfig Property
        {
            get { return _Property ?? throw new InvalidOperationException("Property is not set."); }
            set { _Property = value ?? throw new ArgumentNullException(nameof(Property)); }
        }

        /// <summary>
        /// Appended to the construct ids of the alias record so several bindings can share one stack. Empty in the
        /// per-property topology (keeps the original ids). Default empty.
        /// </summary>
        /// <exception cref="ArgumentNullException">The value is null.</exception>
        public string ConstructSuffix
        {
            get { return _ConstructSuffix; }
            set { _ConstructSuffix = value ?? throw new ArgumentNullException(nameof(ConstructSuffix)); }
        }
    }
}
