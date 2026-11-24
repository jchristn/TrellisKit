namespace TrellisKit.Server.Services
{
    using System;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// A validated member session, cached briefly in memory.
    /// </summary>
    public class SessionPrincipal
    {
        #region Public-Members

        /// <summary>
        /// Principal kind: Member, or Delegated for a platform administrator acting as SystemAdmin in this property.
        /// </summary>
        public TrellisKit.Core.Enums.PrincipalKindEnum PrincipalKind { get; set; } = TrellisKit.Core.Enums.PrincipalKindEnum.Member;

        /// <summary>
        /// Session identifier.
        /// </summary>
        public string SessionId { get; set; } = String.Empty;

        /// <summary>
        /// Member identifier.
        /// </summary>
        public string UserId { get; set; } = String.Empty;

        /// <summary>
        /// Email, or null.
        /// </summary>
        public string? Email { get; set; } = null;

        /// <summary>
        /// Display name.
        /// </summary>
        public string DisplayName { get; set; } = String.Empty;

        /// <summary>
        /// Whether the member holds SystemAdmin.
        /// </summary>
        public bool IsSystemAdmin { get; set; } = false;

        /// <summary>
        /// Whether the member holds a Moderator or FeedModerator role assignment (any scope).
        /// </summary>
        public bool IsModerator { get; set; } = false;

        /// <summary>
        /// Whether the member holds the SupportAgent role.
        /// </summary>
        public bool IsSupportAgent { get; set; } = false;

        /// <summary>
        /// Whether the member holds the Communicator role.
        /// </summary>
        public bool IsCommunicator { get; set; } = false;

        /// <summary>
        /// Whether the member holds the ReleaseManager role.
        /// </summary>
        public bool IsReleaseManager { get; set; } = false;

        /// <summary>
        /// Whether the member attested their age.
        /// </summary>
        public bool AgeAttested { get; set; } = false;

        /// <summary>
        /// Whether the member has an active membership.
        /// </summary>
        public bool HasMembership { get; set; } = false;

        /// <summary>
        /// The member's highest accepted sequence per property document (docs/TRELLISKIT.md section 14), or null for
        /// principals the document gate does not apply to (delegated platform sessions).
        /// </summary>
        public System.Collections.Generic.Dictionary<string, int>? AcceptedDocuments { get; set; } = null;

        /// <summary>
        /// Client that created the session.
        /// </summary>
        public ClientTypeEnum ClientType { get; set; } = ClientTypeEnum.Other;

        /// <summary>
        /// Session expiry (UTC).
        /// </summary>
        public DateTime SessionExpiresUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// When the cached copy must be refreshed (UTC).
        /// </summary>
        public DateTime CachedUntilUtc { get; set; } = DateTime.UtcNow;

        #endregion
    }
}
