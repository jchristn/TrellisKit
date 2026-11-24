namespace TrellisKit.Core.Models
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Responses;

    /// <summary>
    /// A member following a forum.
    /// </summary>
    public class ForumFollow
    {
        #region Public-Members

        /// <summary>
        /// Identifier (ffw_).
        /// </summary>
        public string Id { get; set; } = String.Empty;

        /// <summary>
        /// Forum identifier.
        /// </summary>
        public string ForumId { get; set; } = String.Empty;

        /// <summary>
        /// Member.
        /// </summary>
        public string UserId { get; set; } = String.Empty;

        /// <summary>
        /// Follow level (NewThreads, the only Phase 3 level).
        /// </summary>
        public string Level { get; set; } = "NewThreads";

        /// <summary>
        /// When the follow started.
        /// </summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        #endregion
    }
}
