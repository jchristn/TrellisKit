namespace TrellisKit.Core.Responses
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// The feed card of a trending thread.
    /// </summary>
    public class TrendingPromotionView
    {
        #region Public-Members

        /// <summary>
        /// Feed card identifier.
        /// </summary>
        public string FeedPostId { get; set; } = String.Empty;

        /// <summary>
        /// When it was promoted.
        /// </summary>
        public DateTime PromotedUtc { get; set; } = DateTime.UtcNow;

        #endregion
    }
}
