namespace TrellisKit.Core.Responses
{
    using System;

    /// <summary>
    /// Ninety-day moderation history of a target's author.
    /// </summary>
    public class ModerationAuthorHistory
    {
        #region Public-Members

        /// <summary>
        /// Remove actions on the author's content in 90 days.
        /// </summary>
        public long RemovedCount90d { get; set; } = 0;

        /// <summary>
        /// Hide and AutoHide actions on the author's content in 90 days.
        /// </summary>
        public long HiddenCount90d { get; set; } = 0;

        /// <summary>
        /// Reports on the author's content or profile in 90 days.
        /// </summary>
        public long ReportsAgainst90d { get; set; } = 0;

        /// <summary>
        /// Whether the author is muted.
        /// </summary>
        public bool IsMuted { get; set; } = false;

        /// <summary>
        /// Mute end, or null.
        /// </summary>
        public DateTime? MutedUntilUtc { get; set; } = null;

        #endregion
    }
}
