namespace TrellisKit.Core.Models
{
    using System;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// A single-use emailed token for email verification or password reset, stored as a SHA-256 hash.
    /// </summary>
    public class OneTimeToken
    {
        #region Public-Members

        /// <summary>
        /// Identifier (prefix ott_).
        /// </summary>
        public string Id { get; set; } = String.Empty;

        /// <summary>
        /// Member identifier.
        /// </summary>
        public string UserId { get; set; } = String.Empty;

        /// <summary>
        /// Purpose.
        /// </summary>
        public OneTimeTokenPurposeEnum Purpose { get; set; } = OneTimeTokenPurposeEnum.VerifyEmail;

        /// <summary>
        /// Lowercase hex SHA-256 of the token.
        /// </summary>
        public string TokenHash { get; set; } = String.Empty;

        /// <summary>
        /// Email address the token was sent to, or null.
        /// </summary>
        public string? Email { get; set; } = null;

        /// <summary>
        /// Issued (UTC).
        /// </summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Expiry (UTC).
        /// </summary>
        public DateTime ExpiresUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// When it was used or invalidated (UTC), or null.
        /// </summary>
        public DateTime? UsedUtc { get; set; } = null;

        #endregion
    }
}
