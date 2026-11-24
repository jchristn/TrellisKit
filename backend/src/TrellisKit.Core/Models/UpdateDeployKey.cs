namespace TrellisKit.Core.Models
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// A CI deploy key that may create and publish update groups on its channels (table updatedeploykeys).
    /// </summary>
    public class UpdateDeployKey
    {
        /// <summary>
        /// Identifier (udk_).
        /// </summary>
        public string Id { get; set; } = String.Empty;

        /// <summary>
        /// Name shown in audit records.
        /// </summary>
        public string Name { get; set; } = String.Empty;

        /// <summary>
        /// SHA-256 hex of the secret.
        /// </summary>
        public string SecretHash { get; set; } = String.Empty;

        /// <summary>
        /// Channels the key may publish to.
        /// </summary>
        public List<string> Channels { get; set; } = new List<string>();

        /// <summary>
        /// Expiry, or null.
        /// </summary>
        public DateTime? ExpiresUtc { get; set; } = null;

        /// <summary>
        /// Last use, or null.
        /// </summary>
        public DateTime? LastUsedUtc { get; set; } = null;

        /// <summary>
        /// Revocation time, or null.
        /// </summary>
        public DateTime? RevokedUtc { get; set; } = null;

        /// <summary>
        /// Creator, or null for the admin token.
        /// </summary>
        public string? CreatedByUserId { get; set; } = null;

        /// <summary>
        /// Created time.
        /// </summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    }
}
