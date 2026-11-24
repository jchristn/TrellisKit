namespace TrellisKit.Core.Responses
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// The active feed card of a promoted thread.
    /// </summary>
    public class ThreadPromotionView
    {
        #region Public-Members

        /// <summary>
        /// Feed card identifier.
        /// </summary>
        public string FeedPostId { get; set; } = String.Empty;

        /// <summary>
        /// Who promoted it.
        /// </summary>
        public UserSummary? PromotedBy { get; set; } = null;

        /// <summary>
        /// When it was promoted.
        /// </summary>
        public DateTime PromotedUtc { get; set; } = DateTime.UtcNow;

        #endregion
    }
}
