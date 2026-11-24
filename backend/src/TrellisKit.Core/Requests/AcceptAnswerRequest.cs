namespace TrellisKit.Core.Requests
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Responses;

    /// <summary>
    /// POST /api/v1/threads/{id}/accept-answer body.
    /// </summary>
    public class AcceptAnswerRequest
    {
        #region Public-Members

        /// <summary>
        /// The comment to accept.
        /// </summary>
        public string? CommentId { get; set; } = null;

        #endregion
    }
}
