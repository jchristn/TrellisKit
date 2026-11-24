namespace TrellisKit.Core.Models
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// A pseudonymous app installation seen by the manifest endpoint (table updateinstalls).
    /// </summary>
    public class UpdateInstall
    {
        /// <summary>
        /// Identifier (uin_).
        /// </summary>
        public string Id { get; set; } = String.Empty;

        /// <summary>
        /// HMAC-SHA256 hex of the install id.
        /// </summary>
        public string InstallIdHash { get; set; } = String.Empty;

        /// <summary>
        /// ios or android.
        /// </summary>
        public string Platform { get; set; } = String.Empty;

        /// <summary>
        /// Channel of the last check.
        /// </summary>
        public string Channel { get; set; } = String.Empty;

        /// <summary>
        /// Runtime version of the last check.
        /// </summary>
        public string RuntimeVersion { get; set; } = String.Empty;

        /// <summary>
        /// Update the app ran at the last check.
        /// </summary>
        public string? CurrentUpdateUuid { get; set; } = null;

        /// <summary>
        /// Embedded update id.
        /// </summary>
        public string? EmbeddedUpdateUuid { get; set; } = null;

        /// <summary>
        /// Embedded certificate key id.
        /// </summary>
        public string? KeyId { get; set; } = null;

        /// <summary>
        /// Native app version.
        /// </summary>
        public string? AppVersion { get; set; } = null;

        /// <summary>
        /// First check.
        /// </summary>
        public DateTime FirstSeenUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Last check.
        /// </summary>
        public DateTime LastCheckUtc { get; set; } = DateTime.UtcNow;
    }
}
