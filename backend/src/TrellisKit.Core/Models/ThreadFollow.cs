namespace TrellisKit.Core.Models
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Responses;

    /// <summary>
    /// A member following a thread.
    /// </summary>
    public class ThreadFollow
    {
        #region Public-Members

        /// <summary>
        /// Identifier (tfw_).
        /// </summary>
        public string Id { get; set; } = String.Empty;

        /// <summary>
        /// Thread identifier.
        /// </summary>
        public string ThreadId { get; set; } = String.Empty;

        /// <summary>
        /// Member.
        /// </summary>
        public string UserId { get; set; } = String.Empty;

        /// <summary>
        /// Why the member follows.
        /// </summary>
        public FollowSourceEnum Source { get; set; } = FollowSourceEnum.Explicit;

        /// <summary>
        /// An explicit unfollow that blocks automatic follows.
        /// </summary>
        public bool IsOptedOut { get; set; } = false;

        /// <summary>
        /// When the follow started.
        /// </summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        #endregion
    }
}
