namespace TrellisKit.Server.Services
{
    using System;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Responses;

    /// <summary>
    /// A newly created or refreshed session: the response body plus the raw refresh token for the cookie.
    /// </summary>
    public class IssuedSession
    {
        #region Public-Members

        /// <summary>
        /// Response body (its RefreshToken is null for web clients).
        /// </summary>
        public AuthResponse Response { get; set; } = new AuthResponse();

        /// <summary>
        /// Raw refresh token.
        /// </summary>
        public string RefreshToken { get; set; } = String.Empty;

        /// <summary>
        /// Refresh token expiry (UTC).
        /// </summary>
        public DateTime RefreshExpiresUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Client type.
        /// </summary>
        public ClientTypeEnum ClientType { get; set; } = ClientTypeEnum.Other;

        #endregion
    }
}
