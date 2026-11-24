namespace TrellisKit.Core.Responses
{
    using System;

    /// <summary>
    /// A created platform administrator with its one-time invitation token (POST /api/v1/platform/admins).
    /// </summary>
    public class PlatformInviteResponse
    {
        #region Public-Members

        /// <summary>
        /// The administrator.
        /// </summary>
        public PlatformAdminResponse Admin { get; set; } = new PlatformAdminResponse();

        /// <summary>
        /// Invitation token, returned once only when inviteDelivery is Log (otherwise null: it was emailed); accepted at
        /// POST /api/v1/platform/auth/accept-invite.
        /// </summary>
        public string? InviteToken { get; set; } = null;

        /// <summary>
        /// How the invitation was delivered: Email (the token is not returned) or Log (no deployment email provider is
        /// configured: the token is returned once and written to the server log).
        /// </summary>
        public string InviteDelivery { get; set; } = "Log";

        /// <summary>
        /// Invitation expiry.
        /// </summary>
        public DateTime InviteExpiresUtc { get; set; } = DateTime.UtcNow;

        #endregion
    }
}
