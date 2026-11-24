namespace TrellisKit.Core.Requests
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Responses;

    /// <summary>
    /// POST /api/v1/threads/{id}/promote body (docs/PHASE3_CONTRACT.md section 8.1).
    /// </summary>
    public class PromoteRequest
    {
        #region Public-Members

        /// <summary>
        /// Card title override (up to 140 characters).
        /// </summary>
        public string? Title { get; set; } = null;

        /// <summary>
        /// Card excerpt override (up to 300 characters).
        /// </summary>
        public string? Excerpt { get; set; } = null;

        /// <summary>
        /// Pin the card (SystemAdmin only).
        /// </summary>
        public bool? Pin { get; set; } = null;

        /// <summary>
        /// Card audience (not broader than the thread's).
        /// </summary>
        public ContentAudienceEnum? Audience { get; set; } = null;

        /// <summary>
        /// Run the new feed post fan-out (default true).
        /// </summary>
        public bool? NotifyMembers { get; set; } = null;

        #endregion
    }
}
