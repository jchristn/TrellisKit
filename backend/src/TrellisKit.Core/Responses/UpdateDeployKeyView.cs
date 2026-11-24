namespace TrellisKit.Core.Responses
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// UpdateDeployKey payload. Secret is returned only once, on create.
    /// </summary>
    public class UpdateDeployKeyView
    {
        /// <summary>
        /// Identifier (udk_).
        /// </summary>
        public string Id { get; set; } = String.Empty;

        /// <summary>
        /// Name.
        /// </summary>
        public string Name { get; set; } = String.Empty;

        /// <summary>
        /// Channels (at most updates.maxChannels plus 3).
        /// </summary>
        public List<string> Channels { get; set; } = new List<string>();

        /// <summary>
        /// Not revoked and not expired.
        /// </summary>
        public bool Active { get; set; } = false;

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
        /// The tkdk_ secret (create response only), else null.
        /// </summary>
        public string? Secret { get; set; } = null;

        /// <summary>
        /// Labels.
        /// </summary>
        public List<string> Labels { get; set; } = new List<string>();

        /// <summary>
        /// Tags.
        /// </summary>
        public Dictionary<string, string> Tags { get; set; } = new Dictionary<string, string>(StringComparer.Ordinal);

        /// <summary>
        /// Created time.
        /// </summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    }
}
