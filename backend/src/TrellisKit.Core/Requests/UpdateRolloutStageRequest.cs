namespace TrellisKit.Core.Requests
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// One stage of a rollout plan.
    /// </summary>
    public class UpdateRolloutStageRequest
    {
        /// <summary>
        /// Rollout percentage 0.01 to 100.
        /// </summary>
        public decimal Percentage { get; set; } = 0m;

        /// <summary>
        /// When the stage applies (absolute), or null to use hoursAfterPublish.
        /// </summary>
        public DateTime? StartsUtc { get; set; } = null;

        /// <summary>
        /// Hours after the group's publish time, or null.
        /// </summary>
        public decimal? HoursAfterPublish { get; set; } = null;
    }
}
