namespace TrellisKit.Core.Models
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Responses;

    /// <summary>
    /// One row of a member's follows (forum or thread).
    /// </summary>
    public class FollowRow
    {
        #region Public-Members

        /// <summary>
        /// Follow identifier.
        /// </summary>
        public string Id { get; set; } = String.Empty;

        /// <summary>
        /// Forum or Thread.
        /// </summary>
        public TargetTypeEnum TargetType { get; set; } = TargetTypeEnum.Forum;

        /// <summary>
        /// Forum or thread identifier.
        /// </summary>
        public string TargetId { get; set; } = String.Empty;

        /// <summary>
        /// When the follow started.
        /// </summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        #endregion
    }
}
