namespace TrellisKit.Core.Responses
{
    using System;

    /// <summary>
    /// One Rank field in GET /api/v1/me/rank.
    /// </summary>
    public class MyRankFieldView
    {
        #region Public-Members

        /// <summary>
        /// Effective rank.
        /// </summary>
        public EffectiveRankView Rank { get; set; } = new EffectiveRankView();

        /// <summary>
        /// Next rank and requirements, or null at the highest rank or without a value.
        /// </summary>
        public RankNextView? Next { get; set; } = null;

        #endregion
    }
}
