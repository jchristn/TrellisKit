namespace TrellisKit.Core.Settings
{
    using System;
    using System.Collections.Generic;
    using System.Linq;

    /// <summary>
    /// Platform administration settings (MultiProperty only; docs/MULTIPROPERTY_CONTRACT.md sections 6 and 12).
    /// </summary>
    public class PlatformSettings
    {
        #region Public-Members

        /// <summary>
        /// Host names that serve the platform realm (platform routes and platform sign-in). Lowercase, without ports.
        /// Default empty; MultiProperty requires at least one.
        /// </summary>
        public List<string> Hosts
        {
            get { return _Hosts; }
            set
            {
                _Hosts = (value ?? new List<string>())
                    .Where(h => !String.IsNullOrWhiteSpace(h))
                    .Select(h => h.Trim().TrimEnd('.').ToLowerInvariant())
                    .Distinct(StringComparer.Ordinal)
                    .ToList();
            }
        }

        /// <summary>
        /// Email of the first platform administrator, created at startup when no platform administrator exists.
        /// Default empty (none).
        /// </summary>
        public string BootstrapAdminEmail { get; set; } = String.Empty;

        /// <summary>
        /// Initial password of the bootstrap platform administrator (supply through TK_PLATFORM_BOOTSTRAP_ADMIN_PASSWORD,
        /// never in a file). Default empty: an invitation token is created and logged once instead.
        /// </summary>
        public string BootstrapAdminPassword { get; set; } = String.Empty;

        /// <summary>
        /// Lifetime of a platform administrator sign-in session in minutes. Default 120. Minimum 5, maximum 1440.
        /// </summary>
        public int SessionMinutes
        {
            get { return _SessionMinutes; }
            set { _SessionMinutes = Math.Clamp(value, 5, 1440); }
        }

        /// <summary>
        /// Lifetime of a delegated session (a platform administrator acting as SystemAdmin inside one property) in
        /// minutes. Default 60. Minimum 5, maximum 480. Delegated sessions cannot be refreshed.
        /// </summary>
        public int DelegatedSessionMinutes
        {
            get { return _DelegatedSessionMinutes; }
            set { _DelegatedSessionMinutes = Math.Clamp(value, 5, 480); }
        }

        /// <summary>
        /// Hours a deleted property stays restorable before its data is purged. Default 720 (30 days). Minimum 1,
        /// maximum 720.
        /// </summary>
        public int PropertyPurgeDelayHours
        {
            get { return _PropertyPurgeDelayHours; }
            set { _PropertyPurgeDelayHours = Math.Clamp(value, 1, 720); }
        }

        /// <summary>
        /// Days an invitation to become a platform administrator stays valid. Default 7. Minimum 1, maximum 30.
        /// </summary>
        public int InviteDays
        {
            get { return _InviteDays; }
            set { _InviteDays = Math.Clamp(value, 1, 30); }
        }

        #endregion

        #region Private-Members

        private List<string> _Hosts = new List<string>();
        private int _SessionMinutes = 120;
        private int _DelegatedSessionMinutes = 60;
        private int _PropertyPurgeDelayHours = 720;
        private int _InviteDays = 7;

        #endregion

        #region Public-Methods

        /// <summary>
        /// Whether a normalized host is a platform host.
        /// </summary>
        /// <param name="host">Lowercase host without port.</param>
        /// <returns>True when listed in <see cref="Hosts"/>.</returns>
        public bool IsPlatformHost(string? host)
        {
            return !String.IsNullOrEmpty(host) && _Hosts.Contains(host);
        }

        #endregion
    }
}
