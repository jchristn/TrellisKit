namespace TrellisKit.Core.Responses
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Automatic halt configuration and status of a group.
    /// </summary>
    public class UpdateAutoHaltView
    {
        /// <summary>
        /// Whether the automatic halt is on.
        /// </summary>
        public bool Enabled { get; set; } = false;

        /// <summary>
        /// Failure percentage that halts.
        /// </summary>
        public int FailurePercent { get; set; } = 0;

        /// <summary>
        /// Installs before judging.
        /// </summary>
        public int MinInstalls { get; set; } = 0;

        /// <summary>
        /// Whether the automatic halt paused the group.
        /// </summary>
        public bool Halted { get; set; } = false;

        /// <summary>
        /// When, or null.
        /// </summary>
        public DateTime? HaltedUtc { get; set; } = null;

        /// <summary>
        /// Why, or null.
        /// </summary>
        public string? HaltReason { get; set; } = null;

        /// <summary>
        /// Installs that failed to launch an update of the group (detail only).
        /// </summary>
        public long? Failures { get; set; } = null;

        /// <summary>
        /// Installs running an update of the group (detail only).
        /// </summary>
        public long? Successes { get; set; } = null;

        /// <summary>
        /// Failure share in percent (detail only), or null with no installs.
        /// </summary>
        public decimal? FailureRatePercent { get; set; } = null;
    }
}
