namespace TrellisKit.Core.Models
{
    using System;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// A sign-in session. Access tokens reference it; refresh tokens rotate within it.
    /// </summary>
    public class AuthSession
    {
        #region Public-Members

        /// <summary>
        /// Identifier (prefix ses_).
        /// </summary>
        public string Id { get; set; } = String.Empty;

        /// <summary>
        /// Member identifier.
        /// </summary>
        public string UserId { get; set; } = String.Empty;

        /// <summary>
        /// Client that created the session.
        /// </summary>
        public ClientTypeEnum ClientType { get; set; } = ClientTypeEnum.Other;

        /// <summary>
        /// Source IP at sign-in, or null.
        /// </summary>
        public string? SourceIp { get; set; } = null;

        /// <summary>
        /// User agent at sign-in (truncated to 512 characters), or null.
        /// </summary>
        public string? UserAgent { get; set; } = null;

        /// <summary>
        /// Created (UTC).
        /// </summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Last used (UTC).
        /// </summary>
        public DateTime LastUsedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Absolute expiry (UTC); refreshing does not extend it past the refresh token lifetime.
        /// </summary>
        public DateTime ExpiresUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// When revoked (UTC), or null while active.
        /// </summary>
        public DateTime? RevokedUtc { get; set; } = null;

        /// <summary>
        /// Why it was revoked, or null.
        /// </summary>
        public string? RevokedReason { get; set; } = null;

        /// <summary>
        /// Principal kind: Member for member sessions, Delegated for a platform administrator acting as SystemAdmin in
        /// this property (UserId then holds the platform administrator id, pad_).
        /// </summary>
        public PrincipalKindEnum PrincipalKind { get; set; } = PrincipalKindEnum.Member;

        #endregion

        #region Public-Methods

        /// <summary>
        /// Whether the session is usable at a point in time.
        /// </summary>
        /// <param name="nowUtc">Time (UTC).</param>
        /// <returns>True when not revoked and not expired.</returns>
        public bool IsActiveAt(DateTime nowUtc)
        {
            return RevokedUtc == null && ExpiresUtc > nowUtc;
        }

        #endregion
    }
}
