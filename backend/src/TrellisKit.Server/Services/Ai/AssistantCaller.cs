namespace TrellisKit.Server.Services.Ai
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Security;

    /// <summary>
    /// Who is chatting: a member (with their content actor) or an anonymous visitor of a public assistant.
    /// </summary>
    public class AssistantCaller
    {
        #region Public-Members

        /// <summary>
        /// Member id, or null for a visitor.
        /// </summary>
        public string? UserId { get; set; } = null;

        /// <summary>
        /// Visitor id, or null for a member.
        /// </summary>
        public string? VisitorId { get; set; } = null;

        /// <summary>
        /// Content actor used for the word filter and for retrieval as the member.
        /// </summary>
        public ContentActor Actor { get; set; } = new ContentActor();

        /// <summary>
        /// Display name for the {memberDisplayName} token (empty for visitors).
        /// </summary>
        public string DisplayName { get; set; } = String.Empty;

        /// <summary>
        /// SystemAdmin or the admin token (exempt from member limits).
        /// </summary>
        public bool IsSystemAdmin { get; set; } = false;

        /// <summary>
        /// Active membership (or admin).
        /// </summary>
        public bool HasMembership { get; set; } = false;

        /// <summary>
        /// Client surface.
        /// </summary>
        public AiConversationSurfaceEnum Surface { get; set; } = AiConversationSurfaceEnum.Web;

        /// <summary>
        /// Client IP.
        /// </summary>
        public string? SourceIp { get; set; } = null;

        /// <summary>
        /// Request context of a signed-in caller.
        /// </summary>
        public RequestContext? Context { get; set; } = null;

        #endregion
    }
}
