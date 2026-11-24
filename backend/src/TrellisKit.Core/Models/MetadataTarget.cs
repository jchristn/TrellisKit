namespace TrellisKit.Core.Models
{
    using System;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// An object that can carry labels and tags, as resolved for the generic metadata routes.
    /// </summary>
    public class MetadataTarget
    {
        #region Public-Members

        /// <summary>
        /// Target type.
        /// </summary>
        public TargetTypeEnum TargetType { get; set; } = TargetTypeEnum.User;

        /// <summary>
        /// Target identifier.
        /// </summary>
        public string TargetId { get; set; } = String.Empty;

        /// <summary>
        /// Member who owns the object and may edit its public metadata, or null for staff-only objects.
        /// </summary>
        public string? OwnerUserId { get; set; } = null;

        #endregion
    }
}
