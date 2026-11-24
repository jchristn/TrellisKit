namespace TrellisKit.Core.Requests
{
    using System;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// Body of POST /api/v1/auth/facebook and POST /api/v1/me/identities/facebook.
    /// </summary>
    public class FacebookSignInRequest
    {
        #region Public-Members

        /// <summary>
        /// Limited Login JWT or access token.
        /// </summary>
        public string? Token { get; set; } = null;

        /// <summary>
        /// Jwt (iOS Limited Login) or AccessToken (Android and web).
        /// </summary>
        public ExternalTokenTypeEnum? TokenType { get; set; } = null;

        /// <summary>
        /// Raw nonce for iOS Limited Login JWTs, or null (Android and web send none).
        /// </summary>
        public string? Nonce { get; set; } = null;

        #endregion
    }
}
