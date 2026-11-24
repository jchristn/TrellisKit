namespace TrellisKit.Infra.Config
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Everything one synthesis deploys for one environment: the topology, the configuration of each property, and,
    /// in the shared topology, the configuration of the shared stacks. Build instances with
    /// <see cref="EnvironmentConfigFactory.DeploymentFromContext"/> or <see cref="EnvironmentConfigFactory.CreateDeployment"/>.
    /// </summary>
    public class DeploymentConfig
    {
        private List<EnvironmentConfig> _Properties = new List<EnvironmentConfig>();

        /// <summary>
        /// Topology. Default <see cref="DeploymentTopologyEnum.PerProperty"/>.
        /// </summary>
        public DeploymentTopologyEnum Topology { get; set; } = DeploymentTopologyEnum.PerProperty;

        /// <summary>
        /// Configuration of the shared stacks (shared topology only), or null.
        /// </summary>
        public EnvironmentConfig? Shared { get; set; } = null;

        /// <summary>
        /// Configuration of each property, in deployment file order.
        /// </summary>
        /// <exception cref="ArgumentNullException">The value is null.</exception>
        public List<EnvironmentConfig> Properties
        {
            get { return _Properties; }
            set { _Properties = value ?? throw new ArgumentNullException(nameof(Properties)); }
        }

        /// <summary>
        /// The shared configuration (when present) followed by every property configuration.
        /// </summary>
        public List<EnvironmentConfig> All
        {
            get
            {
                List<EnvironmentConfig> all = new List<EnvironmentConfig>();
                if (Shared != null) all.Add(Shared);
                all.AddRange(_Properties);
                return all;
            }
        }
    }
}
