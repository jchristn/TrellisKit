namespace TrellisKit.Core.Responses
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// Feedback request state (docs/PHASE3_CONTRACT.md section 7).
    /// </summary>
    public class FeedbackView
    {
        #region Public-Members

        /// <summary>
        /// Review status.
        /// </summary>
        public FeedbackStatusEnum Status { get; set; } = FeedbackStatusEnum.Open;

        /// <summary>
        /// What the member wants reviewed.
        /// </summary>
        public string? Focus { get; set; } = null;

        /// <summary>
        /// The reviewer, or null.
        /// </summary>
        public UserSummary? Reviewer { get; set; } = null;

        /// <summary>
        /// The review comment, or null.
        /// </summary>
        public string? ReviewCommentId { get; set; } = null;

        /// <summary>
        /// Last status change.
        /// </summary>
        public DateTime? StatusUtc { get; set; } = null;

        #endregion
    }
}
