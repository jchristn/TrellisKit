namespace TrellisKit.Core.Responses
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// A reminder's state (staff).
    /// </summary>
    public class EventReminderStatusView
    {
        #region Public-Members

        /// <summary>
        /// Minutes before the start.
        /// </summary>
        public int OffsetMinutes { get; set; } = 0;

        /// <summary>
        /// When due.
        /// </summary>
        public DateTime DueUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// When sent, or null.
        /// </summary>
        public DateTime? SentUtc { get; set; } = null;

        /// <summary>
        /// When skipped, or null.
        /// </summary>
        public DateTime? SkippedUtc { get; set; } = null;

        #endregion
    }
}
