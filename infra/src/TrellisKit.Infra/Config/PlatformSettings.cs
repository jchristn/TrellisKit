namespace TrellisKit.Infra.Config
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Platform administration of a shared (MultiProperty) deployment: the hosts that serve the platform API and the
    /// first platform administrator. Empty in the per-property topology, where the backend runs SingleProperty and has
    /// no platform realm.
    /// </summary>
    public class PlatformSettings
    {
        private List<string> _Hosts = new List<string>();
        private string _BootstrapAdminEmail = String.Empty;

        /// <summary>
        /// Platform host names of this environment (TK_PLATFORM_HOSTS), each inside one property's primary zone and
        /// routed to the API load balancer with its own certificate. Default empty. Required in the shared topology,
        /// because the backend refuses to start in MultiProperty mode without one.
        /// </summary>
        /// <exception cref="ArgumentNullException">The value is null.</exception>
        public List<string> Hosts
        {
            get { return _Hosts; }
            set { _Hosts = Guard.NotNull(value, nameof(Hosts)); }
        }

        /// <summary>
        /// Email of the first platform administrator (TK_PLATFORM_BOOTSTRAP_ADMIN_EMAIL), or empty to leave the live
        /// setting unset. Default empty. Its password comes only from the platform secret.
        /// </summary>
        /// <exception cref="ArgumentNullException">The value is null.</exception>
        public string BootstrapAdminEmail
        {
            get { return _BootstrapAdminEmail; }
            set { _BootstrapAdminEmail = value ?? throw new ArgumentNullException(nameof(BootstrapAdminEmail)); }
        }
    }
}
