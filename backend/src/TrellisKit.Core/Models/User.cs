namespace TrellisKit.Core.Models
{
    using System;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// A member account. Deleted accounts are anonymized in place (IsDeleted) rather than removed.
    /// </summary>
    public class User
    {
        #region Public-Members

        /// <summary>
        /// Identifier (prefix usr_).
        /// </summary>
        public string Id { get; set; } = String.Empty;

        /// <summary>
        /// Email address, or null when the provider withheld it or the account was deleted. Unique case-insensitively.
        /// </summary>
        public string? Email { get; set; } = null;

        /// <summary>
        /// Whether the email address is verified.
        /// </summary>
        public bool EmailVerified { get; set; } = false;

        /// <summary>
        /// When the email address was verified (UTC), or null.
        /// </summary>
        public DateTime? EmailVerifiedUtc { get; set; } = null;

        /// <summary>
        /// Display name, 2 to 60 characters.
        /// </summary>
        public string DisplayName { get; set; } = String.Empty;

        /// <summary>
        /// Public handle, 3 to 30 of A-Z a-z 0-9 _ and period, unique case-insensitively, or null.
        /// </summary>
        public string? Handle { get; set; } = null;

        /// <summary>
        /// Stored avatar file name, or null.
        /// </summary>
        public string? AvatarFile { get; set; } = null;

        /// <summary>
        /// Biography, up to 1000 characters, or null.
        /// </summary>
        public string? Bio { get; set; } = null;

        /// <summary>
        /// Rank, or null when not set.
        /// </summary>
        public string? Rank { get; set; } = null;

        /// <summary>
        /// Level within the rank. Default 0. Minimum 0, maximum the property profile.rank.maxLevel.
        /// </summary>
        public int RankLevel
        {
            get { return _RankLevel; }
            set { _RankLevel = Math.Clamp(value, 0, 6); }
        }

        /// <summary>
        /// Affiliation, up to 100 characters, or null.
        /// </summary>
        public string? Affiliation { get; set; } = null;

        /// <summary>
        /// City, up to 100 characters, or null.
        /// </summary>
        public string? City { get; set; } = null;

        /// <summary>
        /// Region or state, up to 100 characters, or null.
        /// </summary>
        public string? Region { get; set; } = null;

        /// <summary>
        /// ISO 3166-1 alpha-2 country code, or null.
        /// </summary>
        public string? Country { get; set; } = null;

        /// <summary>
        /// Free text for the Other goal, or null.
        /// </summary>
        public string? GoalsOther { get; set; } = null;

        /// <summary>
        /// When the member attested they meet the property minimum age (membership.minimumAge) (UTC), or null when not yet attested. The date of birth itself is never stored.
        /// </summary>
        public DateTime? AgeAttestedUtc { get; set; } = null;

        /// <summary>
        /// May create main feed posts. Default false.
        /// </summary>
        public bool CanPostToFeed { get; set; } = false;

        /// <summary>
        /// May promote forum threads into the feed. Default false.
        /// </summary>
        public bool CanPromoteToFeed { get; set; } = false;

        /// <summary>
        /// May attach video. Default true.
        /// </summary>
        public bool CanUploadVideo { get; set; } = true;

        /// <summary>
        /// May send direct messages. Default true.
        /// </summary>
        public bool CanSendDirectMessages { get; set; } = true;

        /// <summary>
        /// Whether a mute is set. Use IsMutedAt to account for MutedUntilUtc.
        /// </summary>
        public bool IsMuted { get; set; } = false;

        /// <summary>
        /// When the mute ends (UTC), or null for indefinite.
        /// </summary>
        public DateTime? MutedUntilUtc { get; set; } = null;

        /// <summary>
        /// Reason for the mute, or null.
        /// </summary>
        public string? MutedReason { get; set; } = null;

        /// <summary>
        /// Whether the account is banned (cannot sign in).
        /// </summary>
        public bool IsBanned { get; set; } = false;

        /// <summary>
        /// Reason for the ban, or null.
        /// </summary>
        public string? BannedReason { get; set; } = null;

        /// <summary>
        /// When the ban was applied (UTC), or null.
        /// </summary>
        public DateTime? BannedUtc { get; set; } = null;

        /// <summary>
        /// Whether the account was deleted and anonymized.
        /// </summary>
        public bool IsDeleted { get; set; } = false;

        /// <summary>
        /// When the account was deleted (UTC), or null.
        /// </summary>
        public DateTime? DeletedUtc { get; set; } = null;

        /// <summary>
        /// Last authenticated activity (UTC), or null.
        /// </summary>
        public DateTime? LastSeenUtc { get; set; } = null;

        /// <summary>
        /// Stripe customer identifier, or null.
        /// </summary>
        public string? StripeCustomerId { get; set; } = null;

        /// <summary>
        /// UUID sent to the stores as appAccountToken (Apple) and obfuscatedAccountId (Google).
        /// </summary>
        public string StoreAccountToken { get; set; } = Guid.NewGuid().ToString("D");

        /// <summary>
        /// Failed password attempts in the current lockout window. Minimum 0.
        /// </summary>
        public int FailedLoginCount
        {
            get { return _FailedLoginCount; }
            set { _FailedLoginCount = Math.Max(0, value); }
        }

        /// <summary>
        /// Start of the current failed-attempt window (UTC), or null.
        /// </summary>
        public DateTime? FailedLoginWindowUtc { get; set; } = null;

        /// <summary>
        /// Password sign-in is locked until this time (UTC), or null.
        /// </summary>
        public DateTime? LockedUntilUtc { get; set; } = null;

        /// <summary>
        /// Created (UTC).
        /// </summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Last updated (UTC).
        /// </summary>
        public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// The member's palette preset preference (an engine preset id), or null for the property's brand palette
        /// (docs/TRELLISKIT.md section 2.1). Written only through IUserMethods.SetPalettePresetAsync.
        /// </summary>
        public string? PalettePreset { get; set; } = null;

        #endregion

        #region Private-Members

        private int _RankLevel = 0;
        private int _FailedLoginCount = 0;

        #endregion

        #region Public-Methods

        /// <summary>
        /// Whether the member is muted at a point in time.
        /// </summary>
        /// <param name="nowUtc">Time (UTC).</param>
        /// <returns>True when muted.</returns>
        public bool IsMutedAt(DateTime nowUtc)
        {
            return IsMuted && (MutedUntilUtc == null || MutedUntilUtc.Value > nowUtc);
        }

        #endregion
    }
}
