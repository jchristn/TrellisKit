namespace TrellisKit.Core.Models
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Responses;

    /// <summary>
    /// A moderator assignment covering a forum.
    /// </summary>
    public class ForumModeratorRow
    {
        #region Public-Members

        /// <summary>
        /// Moderator.
        /// </summary>
        public string UserId { get; set; } = String.Empty;

        /// <summary>
        /// Forum identifier or * for every forum.
        /// </summary>
        public string ResourceId { get; set; } = String.Empty;

        /// <summary>
        /// When the assignment was granted.
        /// </summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Who granted it, or null.
        /// </summary>
        public string? GrantedByUserId { get; set; } = null;

        #endregion
    }
}
