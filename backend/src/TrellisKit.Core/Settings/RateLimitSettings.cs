namespace TrellisKit.Core.Settings
{
    using System;

    /// <summary>
    /// Request rate limits (contract section 12).
    /// </summary>
    public class RateLimitSettings
    {
        #region Public-Members

        /// <summary>
        /// GET requests on feed, comments, reactions, media, and notifications per member each minute. Default 600. Minimum 10, maximum 100000.
        /// </summary>
        public int MemberReadsPerMinute
        {
            get { return _MemberReadsPerMinute; }
            set { _MemberReadsPerMinute = Math.Clamp(value, 10, 100000); }
        }

        /// <summary>
        /// Unread count requests per member each minute. Default 120. Minimum 1, maximum 10000.
        /// </summary>
        public int UnreadCountPerMinute
        {
            get { return _UnreadCountPerMinute; }
            set { _UnreadCountPerMinute = Math.Clamp(value, 1, 10000); }
        }

        /// <summary>
        /// Post and comment edits per member each hour. Default 60. Minimum 1, maximum 10000.
        /// </summary>
        public int EditsPerHour
        {
            get { return _EditsPerHour; }
            set { _EditsPerHour = Math.Clamp(value, 1, 10000); }
        }

        /// <summary>
        /// Media grant requests per member each hour. Default 60. Minimum 1, maximum 10000.
        /// </summary>
        public int MediaGrantsPerHour
        {
            get { return _MediaGrantsPerHour; }
            set { _MediaGrantsPerHour = Math.Clamp(value, 1, 10000); }
        }

        /// <summary>
        /// Preference updates and read or unread marks per member each minute. Default 120. Minimum 1, maximum 10000.
        /// </summary>
        public int NotificationWritesPerMinute
        {
            get { return _NotificationWritesPerMinute; }
            set { _NotificationWritesPerMinute = Math.Clamp(value, 1, 10000); }
        }

        /// <summary>
        /// Member search requests per member each minute. Default 120. Minimum 1, maximum 10000.
        /// </summary>
        public int MemberSearchPerMinute
        {
            get { return _MemberSearchPerMinute; }
            set { _MemberSearchPerMinute = Math.Clamp(value, 1, 10000); }
        }

        /// <summary>
        /// Anonymous public feed requests per IP each minute. Default 120. Minimum 1, maximum 100000.
        /// </summary>
        public int PublicFeedPerMinute
        {
            get { return _PublicFeedPerMinute; }
            set { _PublicFeedPerMinute = Math.Clamp(value, 1, 100000); }
        }

        /// <summary>
        /// Anonymous public landing page, sitemap, and robots.txt requests per IP each minute (docs/PHASE4_CONTRACT.md
        /// section 18; crawlers fetch many pages, so the default is high). Default 600. Minimum 1, maximum 100000.
        /// </summary>
        public int PublicPagesPerMinute
        {
            get { return _PublicPagesPerMinute; }
            set { _PublicPagesPerMinute = Math.Clamp(value, 1, 100000); }
        }

        /// <summary>
        /// Anonymous email unsubscribe requests per IP each hour. Default 30. Minimum 1, maximum 10000.
        /// </summary>
        public int UnsubscribePerHour
        {
            get { return _UnsubscribePerHour; }
            set { _UnsubscribePerHour = Math.Clamp(value, 1, 10000); }
        }

        #endregion

        #region Private-Members

        private int _MemberReadsPerMinute = 600;
        private int _UnreadCountPerMinute = 120;
        private int _EditsPerHour = 60;
        private int _MediaGrantsPerHour = 60;
        private int _NotificationWritesPerMinute = 120;
        private int _MemberSearchPerMinute = 120;
        private int _PublicFeedPerMinute = 120;
        private int _PublicPagesPerMinute = 600;
        private int _UnsubscribePerHour = 30;

        #endregion
    }
}
