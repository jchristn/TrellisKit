namespace TrellisKit.Core.Responses
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// A trending queue row (docs/PHASE3_CONTRACT.md section 5.3).
    /// </summary>
    public class TrendingItemView : IMetadataPayload
    {
        #region Public-Members

        /// <summary>
        /// The thread.
        /// </summary>
        public ThreadSummaryView Thread
        {
            get { return _Thread; }
            set { _Thread = value ?? new ThreadSummaryView(); }
        }

        /// <summary>
        /// Score terms.
        /// </summary>
        public TrendingBreakdown Breakdown
        {
            get { return _Breakdown; }
            set { _Breakdown = value ?? new TrendingBreakdown(); }
        }

        /// <summary>
        /// Position in the result (1 based).
        /// </summary>
        public int Rank { get; set; } = 0;

        /// <summary>
        /// When the stored score (thread.trendingScore) was computed, or null before the first worker pass.
        /// </summary>
        public DateTime? ComputedUtc { get; set; } = null;

        /// <summary>
        /// Active feed card, or null.
        /// </summary>
        public TrendingPromotionView? Promotion { get; set; } = null;

        /// <summary>
        /// The thread's labels.
        /// </summary>
        public List<string> Labels
        {
            get { return _Labels; }
            set { _Labels = value ?? new List<string>(); }
        }

        /// <summary>
        /// The thread's tags.
        /// </summary>
        public Dictionary<string, string> Tags
        {
            get { return _Tags; }
            set { _Tags = value ?? new Dictionary<string, string>(); }
        }

        #endregion

        #region Private-Members

        private ThreadSummaryView _Thread = new ThreadSummaryView();
        private TrendingBreakdown _Breakdown = new TrendingBreakdown();
        private List<string> _Labels = new List<string>();
        private Dictionary<string, string> _Tags = new Dictionary<string, string>();

        #endregion
    }
}
