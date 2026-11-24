namespace TrellisKit.Core.Responses
{
    using System;

    /// <summary>
    /// Result of POST {prefix}/{id}/unpromote for a lesson or an event (docs/PHASE4_CONTRACT.md section 5.8).
    /// </summary>
    public class TargetPromotionView
    {
        #region Public-Members

        /// <summary>
        /// Source type (Lesson, Event).
        /// </summary>
        public string TargetType { get; set; } = String.Empty;

        /// <summary>
        /// Source identifier.
        /// </summary>
        public string TargetId { get; set; } = String.Empty;

        /// <summary>
        /// Whether a feed card existed and was removed by this call.
        /// </summary>
        public bool Unpromoted { get; set; } = false;

        /// <summary>
        /// The source's current feed card (always null after unpromote).
        /// </summary>
        public string? FeedPostId { get; set; } = null;

        #endregion
    }
}
