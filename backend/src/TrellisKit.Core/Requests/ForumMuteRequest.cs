namespace TrellisKit.Core.Requests
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Responses;

    /// <summary>
    /// POST /api/v1/admin/moderation/forum-mutes body.
    /// </summary>
    public class ForumMuteRequest
    {
        #region Public-Members

        /// <summary>
        /// Member to mute.
        /// </summary>
        public string? UserId { get; set; } = null;

        /// <summary>
        /// Forum.
        /// </summary>
        public string? ForumId { get; set; } = null;

        /// <summary>
        /// End of the mute, or null until lifted.
        /// </summary>
        public DateTime? UntilUtc { get; set; } = null;

        /// <summary>
        /// Reason.
        /// </summary>
        public string? Reason { get; set; } = null;

        /// <summary>
        /// Notify the member (default true).
        /// </summary>
        public bool? NotifyMember { get; set; } = null;

        /// <summary>
        /// Labels of the mute.
        /// </summary>
        public List<string>? Labels { get; set; } = null;

        /// <summary>
        /// Tags of the mute.
        /// </summary>
        public Dictionary<string, string>? Tags { get; set; } = null;

        #endregion
    }
}
