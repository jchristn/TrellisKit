namespace TrellisKit.Core.Models
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// One occurrence produced by a recurrence rule.
    /// </summary>
    public class EventOccurrenceSlot
    {
        #region Public-Members

        /// <summary>
        /// Position in the rule (0 based).
        /// </summary>
        public int Index { get; set; } = 0;

        /// <summary>
        /// Local wall time of the start (unspecified kind).
        /// </summary>
        public DateTime StartLocal { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Start (UTC).
        /// </summary>
        public DateTime StartUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// End (UTC).
        /// </summary>
        public DateTime EndUtc { get; set; } = DateTime.UtcNow;

        #endregion
    }
}
