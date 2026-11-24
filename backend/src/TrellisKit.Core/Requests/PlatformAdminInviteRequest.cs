namespace TrellisKit.Core.Requests
{
    using System;

    /// <summary>
    /// POST /api/v1/platform/admins body.
    /// </summary>
    public class PlatformAdminInviteRequest
    {
        #region Public-Members

        /// <summary>
        /// Email.
        /// </summary>
        public string Email { get; set; } = String.Empty;

        /// <summary>
        /// Display name.
        /// </summary>
        public string DisplayName { get; set; } = String.Empty;

        #endregion
    }
}
