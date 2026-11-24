namespace TrellisKit.Core.Requests
{
    using System;

    /// <summary>
    /// POST /api/v1/admin/moderators body (contract section 10.4).
    /// </summary>
    public class ModeratorGrantRequest
    {
        #region Public-Members

        /// <summary>
        /// Member to grant.
        /// </summary>
        public string? UserId { get; set; } = null;

        /// <summary>
        /// Scope: Feed (the default) or Forum (Phase 3).
        /// </summary>
        public string? ResourceType { get; set; } = null;

        /// <summary>
        /// Forum identifier for resourceType Forum, or * for every forum (the default). Ignored for Feed.
        /// </summary>
        public string? ResourceId { get; set; } = null;

        #endregion
    }
}
