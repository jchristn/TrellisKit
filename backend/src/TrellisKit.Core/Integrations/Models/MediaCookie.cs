namespace TrellisKit.Core.Integrations.Models
{
    using System;
    using System.Text.Json.Serialization;

    /// <summary>
    /// A media grant cookie (contract section 4.7).
    /// </summary>
    public class MediaCookie
    {
        #region Public-Members

        /// <summary>
        /// Cookie name.
        /// </summary>
        public string Name { get; set; } = String.Empty;

        /// <summary>
        /// Cookie value.
        /// </summary>
        public string Value { get; set; } = String.Empty;

        /// <summary>
        /// Domain attribute, or null for a host-only cookie.
        /// </summary>
        public string? Domain { get; set; } = null;

        /// <summary>
        /// Path attribute.
        /// </summary>
        public string Path { get; set; } = "/";

        /// <summary>
        /// Expiry.
        /// </summary>
        public DateTime ExpiresUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Whether Set-Cookie carries the Secure attribute. Not serialized.
        /// </summary>
        [JsonIgnore]
        public bool Secure { get; set; } = true;

        #endregion
    }
}
