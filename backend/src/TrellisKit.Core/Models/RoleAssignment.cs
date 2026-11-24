namespace TrellisKit.Core.Models
{
    using System;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// A role granted to a member, optionally scoped to a resource.
    /// </summary>
    public class RoleAssignment
    {
        #region Public-Members

        /// <summary>
        /// Identifier (prefix rla_).
        /// </summary>
        public string Id { get; set; } = String.Empty;

        /// <summary>
        /// Member identifier.
        /// </summary>
        public string UserId { get; set; } = String.Empty;

        /// <summary>
        /// Role (never User, which is implicit).
        /// </summary>
        public RoleEnum Role { get; set; } = RoleEnum.User;

        /// <summary>
        /// Resource type.
        /// </summary>
        public ResourceTypeEnum ResourceType { get; set; } = ResourceTypeEnum.Global;

        /// <summary>
        /// Resource identifier, or * for all.
        /// </summary>
        public string ResourceId { get; set; } = "*";

        /// <summary>
        /// Administrator who granted it, or null for the system.
        /// </summary>
        public string? GrantedByUserId { get; set; } = null;

        /// <summary>
        /// Granted (UTC).
        /// </summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        #endregion
    }
}
