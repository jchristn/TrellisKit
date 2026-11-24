namespace TrellisKit.Core.Responses
{
    using System;

    /// <summary>
    /// Returned by every sign-in, registration, and refresh call.
    /// </summary>
    public class AuthResponse
    {
        #region Public-Members

        /// <summary>
        /// Opaque access token.
        /// </summary>
        public string AccessToken { get; set; } = String.Empty;

        /// <summary>
        /// Access token expiry (UTC).
        /// </summary>
        public DateTime AccessTokenExpiresUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Refresh token for mobile and other clients; null for web (sent as the tk_refresh cookie).
        /// </summary>
        public string? RefreshToken { get; set; } = null;

        /// <summary>
        /// Whether this call created the account.
        /// </summary>
        public bool IsNewAccount { get; set; } = false;

        /// <summary>
        /// The member.
        /// </summary>
        public MeResponse User { get; set; } = new MeResponse();

        #endregion
    }
}
