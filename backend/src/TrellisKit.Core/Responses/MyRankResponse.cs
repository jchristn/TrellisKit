namespace TrellisKit.Core.Responses
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// GET /api/v1/me/rank: the caller's effective rank per Rank field, recent history, lineage, and next requirements.
    /// </summary>
    public class MyRankResponse
    {
        #region Public-Members

        /// <summary>
        /// Whether clients show verified or self-reported labels (ranks.showVerificationLabels).
        /// </summary>
        public bool ShowVerificationLabels { get; set; } = true;

        /// <summary>
        /// One entry per enabled Rank profile field (at most 10).
        /// </summary>
        public List<MyRankFieldView> Fields { get; set; } = new List<MyRankFieldView>();

        /// <summary>
        /// The 20 most recent awards and revokes, newest first.
        /// </summary>
        public List<RankPromotionView> History { get; set; } = new List<RankPromotionView>();

        /// <summary>
        /// Total awards and revokes.
        /// </summary>
        public long HistoryTotal { get; set; } = 0;

        /// <summary>
        /// Lineage entries (at most 50 per field, 100 in all).
        /// </summary>
        public List<LineageEntryView> Lineage { get; set; } = new List<LineageEntryView>();

        #endregion
    }
}
