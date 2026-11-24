namespace TrellisKit.Core.Security
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// Authenticated request context, established by the authentication hook and stored in the request metadata.
    /// </summary>
    public class RequestContext
    {
        #region Public-Members

        /// <summary>
        /// Property the request resolved to (docs/MULTIPROPERTY_CONTRACT.md section 2.6). Empty only before resolution.
        /// </summary>
        public string PropertyId { get; set; } = String.Empty;

        /// <summary>
        /// Kind of principal.
        /// </summary>
        public PrincipalKindEnum PrincipalKind { get; set; } = PrincipalKindEnum.Anonymous;

        /// <summary>
        /// Platform administrator id (pad_) for a delegated session, otherwise null.
        /// </summary>
        public string? PlatformAdminId { get; set; } = null;

        /// <summary>
        /// Whether the request is authenticated.
        /// </summary>
        public bool IsAuthenticated { get; set; } = false;

        /// <summary>
        /// User identifier, or null for the break-glass administrator and anonymous requests.
        /// </summary>
        public string? UserId { get; set; } = null;

        /// <summary>
        /// Whether the caller has system administrator rights.
        /// </summary>
        public bool IsAdmin { get; set; } = false;

        /// <summary>
        /// Principal display name for logs and request history, or null.
        /// </summary>
        public string? PrincipalName { get; set; } = null;

        /// <summary>
        /// Authentication scheme that established the context, for example "AdminToken".
        /// </summary>
        public string? Scheme { get; set; } = null;

        /// <summary>
        /// Session identifier for member sessions, or null for the admin token.
        /// </summary>
        public string? SessionId { get; set; } = null;

        /// <summary>
        /// Member email, or null.
        /// </summary>
        public string? Email { get; set; } = null;

        /// <summary>
        /// Client that created the session.
        /// </summary>
        public ClientTypeEnum ClientType { get; set; } = ClientTypeEnum.Other;

        /// <summary>
        /// Whether the member has attested they meet the property minimum age (membership.minimumAge). True for the admin token.
        /// </summary>
        public bool AgeAttested { get; set; } = false;

        /// <summary>
        /// Whether the member has an active membership (as of the cached session check).
        /// </summary>
        public bool HasMembership { get; set; } = false;

        /// <summary>
        /// The member's highest accepted sequence per property document (docs/TRELLISKIT.md section 14), or null when the
        /// document gate does not apply (the admin token, delegated platform sessions, deploy keys, signed-out callers).
        /// </summary>
        public Dictionary<string, int>? AcceptedDocuments { get; set; } = null;

        /// <summary>
        /// Whether the request used the break-glass admin token rather than a member session.
        /// </summary>
        public bool IsBreakGlass { get; set; } = false;

        /// <summary>
        /// Whether the member holds a Moderator or FeedModerator role assignment (any scope).
        /// </summary>
        public bool IsModerator { get; set; } = false;

        /// <summary>
        /// Whether the caller holds the SupportAgent role (support inbox and ideas board admin routes).
        /// </summary>
        public bool IsSupportAgent { get; set; } = false;

        /// <summary>
        /// Whether the caller holds the Communicator role (broadcast admin routes).
        /// </summary>
        public bool IsCommunicator { get; set; } = false;

        /// <summary>
        /// Whether the caller holds the ReleaseManager role (release and app configuration admin routes).
        /// </summary>
        public bool IsReleaseManager { get; set; } = false;

        /// <summary>
        /// Granted scopes. Never null.
        /// </summary>
        public List<string> Scopes
        {
            get { return _Scopes; }
            set { _Scopes = value ?? new List<string>(); }
        }

        /// <summary>
        /// Correlation identifier for logs. Defaults to a new identifier per request.
        /// </summary>
        public string CorrelationId { get; set; } = Guid.NewGuid().ToString("N");

        /// <summary>
        /// Live update deploy key id (udk_) when the caller authenticated with a deploy key, otherwise null. Deploy
        /// keys reach only the publish routes, and only for <see cref="DeployKeyChannels"/>.
        /// </summary>
        public string? DeployKeyId { get; set; } = null;

        /// <summary>
        /// Channels a deploy key may publish to. Empty for other callers. Never null.
        /// </summary>
        public List<string> DeployKeyChannels { get; set; } = new List<string>();

        #endregion

        #region Private-Members

        private List<string> _Scopes = new List<string>();

        #endregion
    }
}
