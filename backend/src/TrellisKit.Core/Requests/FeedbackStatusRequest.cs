namespace TrellisKit.Core.Requests
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Responses;

    /// <summary>
    /// PUT /api/v1/threads/{id}/feedback body.
    /// </summary>
    public class FeedbackStatusRequest
    {
        #region Public-Members

        /// <summary>
        /// The new status.
        /// </summary>
        public FeedbackStatusEnum? Status { get; set; } = null;

        /// <summary>
        /// The review comment (required for Reviewed).
        /// </summary>
        public string? ReviewCommentId { get; set; } = null;

        #endregion
    }
}
