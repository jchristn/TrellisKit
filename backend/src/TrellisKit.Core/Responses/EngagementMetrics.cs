namespace TrellisKit.Core.Responses
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// GET /api/v1/admin/metrics/engagement (contract section 10.1). The daily lists have exactly one entry per day of the window (fixed bound: days, at most 365).
    /// </summary>
    public class EngagementMetrics
    {
        #region Public-Members

        /// <summary>
        /// Window in days.
        /// </summary>
        public int Days { get; set; } = 30;

        /// <summary>
        /// Posts published per UTC day (never null).
        /// </summary>
        public List<DailyCount> PostsByDay
        {
            get { return _PostsByDay; }
            set { _PostsByDay = value ?? new List<DailyCount>(); }
        }

        /// <summary>
        /// Comments created per UTC day (never null).
        /// </summary>
        public List<DailyCount> CommentsByDay
        {
            get { return _CommentsByDay; }
            set { _CommentsByDay = value ?? new List<DailyCount>(); }
        }

        /// <summary>
        /// Reactions added per UTC day (never null).
        /// </summary>
        public List<DailyCount> ReactionsByDay
        {
            get { return _ReactionsByDay; }
            set { _ReactionsByDay = value ?? new List<DailyCount>(); }
        }

        /// <summary>
        /// Forum threads published per UTC day, deleted threads excluded (Phase 3 section 12; never null).
        /// </summary>
        public List<DailyCount> ThreadsByDay
        {
            get { return _ThreadsByDay; }
            set { _ThreadsByDay = value ?? new List<DailyCount>(); }
        }

        /// <summary>
        /// Comments on forum threads created per UTC day (Phase 3 section 12; never null).
        /// </summary>
        public List<DailyCount> ThreadCommentsByDay
        {
            get { return _ThreadCommentsByDay; }
            set { _ThreadCommentsByDay = value ?? new List<DailyCount>(); }
        }

        /// <summary>
        /// Members who commented or reacted in the window.
        /// </summary>
        public long ActiveMembers { get; set; } = 0;

        /// <summary>
        /// Open reports.
        /// </summary>
        public long OpenReports { get; set; } = 0;

        /// <summary>
        /// Scheduled posts not yet published.
        /// </summary>
        public long ScheduledPosts { get; set; } = 0;

        #endregion

        #region Private-Members

        private List<DailyCount> _PostsByDay = new List<DailyCount>();
        private List<DailyCount> _CommentsByDay = new List<DailyCount>();
        private List<DailyCount> _ReactionsByDay = new List<DailyCount>();
        private List<DailyCount> _ThreadsByDay = new List<DailyCount>();
        private List<DailyCount> _ThreadCommentsByDay = new List<DailyCount>();

        #endregion
    }
}
