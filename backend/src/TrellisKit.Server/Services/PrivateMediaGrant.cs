namespace TrellisKit.Server.Services
{
    using System;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// The claims of a per-object private media URL token: one media item, its purpose and scope, the viewer the URL was
    /// issued to, and the expiry (messaging.mediaUrlMinutes after issue).
    /// </summary>
    public class PrivateMediaGrant
    {
        #region Public-Members

        /// <summary>
        /// Property identifier the URL was issued in.
        /// </summary>
        public string PropertyId { get; set; } = String.Empty;

        /// <summary>
        /// Media identifier.
        /// </summary>
        public string MediaId { get; set; } = String.Empty;

        /// <summary>
        /// Private purpose of the media.
        /// </summary>
        public MediaPurposeEnum Purpose { get; set; } = MediaPurposeEnum.Ticket;

        /// <summary>
        /// Scope identifier (ticket or conversation), or empty for a pending upload shown to its owner.
        /// </summary>
        public string ScopeId { get; set; } = String.Empty;

        /// <summary>
        /// Viewer user identifier, or <see cref="PrivateMediaTokens.BreakGlassViewer"/> for the break-glass admin token.
        /// </summary>
        public string ViewerKey { get; set; } = String.Empty;

        /// <summary>
        /// Expiry (UTC).
        /// </summary>
        public DateTime ExpiresUtc { get; set; } = DateTime.UtcNow;

        #endregion
    }
}
