namespace TrellisKit.Core.Models
{
    using System;

    /// <summary>
    /// A single-use refresh token, stored as a SHA-256 hash. Using a token twice revokes its session.
    /// </summary>
    public class RefreshToken
    {
        #region Public-Members

        /// <summary>
        /// Identifier (prefix rft_).
        /// </summary>
        public string Id { get; set; } = String.Empty;

        /// <summary>
        /// Session identifier.
        /// </summary>
        public string SessionId { get; set; } = String.Empty;

        /// <summary>
        /// Member identifier.
        /// </summary>
        public string UserId { get; set; } = String.Empty;

        /// <summary>
        /// Lowercase hex SHA-256 of the token.
        /// </summary>
        public string TokenHash { get; set; } = String.Empty;

        /// <summary>
        /// Issued (UTC).
        /// </summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Expiry (UTC).
        /// </summary>
        public DateTime ExpiresUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// When it was rotated (UTC), or null while unused.
        /// </summary>
        public DateTime? UsedUtc { get; set; } = null;

        /// <summary>
        /// Identifier of the token that replaced it, or null.
        /// </summary>
        public string? ReplacedById { get; set; } = null;

        #endregion
    }
}
