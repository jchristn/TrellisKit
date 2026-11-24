namespace TrellisKit.Core.Settings
{
    using System;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// Ideas board settings (docs/PHASE5_CONTRACT.md section 14).
    /// </summary>
    public class IdeaSettings
    {
        #region Public-Members

        /// <summary>
        /// Direct lets members submit ideas; StaffOnly has staff publish them (usually from enhancement tickets). Default StaffOnly.
        /// </summary>
        public IdeaSubmissionModeEnum MemberSubmissions { get; set; } = IdeaSubmissionModeEnum.StaffOnly;

        /// <summary>
        /// Whether members may vote on ideas with a closed status. Default false.
        /// </summary>
        public bool VoteOnClosed { get; set; } = false;

        /// <summary>
        /// Votes in this window rank the Trending ordering. Default 14. Minimum 1, maximum 90.
        /// </summary>
        public int TrendingDays
        {
            get { return _TrendingDays; }
            set { _TrendingDays = Math.Clamp(value, 1, 90); }
        }

        /// <summary>
        /// Serve Public ideas on /api/v1/public/ideas to signed-out visitors. Default false.
        /// </summary>
        public bool PublicBoardEnabled { get; set; } = false;

        /// <summary>
        /// Whether forum moderators with every forum in scope handle reported idea comments. Default true.
        /// </summary>
        public bool ForumModeratorsModerate { get; set; } = true;

        /// <summary>
        /// Ideas a member may submit each day. Default 5. Minimum 1, maximum 1000.
        /// </summary>
        public int IdeasPerDay
        {
            get { return _IdeasPerDay; }
            set { _IdeasPerDay = Math.Clamp(value, 1, 1000); }
        }

        /// <summary>
        /// Vote changes a member may make each minute. Default 60. Minimum 1, maximum 10000.
        /// </summary>
        public int VotesPerMinute
        {
            get { return _VotesPerMinute; }
            set { _VotesPerMinute = Math.Clamp(value, 1, 10000); }
        }

        /// <summary>
        /// How often recent vote counts are recomputed. Default 60. Minimum 5, maximum 1440.
        /// </summary>
        public int TrendingIntervalMinutes
        {
            get { return _TrendingIntervalMinutes; }
            set { _TrendingIntervalMinutes = Math.Clamp(value, 5, 1440); }
        }

        #endregion

        #region Private-Members

        private int _TrendingDays = 14;
        private int _IdeasPerDay = 5;
        private int _VotesPerMinute = 60;
        private int _TrendingIntervalMinutes = 60;

        #endregion
    }
}
