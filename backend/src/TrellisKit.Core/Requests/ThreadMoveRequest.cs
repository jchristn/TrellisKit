namespace TrellisKit.Core.Requests
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Responses;

    /// <summary>
    /// POST /api/v1/threads/{id}/move body.
    /// </summary>
    public class ThreadMoveRequest
    {
        #region Public-Members

        /// <summary>
        /// Destination forum.
        /// </summary>
        public string? ForumId { get; set; } = null;

        /// <summary>
        /// Reason shown to the author.
        /// </summary>
        public string? Reason { get; set; } = null;

        /// <summary>
        /// Notify the author (default true).
        /// </summary>
        public bool? NotifyAuthor { get; set; } = null;

        #endregion
    }
}
