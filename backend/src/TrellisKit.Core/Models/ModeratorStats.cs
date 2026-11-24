namespace TrellisKit.Core.Models
{
    using System;

    /// <summary>
    /// Thirty-day activity of one moderator (Phase 2 contract section 10.4).
    /// </summary>
    public class ModeratorStats
    {
        #region Public-Members

        /// <summary>
        /// Moderator.
        /// </summary>
        public string UserId { get; set; } = String.Empty;

        /// <summary>
        /// Moderation actions taken in the window.
        /// </summary>
        public long Actions { get; set; } = 0;

        /// <summary>
        /// Reports resolved in the window after their review target.
        /// </summary>
        public long OverdueHandled { get; set; } = 0;

        /// <summary>
        /// Median minutes from report to resolution in the window (over the most recent resolutions), or null.
        /// </summary>
        public double? MedianResponseMinutes { get; set; } = null;

        #endregion
    }
}
