namespace TrellisKit.Core.Settings
{
    using System;

    /// <summary>
    /// Forum settings (docs/PHASE3_CONTRACT.md section 15).
    /// </summary>
    public class ForumSettings
    {
        #region Public-Members

        /// <summary>
        /// Forums a property may have. Default 200. Minimum 1, maximum 1000.
        /// </summary>
        public int MaxForums
        {
            get { return _MaxForums; }
            set { _MaxForums = Math.Clamp(value, 1, 1000); }
        }

        /// <summary>
        /// Longest thread body, in Unicode scalar values. Default 10000. Minimum 100, maximum 50000.
        /// </summary>
        public int MaxBodyLength
        {
            get { return _MaxBodyLength; }
            set { _MaxBodyLength = Math.Clamp(value, 100, 50000); }
        }

        /// <summary>
        /// Media items a thread may carry. Default 10. Minimum 0, maximum 20.
        /// </summary>
        public int MaxAttachments
        {
            get { return _MaxAttachments; }
            set { _MaxAttachments = Math.Clamp(value, 0, 20); }
        }

        /// <summary>
        /// Videos a thread may carry. Default 1. Minimum 0, maximum 10.
        /// </summary>
        public int MaxVideosPerThread
        {
            get { return _MaxVideosPerThread; }
            set { _MaxVideosPerThread = Math.Clamp(value, 0, 10); }
        }

        /// <summary>
        /// Pinned threads per forum. Default 5. Minimum 1, maximum 20.
        /// </summary>
        public int MaxPinnedPerForum
        {
            get { return _MaxPinnedPerForum; }
            set { _MaxPinnedPerForum = Math.Clamp(value, 1, 20); }
        }

        /// <summary>
        /// Open or InReview Feedback threads per member (a forum may override it). Default 3. Minimum 0 (unlimited), maximum 100.
        /// </summary>
        public int MaxOpenFeedbackPerMember
        {
            get { return _MaxOpenFeedbackPerMember; }
            set { _MaxOpenFeedbackPerMember = Math.Clamp(value, 0, 100); }
        }

        /// <summary>
        /// Longest forum mute a moderator may set, in days. Default 0, which means no cap (resolved decision 15). Minimum 0, maximum 3650.
        /// </summary>
        public int MaxForumMuteDays
        {
            get { return _MaxForumMuteDays; }
            set { _MaxForumMuteDays = Math.Clamp(value, 0, 3650); }
        }

        /// <summary>
        /// Threads a member may follow; following more drops the least recently active. Default 5000. Minimum 100, maximum 100000.
        /// </summary>
        public int MaxFollowsPerMember
        {
            get { return _MaxFollowsPerMember; }
            set { _MaxFollowsPerMember = Math.Clamp(value, 100, 100000); }
        }

        /// <summary>
        /// Whether members follow a thread when they first comment on it (each member can turn it off). Default true.
        /// </summary>
        public bool AutoFollowOnComment { get; set; } = true;

        /// <summary>
        /// Read markers older than this are pruned. Default 90. Minimum 7, maximum 3650.
        /// </summary>
        public int ReadStateRetentionDays
        {
            get { return _ReadStateRetentionDays; }
            set { _ReadStateRetentionDays = Math.Clamp(value, 7, 3650); }
        }

        /// <summary>
        /// Hours between Following and Forums email digests for members who turned email on for those categories
        /// (docs/PHASE3_CONTRACT.md section 9.3; weekly by default). Default 168. Minimum 24, maximum 720.
        /// </summary>
        public int EmailDigestIntervalHours
        {
            get { return _EmailDigestIntervalHours; }
            set { _EmailDigestIntervalHours = Math.Clamp(value, 24, 720); }
        }

        /// <summary>
        /// Most notifications listed in one digest email (the rest are counted). Default 10. Minimum 1, maximum 50.
        /// </summary>
        public int EmailDigestMaxItems
        {
            get { return _EmailDigestMaxItems; }
            set { _EmailDigestMaxItems = Math.Clamp(value, 1, 50); }
        }

        /// <summary>
        /// Without a read marker, a thread is unread only when active within this many days. Default 14. Minimum 1, maximum 365.
        /// </summary>
        public int UnreadWindowDays
        {
            get { return _UnreadWindowDays; }
            set { _UnreadWindowDays = Math.Clamp(value, 1, 365); }
        }

        /// <summary>
        /// Threads a member may create each hour. Default 10. Minimum 1, maximum 1000.
        /// </summary>
        public int ThreadsPerHour
        {
            get { return _ThreadsPerHour; }
            set { _ThreadsPerHour = Math.Clamp(value, 1, 1000); }
        }

        /// <summary>
        /// Threads a member may create each day. Default 50. Minimum 1, maximum 10000.
        /// </summary>
        public int ThreadsPerDay
        {
            get { return _ThreadsPerDay; }
            set { _ThreadsPerDay = Math.Clamp(value, 1, 10000); }
        }

        /// <summary>
        /// Threads a new account may create each hour. Default 2. Minimum 1, maximum 1000.
        /// </summary>
        public int NewAccountThreadsPerHour
        {
            get { return _NewAccountThreadsPerHour; }
            set { _NewAccountThreadsPerHour = Math.Clamp(value, 1, 1000); }
        }

        /// <summary>
        /// Threads a new account may create each day. Default 5. Minimum 1, maximum 10000.
        /// </summary>
        public int NewAccountThreadsPerDay
        {
            get { return _NewAccountThreadsPerDay; }
            set { _NewAccountThreadsPerDay = Math.Clamp(value, 1, 10000); }
        }

        /// <summary>
        /// Follow and unfollow requests per member each minute. Default 120. Minimum 1, maximum 10000.
        /// </summary>
        public int FollowsPerMinute
        {
            get { return _FollowsPerMinute; }
            set { _FollowsPerMinute = Math.Clamp(value, 1, 10000); }
        }

        /// <summary>
        /// Thread searches per member each minute. Default 60. Minimum 1, maximum 10000.
        /// </summary>
        public int SearchPerMinute
        {
            get { return _SearchPerMinute; }
            set { _SearchPerMinute = Math.Clamp(value, 1, 10000); }
        }

        /// <summary>
        /// Promote, discuss, and unpromote requests per member each hour. Default 30. Minimum 1, maximum 1000.
        /// </summary>
        public int PromotionsPerHour
        {
            get { return _PromotionsPerHour; }
            set { _PromotionsPerHour = Math.Clamp(value, 1, 1000); }
        }

        /// <summary>
        /// Whether a property without forums gets a default General discussion forum (resolved decision 12). Default true.
        /// </summary>
        public bool SeedDefaultForum { get; set; } = true;

        #endregion

        #region Private-Members

        private int _MaxForums = 200;
        private int _MaxBodyLength = 10000;
        private int _MaxAttachments = 10;
        private int _MaxVideosPerThread = 1;
        private int _MaxPinnedPerForum = 5;
        private int _MaxOpenFeedbackPerMember = 3;
        private int _MaxForumMuteDays = 0;
        private int _MaxFollowsPerMember = 5000;
        private int _ReadStateRetentionDays = 90;
        private int _EmailDigestIntervalHours = 168;
        private int _EmailDigestMaxItems = 10;
        private int _UnreadWindowDays = 14;
        private int _ThreadsPerHour = 10;
        private int _ThreadsPerDay = 50;
        private int _NewAccountThreadsPerHour = 2;
        private int _NewAccountThreadsPerDay = 5;
        private int _FollowsPerMinute = 120;
        private int _SearchPerMinute = 60;
        private int _PromotionsPerHour = 30;

        #endregion
    }
}
