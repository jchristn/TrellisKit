namespace TrellisKit.Infra.Stacks
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Stacks that <see cref="ApiStack"/> reads from.
    /// </summary>
    public class ApiStackDependencies
    {
        private NetworkStack? _Network;
        private List<ApiHostBinding> _Hosts = new List<ApiHostBinding>();
        private List<PlatformHostBinding> _PlatformHosts = new List<PlatformHostBinding>();
        private DataStack? _Data;
        private StorageStack? _Storage;
        private RegistryStack? _Registry;
        private ObservabilityStack? _Observability;
        private MediaPipeline? _Media;

        /// <summary>
        /// VPC and security groups.
        /// </summary>
        /// <exception cref="InvalidOperationException">Read before it was set.</exception>
        public NetworkStack Network
        {
            get { return _Network ?? throw new InvalidOperationException("Network is not set."); }
            set { _Network = value ?? throw new ArgumentNullException(nameof(Network)); }
        }

        /// <summary>
        /// API hosts served by the load balancer (certificate and alias record each). The first is the listener's
        /// default certificate. At least one is required.
        /// </summary>
        /// <exception cref="ArgumentNullException">The value is null.</exception>
        public List<ApiHostBinding> Hosts
        {
            get { return _Hosts; }
            set { _Hosts = value ?? throw new ArgumentNullException(nameof(Hosts)); }
        }

        /// <summary>
        /// Platform hosts served by the load balancer (shared topology; certificate and alias record each). Default empty.
        /// </summary>
        /// <exception cref="ArgumentNullException">The value is null.</exception>
        public List<PlatformHostBinding> PlatformHosts
        {
            get { return _PlatformHosts; }
            set { _PlatformHosts = value ?? throw new ArgumentNullException(nameof(PlatformHosts)); }
        }

        /// <summary>
        /// Database and its credentials.
        /// </summary>
        /// <exception cref="InvalidOperationException">Read before it was set.</exception>
        public DataStack Data
        {
            get { return _Data ?? throw new InvalidOperationException("Data is not set."); }
            set { _Data = value ?? throw new ArgumentNullException(nameof(Data)); }
        }

        /// <summary>
        /// Buckets.
        /// </summary>
        /// <exception cref="InvalidOperationException">Read before it was set.</exception>
        public StorageStack Storage
        {
            get { return _Storage ?? throw new InvalidOperationException("Storage is not set."); }
            set { _Storage = value ?? throw new ArgumentNullException(nameof(Storage)); }
        }

        /// <summary>
        /// Image repository.
        /// </summary>
        /// <exception cref="InvalidOperationException">Read before it was set.</exception>
        public RegistryStack Registry
        {
            get { return _Registry ?? throw new InvalidOperationException("Registry is not set."); }
            set { _Registry = value ?? throw new ArgumentNullException(nameof(Registry)); }
        }

        /// <summary>
        /// Log groups and the Prometheus workspace.
        /// </summary>
        /// <exception cref="InvalidOperationException">Read before it was set.</exception>
        public ObservabilityStack Observability
        {
            get { return _Observability ?? throw new InvalidOperationException("Observability is not set."); }
            set { _Observability = value ?? throw new ArgumentNullException(nameof(Observability)); }
        }

        /// <summary>
        /// Media signing key and MediaConvert role.
        /// </summary>
        /// <exception cref="InvalidOperationException">Read before it was set.</exception>
        public MediaPipeline Media
        {
            get { return _Media ?? throw new InvalidOperationException("Media is not set."); }
            set { _Media = value ?? throw new ArgumentNullException(nameof(Media)); }
        }
    }
}
