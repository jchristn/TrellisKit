namespace TrellisKit.Core.Models
{
    using System;

    /// <summary>
    /// A platform administrator. Stored in its own deployment-level table, separate from every property's member accounts (resolved decision 1).
    /// </summary>
    public class PlatformAdmin
    {
        #region Public-Members

        /// <summary>
        /// Identifier (pad_).
        /// </summary>
        public string Id { get; set; } = String.Empty;

        /// <summary>
        /// Sign-in email (unique among active platform administrators).
        /// </summary>
        public string Email { get; set; } = String.Empty;

        /// <summary>
        /// Display name, recorded as the actor in audits.
        /// </summary>
        public string DisplayName { get; set; } = String.Empty;

        /// <summary>
        /// Password hash, or null until the invitation is accepted.
        /// </summary>
        public string? PasswordHash { get; set; } = null;

        /// <summary>
        /// SHA-256 of the pending invitation token, or null.
        /// </summary>
        public string? InviteTokenHash { get; set; } = null;

        /// <summary>
        /// Invitation expiry.
        /// </summary>
        public DateTime? InviteExpiresUtc { get; set; } = null;

        /// <summary>
        /// Last successful sign-in.
        /// </summary>
        public DateTime? LastLoginUtc { get; set; } = null;

        /// <summary>
        /// Failed sign-ins in the current window.
        /// </summary>
        public int FailedLoginCount { get; set; } = 0;

        /// <summary>
        /// Locked until this time after too many failures.
        /// </summary>
        public DateTime? LockedUntilUtc { get; set; } = null;

        /// <summary>
        /// When removed (removed administrators keep their row for audit references).
        /// </summary>
        public DateTime? RemovedUtc { get; set; } = null;

        /// <summary>
        /// Created time.
        /// </summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Last change time.
        /// </summary>
        public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;

        #endregion
    }
}
