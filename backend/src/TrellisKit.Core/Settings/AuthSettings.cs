namespace TrellisKit.Core.Settings
{
    using System;

    /// <summary>
    /// Authentication and session settings.
    /// </summary>
    public class AuthSettings
    {
        #region Public-Members

        /// <summary>
        /// Break-glass administrator bearer token. Generated on first boot when empty. Never returned by the settings API. Override with TK_ADMIN_TOKEN.
        /// </summary>
        public string AdminToken { get; set; } = String.Empty;

        /// <summary>
        /// Base64 256-bit key that encrypts access tokens. Generated on first boot when empty. Changing it signs everyone out. Secret.
        /// </summary>
        public string TokenEncryptionKey { get; set; } = String.Empty;

        /// <summary>
        /// Email that receives the SystemAdmin role once verified. Empty for none.
        /// </summary>
        public string BootstrapAdminEmail { get; set; } = String.Empty;

        /// <summary>
        /// Initial password of the bootstrap administrator. When this and <see cref="BootstrapAdminEmail"/> are set and the
        /// property has no members, startup creates that account (email verified, SystemAdmin) with this password. Empty
        /// for none. Secret; supply it through the environment (TK_BOOTSTRAP_ADMIN_PASSWORD) outside local development.
        /// </summary>
        public string BootstrapAdminPassword { get; set; } = String.Empty;

        /// <summary>
        /// Minimum admin token length accepted when the token is changed. Default 32. Minimum 16, maximum 256.
        /// </summary>
        public int MinimumAdminTokenLength
        {
            get { return _MinimumAdminTokenLength; }
            set { _MinimumAdminTokenLength = Math.Clamp(value, 16, 256); }
        }

        /// <summary>
        /// Access token lifetime in minutes. Default 60. Minimum 5, maximum 1440.
        /// </summary>
        public int AccessTokenMinutes
        {
            get { return _AccessTokenMinutes; }
            set { _AccessTokenMinutes = Math.Clamp(value, 5, 1440); }
        }

        /// <summary>
        /// Refresh token (session) lifetime in days. Default 90. Minimum 1, maximum 365.
        /// </summary>
        public int RefreshTokenDays
        {
            get { return _RefreshTokenDays; }
            set { _RefreshTokenDays = Math.Clamp(value, 1, 365); }
        }

        /// <summary>
        /// Seconds after a refresh token is rotated during which presenting it again is treated as a concurrent refresh
        /// race (401 without revoking the session) instead of reuse. Default 10. Minimum 0, maximum 120.
        /// </summary>
        public int RefreshReuseGraceSeconds
        {
            get { return _RefreshReuseGraceSeconds; }
            set { _RefreshReuseGraceSeconds = Math.Clamp(value, 0, 120); }
        }

        /// <summary>
        /// Failed password attempts before a temporary lockout. Default 10. Minimum 3, maximum 100.
        /// </summary>
        public int LockoutThreshold
        {
            get { return _LockoutThreshold; }
            set { _LockoutThreshold = Math.Clamp(value, 3, 100); }
        }

        /// <summary>
        /// Lockout window and duration in minutes. Default 15. Minimum 1, maximum 1440.
        /// </summary>
        public int LockoutMinutes
        {
            get { return _LockoutMinutes; }
            set { _LockoutMinutes = Math.Clamp(value, 1, 1440); }
        }

        /// <summary>
        /// Seconds a validated access token's session is cached in memory before the database is checked again.
        /// Revocations made on this instance apply immediately; other instances see them within this window.
        /// Default 30. Minimum 0 (no cache), maximum 300.
        /// </summary>
        public int SessionCacheSeconds
        {
            get { return _SessionCacheSeconds; }
            set { _SessionCacheSeconds = Math.Clamp(value, 0, 300); }
        }

        /// <summary>
        /// Password reset requests allowed per email address and per IP each hour. Default 5. Minimum 1, maximum 100.
        /// </summary>
        public int PasswordResetRequestsPerHour
        {
            get { return _PasswordResetRequestsPerHour; }
            set { _PasswordResetRequestsPerHour = Math.Clamp(value, 1, 100); }
        }

        /// <summary>
        /// Verification email resends allowed per member each hour. Default 5. Minimum 1, maximum 100.
        /// </summary>
        public int VerificationResendsPerHour
        {
            get { return _VerificationResendsPerHour; }
            set { _VerificationResendsPerHour = Math.Clamp(value, 1, 100); }
        }

        #endregion

        #region Private-Members

        private int _MinimumAdminTokenLength = 32;
        private int _AccessTokenMinutes = 60;
        private int _RefreshTokenDays = 90;
        private int _RefreshReuseGraceSeconds = 10;
        private int _LockoutThreshold = 10;
        private int _LockoutMinutes = 15;
        private int _SessionCacheSeconds = 30;
        private int _PasswordResetRequestsPerHour = 5;
        private int _VerificationResendsPerHour = 5;

        #endregion
    }
}
