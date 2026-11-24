namespace TrellisKit.Infra.Stacks
{
    using System;
    using Amazon.CDK.AWS.CertificateManager;

    /// <summary>
    /// One platform host (shared topology) served by the API load balancer: its regional certificate and alias record
    /// live in the hosted zone of the property whose primary domain contains it.
    /// </summary>
    public class PlatformHostBinding
    {
        private DnsStack? _Dns;
        private string _Host = String.Empty;
        private string _ConstructSuffix = String.Empty;

        /// <summary>
        /// Hosted zones of the property whose primary domain contains the host.
        /// </summary>
        /// <exception cref="InvalidOperationException">Read before it was set.</exception>
        /// <exception cref="ArgumentNullException">The value is null.</exception>
        public DnsStack Dns
        {
            get { return _Dns ?? throw new InvalidOperationException("Dns is not set."); }
            set { _Dns = value ?? throw new ArgumentNullException(nameof(Dns)); }
        }

        /// <summary>
        /// Platform host name of this environment, for example platform.dev.example.test.
        /// </summary>
        /// <exception cref="ArgumentNullException">The value is empty.</exception>
        public string Host
        {
            get { return _Host; }
            set
            {
                if (String.IsNullOrWhiteSpace(value)) throw new ArgumentNullException(nameof(Host));
                _Host = value;
            }
        }

        /// <summary>
        /// Certificate requested ahead of the API (external DNS mode: by the owning property's DNS stack, so its
        /// validation records are outputs there), or null to have the API stack create it validated through the
        /// property's hosted zone. Default null.
        /// </summary>
        public ICertificate? Certificate { get; set; } = null;

        /// <summary>
        /// Appended to the construct ids of the certificate and alias record. Default empty.
        /// </summary>
        /// <exception cref="ArgumentNullException">The value is null.</exception>
        public string ConstructSuffix
        {
            get { return _ConstructSuffix; }
            set { _ConstructSuffix = value ?? throw new ArgumentNullException(nameof(ConstructSuffix)); }
        }
    }
}
