namespace TrellisKit.Core.Requests
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Body of rollout, publish, and promotion lifecycle routes.
    /// </summary>
    public class UpdateRolloutRequest
    {
        /// <summary>
        /// Rollout percentage 0.01 to 100, or null.
        /// </summary>
        public decimal? RolloutPercentage { get; set; } = null;

        /// <summary>
        /// Limit a rollout change to one platform, or null for both.
        /// </summary>
        public string? Platform { get; set; } = null;

        /// <summary>
        /// Complete an older update still mid-rollout on the same target.
        /// </summary>
        public bool Supersede { get; set; } = false;

        /// <summary>
        /// Target channel of a promotion.
        /// </summary>
        public string? Channel { get; set; } = null;

        /// <summary>
        /// Message of a promotion or rollback, or null to reuse the source message.
        /// </summary>
        public string? Message { get; set; } = null;

        /// <summary>
        /// Reason (pause, resume), or null.
        /// </summary>
        public string? Reason { get; set; } = null;
    }
}
