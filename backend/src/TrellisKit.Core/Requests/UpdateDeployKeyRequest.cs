namespace TrellisKit.Core.Requests
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Body of POST /api/v1/admin/updates/deploy-keys.
    /// </summary>
    public class UpdateDeployKeyRequest
    {
        /// <summary>
        /// Name (1 to 100 characters).
        /// </summary>
        public string Name { get; set; } = String.Empty;

        /// <summary>
        /// Channels the key may publish to (never production).
        /// </summary>
        public List<string> Channels { get; set; } = new List<string>();

        /// <summary>
        /// Expiry, or null.
        /// </summary>
        public DateTime? ExpiresUtc { get; set; } = null;

        /// <summary>
        /// Labels, or null.
        /// </summary>
        public List<string>? Labels { get; set; } = null;

        /// <summary>
        /// Tags, or null.
        /// </summary>
        public Dictionary<string, string>? Tags { get; set; } = null;
    }
}
