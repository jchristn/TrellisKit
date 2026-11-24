namespace TrellisKit.Core.Responses
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// Forum metrics (GET /api/v1/admin/metrics/forums).
    /// </summary>
    public class ForumMetrics
    {
        #region Public-Members

        /// <summary>
        /// Days in the window.
        /// </summary>
        public int Days { get; set; } = 0;

        /// <summary>
        /// Threads published per day (one entry per day).
        /// </summary>
        public List<DailyCount> ThreadsByDay
        {
            get { return _ThreadsByDay; }
            set { _ThreadsByDay = value ?? new List<DailyCount>(); }
        }

        /// <summary>
        /// Thread comments per day.
        /// </summary>
        public List<DailyCount> CommentsByDay
        {
            get { return _CommentsByDay; }
            set { _CommentsByDay = value ?? new List<DailyCount>(); }
        }

        /// <summary>
        /// Distinct poll voters per day.
        /// </summary>
        public List<DailyCount> VotesByDay
        {
            get { return _VotesByDay; }
            set { _VotesByDay = value ?? new List<DailyCount>(); }
        }

        /// <summary>
        /// Threads with activity in the window.
        /// </summary>
        public long ActiveThreads { get; set; } = 0;

        /// <summary>
        /// Open or InReview Feedback threads.
        /// </summary>
        public long OpenReviewRequests { get; set; } = 0;

        /// <summary>
        /// Published Questions without an accepted answer.
        /// </summary>
        public long UnansweredQuestions { get; set; } = 0;

        /// <summary>
        /// At most 20 forums by threads in the window.
        /// </summary>
        public BoundedPreview<AdminForumView> ByForum
        {
            get { return _ByForum; }
            set { _ByForum = value ?? new BoundedPreview<AdminForumView>(); }
        }

        #endregion

        #region Private-Members

        private List<DailyCount> _ThreadsByDay = new List<DailyCount>();
        private List<DailyCount> _CommentsByDay = new List<DailyCount>();
        private List<DailyCount> _VotesByDay = new List<DailyCount>();
        private BoundedPreview<AdminForumView> _ByForum = new BoundedPreview<AdminForumView>();

        #endregion
    }
}
