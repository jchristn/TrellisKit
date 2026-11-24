namespace TrellisKit.Core.Requests
{
    using System;

    /// <summary>
    /// POST /api/v1/platform/auth/accept-invite body: the invitation token and the new password.
    /// </summary>
    public class PlatformAcceptInviteRequest
    {
        #region Public-Members

        /// <summary>
        /// Invitation token.
        /// </summary>
        public string Token { get; set; } = String.Empty;

        /// <summary>
        /// New password (the member password rules apply).
        /// </summary>
        public string Password { get; set; } = String.Empty;

        #endregion
    }
}
