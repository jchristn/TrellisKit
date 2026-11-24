namespace TrellisKit.Core.Responses
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// One rollout plan stage.
    /// </summary>
    public class UpdateRolloutStageView
    {
        /// <summary>
        /// Position.
        /// </summary>
        public int Position { get; set; } = 0;

        /// <summary>
        /// Rollout percentage.
        /// </summary>
        public decimal Percentage { get; set; } = 0m;

        /// <summary>
        /// When it applies.
        /// </summary>
        public DateTime StartsUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// When it was applied, or null.
        /// </summary>
        public DateTime? AppliedUtc { get; set; } = null;
    }
}
