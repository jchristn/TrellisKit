namespace TrellisKit.Core.Requests
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Body of PUT /api/v1/admin/update-groups/{id}/rollout-plan.
    /// </summary>
    public class UpdateRolloutPlanRequest
    {
        /// <summary>
        /// Stages (at most 10, increasing percentage and time); empty or null clears the plan.
        /// </summary>
        public List<UpdateRolloutStageRequest>? Stages { get; set; } = null;

        /// <summary>
        /// Replace the stages with the property template (setting updates.rolloutPlanTemplate).
        /// </summary>
        public bool UseTemplate { get; set; } = false;

        /// <summary>
        /// Automatic halt on, or null to keep.
        /// </summary>
        public bool? AutoHaltEnabled { get; set; } = null;

        /// <summary>
        /// Failure percentage that halts, 1 to 100, or null to keep.
        /// </summary>
        public int? AutoHaltFailurePercent { get; set; } = null;

        /// <summary>
        /// Installs before judging, 1 to 100000, or null to keep.
        /// </summary>
        public int? AutoHaltMinInstalls { get; set; } = null;
    }
}
