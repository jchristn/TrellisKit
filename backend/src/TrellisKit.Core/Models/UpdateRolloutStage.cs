namespace TrellisKit.Core.Models
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// One stage of a rollout plan (table updaterolloutstages).
    /// </summary>
    public class UpdateRolloutStage
    {
        /// <summary>
        /// Position (0 first).
        /// </summary>
        public int Position { get; set; } = 0;

        /// <summary>
        /// Rollout at this stage in basis points.
        /// </summary>
        public int BasisPoints { get; set; } = 0;

        /// <summary>
        /// When the stage applies.
        /// </summary>
        public DateTime StartsUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// When the worker applied it, or null.
        /// </summary>
        public DateTime? AppliedUtc { get; set; } = null;
    }
}
