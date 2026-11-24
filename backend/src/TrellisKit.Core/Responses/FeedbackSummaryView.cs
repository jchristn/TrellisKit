namespace TrellisKit.Core.Responses
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// Feedback state in a thread list item.
    /// </summary>
    public class FeedbackSummaryView
    {
        #region Public-Members

        /// <summary>
        /// Review status.
        /// </summary>
        public FeedbackStatusEnum Status { get; set; } = FeedbackStatusEnum.Open;

        /// <summary>
        /// The moderator who started or finished the review, or null while nobody has picked the request up (or the
        /// reviewer's account is gone).
        /// </summary>
        public UserSummary? Reviewer { get; set; } = null;

        #endregion
    }
}
