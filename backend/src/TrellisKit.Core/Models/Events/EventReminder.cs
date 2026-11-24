namespace TrellisKit.Core.Models
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// A scheduled reminder of an event (docs/PHASE4_CONTRACT.md section 4.4).
    /// </summary>
    public class EventReminder
    {
        #region Public-Members

        /// <summary>
        /// Identifier (erm_).
        /// </summary>
        public string Id { get; set; } = String.Empty;

        /// <summary>
        /// Event.
        /// </summary>
        public string EventId { get; set; } = String.Empty;

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
        /// When skipped (late), or null.
        /// </summary>
        public DateTime? SkippedUtc { get; set; } = null;

        /// <summary>
        /// When a worker claimed it, or null.
        /// </summary>
        public DateTime? ClaimedUtc { get; set; } = null;

        #endregion
    }
}
