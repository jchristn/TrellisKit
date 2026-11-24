namespace TrellisKit.Core.Models
{
    using System;

    /// <summary>
    /// Per-user posting permissions that extend the Phase 1 member flags. A missing row means every permission is
    /// granted (the defaults below).
    /// </summary>
    public class UserPermission
    {
        #region Public-Members

        /// <summary>
        /// Member identifier.
        /// </summary>
        public string UserId { get; set; } = String.Empty;

        /// <summary>
        /// Whether the member may write feed posts (with canPostToFeed), comments, and replies, and edit them. Default true.
        /// </summary>
        public bool CanPost { get; set; } = true;

        /// <summary>
        /// Whether the member may upload and attach media. Default true.
        /// </summary>
        public bool CanPostMedia { get; set; } = true;

        /// <summary>
        /// Last change (UTC).
        /// </summary>
        public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Administrator who made the last change, or null.
        /// </summary>
        public string? UpdatedByUserId { get; set; } = null;

        #endregion
    }
}
