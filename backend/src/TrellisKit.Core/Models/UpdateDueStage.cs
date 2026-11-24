namespace TrellisKit.Core.Models
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// A rollout plan stage whose start time has passed (worker input).
    /// </summary>
    public class UpdateDueStage
    {
        /// <summary>
        /// Group.
        /// </summary>
        public string GroupId { get; set; } = String.Empty;

        /// <summary>
        /// Stage position.
        /// </summary>
        public int Position { get; set; } = 0;

        /// <summary>
        /// Rollout to apply, in basis points.
        /// </summary>
        public int BasisPoints { get; set; } = 0;
    }
}
