namespace TrellisKit.Core.Models
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Responses;

    /// <summary>
    /// A moderator's mute of a member in one forum.
    /// </summary>
    public class ForumMute
    {
        #region Public-Members

        /// <summary>
        /// Identifier (fmu_).
        /// </summary>
        public string Id { get; set; } = String.Empty;

        /// <summary>
        /// Forum identifier.
        /// </summary>
        public string ForumId { get; set; } = String.Empty;

        /// <summary>
        /// Muted member.
        /// </summary>
        public string UserId { get; set; } = String.Empty;

        /// <summary>
        /// End of the mute, or null until lifted.
        /// </summary>
        public DateTime? UntilUtc { get; set; } = null;

        /// <summary>
        /// Reason, or null.
        /// </summary>
        public string? Reason { get; set; } = null;

        /// <summary>
        /// Moderator, or null for the admin token.
        /// </summary>
        public string? MutedByUserId { get; set; } = null;

        /// <summary>
        /// When the mute was lifted, or null while active.
        /// </summary>
        public DateTime? EndedUtc { get; set; } = null;

        /// <summary>
        /// Who lifted it, or null.
        /// </summary>
        public string? EndedByUserId { get; set; } = null;

        /// <summary>
        /// Creation time (UTC).
        /// </summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        #endregion
    }
}
