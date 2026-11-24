namespace TrellisKit.Core.Requests
{
    using System;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// Typed conditions of the moderators enumeration.
    /// </summary>
    public class ModeratorFilter
    {
        #region Public-Members

        /// <summary>
        /// Moderator or FeedModerator, or null for both.
        /// </summary>
        public RoleEnum? Role { get; set; } = null;

        /// <summary>
        /// Scope type of the role, or null.
        /// </summary>
        public ResourceTypeEnum? ResourceType { get; set; } = null;

        #endregion
    }
}
