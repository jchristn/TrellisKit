namespace TrellisKit.Core.Requests
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Responses;

    /// <summary>
    /// POST /api/v1/feed/{id}/discuss body (docs/PHASE3_CONTRACT.md section 8.2).
    /// </summary>
    public class DiscussRequest
    {
        #region Public-Members

        /// <summary>
        /// Destination forum (must allow Discussion).
        /// </summary>
        public string? ForumId { get; set; } = null;

        /// <summary>
        /// Thread title override.
        /// </summary>
        public string? Title { get; set; } = null;

        /// <summary>
        /// Labels of the new thread.
        /// </summary>
        public List<string>? Labels { get; set; } = null;

        /// <summary>
        /// Tags of the new thread.
        /// </summary>
        public Dictionary<string, string>? Tags { get; set; } = null;

        #endregion
    }
}
