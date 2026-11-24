namespace TrellisKit.Core.Settings
{
    using System;

    /// <summary>
    /// Feed post settings (contract section 5).
    /// </summary>
    public class FeedSettings
    {
        #region Public-Members

        /// <summary>
        /// Longest feed post body, in Unicode scalar values. Default 10000. Minimum 100, maximum 50000.
        /// </summary>
        public int MaxBodyLength
        {
            get { return _MaxBodyLength; }
            set { _MaxBodyLength = Math.Clamp(value, 100, 50000); }
        }

        /// <summary>
        /// Media items a feed post may carry. Default 10. Minimum 0, maximum 20.
        /// </summary>
        public int MaxAttachments
        {
            get { return _MaxAttachments; }
            set { _MaxAttachments = Math.Clamp(value, 0, 20); }
        }

        /// <summary>
        /// Videos a feed post may carry. Default 1. Minimum 0, maximum 10.
        /// </summary>
        public int MaxVideosPerPost
        {
            get { return _MaxVideosPerPost; }
            set { _MaxVideosPerPost = Math.Clamp(value, 0, 10); }
        }

        /// <summary>
        /// Published posts that may be pinned at once. Default 3. Minimum 1, maximum 10.
        /// </summary>
        public int MaxPinned
        {
            get { return _MaxPinned; }
            set { _MaxPinned = Math.Clamp(value, 1, 10); }
        }

        /// <summary>
        /// How often scheduled and processing posts are checked for publishing. Default 30. Minimum 5, maximum 600.
        /// </summary>
        public int PublishIntervalSeconds
        {
            get { return _PublishIntervalSeconds; }
            set { _PublishIntervalSeconds = Math.Clamp(value, 5, 600); }
        }

        /// <summary>
        /// Feed posts a member may create each hour. Default 30. Minimum 1, maximum 1000.
        /// </summary>
        public int PostsPerHour
        {
            get { return _PostsPerHour; }
            set { _PostsPerHour = Math.Clamp(value, 1, 1000); }
        }

        #endregion

        #region Private-Members

        private int _MaxBodyLength = 10000;
        private int _MaxAttachments = 10;
        private int _MaxVideosPerPost = 1;
        private int _MaxPinned = 3;
        private int _PublishIntervalSeconds = 30;
        private int _PostsPerHour = 30;

        #endregion
    }
}
