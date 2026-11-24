namespace TrellisKit.Core.Requests
{
    using System;

    /// <summary>
    /// Body of POST /api/v1/auth/apple and POST /api/v1/me/identities/apple.
    /// </summary>
    public class AppleSignInRequest
    {
        #region Public-Members

        /// <summary>
        /// Identity token JWT from Sign in with Apple.
        /// </summary>
        public string? IdentityToken { get; set; } = null;

        /// <summary>
        /// Raw nonce the client generated (its SHA-256 is in the token).
        /// </summary>
        public string? Nonce { get; set; } = null;

        /// <summary>
        /// Given name (Apple shares it only on the first sign-in).
        /// </summary>
        public string? GivenName { get; set; } = null;

        /// <summary>
        /// Family name (first sign-in only).
        /// </summary>
        public string? FamilyName { get; set; } = null;

        #endregion
    }
}
