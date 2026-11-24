namespace TrellisKit.Core.Models
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Responses;

    /// <summary>
    /// Forum metrics counters (docs/PHASE3_CONTRACT.md section 12).
    /// </summary>
    public class ThreadActivityCounts
    {
        #region Public-Members

        /// <summary>
        /// Published threads with activity in the window.
        /// </summary>
        public long ActiveThreads { get; set; } = 0;

        /// <summary>
        /// Feedback threads Open or InReview.
        /// </summary>
        public long OpenReviewRequests { get; set; } = 0;

        /// <summary>
        /// Published Questions without an accepted answer.
        /// </summary>
        public long UnansweredQuestions { get; set; } = 0;

        #endregion
    }
}
