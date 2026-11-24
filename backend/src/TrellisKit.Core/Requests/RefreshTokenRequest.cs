namespace TrellisKit.Core.Requests
{
    using System;

    /// <summary>
    /// Body of POST /api/v1/auth/refresh and POST /api/v1/auth/logout for mobile clients (web clients send no body and use the cookie).
    /// </summary>
    public class RefreshTokenRequest
    {
        #region Public-Members

        /// <summary>
        /// Refresh token.
        /// </summary>
        public string? RefreshToken { get; set; } = null;

        #endregion
    }
}
