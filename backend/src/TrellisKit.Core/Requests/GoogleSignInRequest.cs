namespace TrellisKit.Core.Requests
{
    using System;

    /// <summary>
    /// Body of POST /api/v1/auth/google and POST /api/v1/me/identities/google.
    /// </summary>
    public class GoogleSignInRequest
    {
        #region Public-Members

        /// <summary>
        /// Google ID token JWT.
        /// </summary>
        public string? IdToken { get; set; } = null;

        #endregion
    }
}
