namespace TrellisKit.Core.Models
{
    using System;

    /// <summary>
    /// A platform administrator sign-in session (deployment-level). The bearer token is opaque; only its SHA-256 is stored.
    /// </summary>
    public class PlatformSession
    {
        #region Public-Members

        /// <summary>
        /// Identifier (pss_).
        /// </summary>
        public string Id { get; set; } = String.Empty;

        /// <summary>
        /// Platform administrator.
        /// </summary>
        public string AdminId { get; set; } = String.Empty;

        /// <summary>
        /// SHA-256 (hex) of the bearer token.
        /// </summary>
        public string TokenHash { get; set; } = String.Empty;

        /// <summary>
        /// Source IP at sign-in.
        /// </summary>
        public string? SourceIp { get; set; } = null;

        /// <summary>
        /// User agent at sign-in.
        /// </summary>
        public string? UserAgent { get; set; } = null;

        /// <summary>
        /// Created time.
        /// </summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Last use.
        /// </summary>
        public DateTime LastUsedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Absolute expiry (platform.sessionMinutes).
        /// </summary>
        public DateTime ExpiresUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// When revoked (sign-out or administrator removal).
        /// </summary>
        public DateTime? RevokedUtc { get; set; } = null;

        #endregion
    }
}
