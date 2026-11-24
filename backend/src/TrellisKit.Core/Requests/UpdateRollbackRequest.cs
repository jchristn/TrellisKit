namespace TrellisKit.Core.Requests
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Body of the rollback routes (section 7.3).
    /// </summary>
    public class UpdateRollbackRequest
    {
        /// <summary>
        /// Group whose content to republish (rollback).
        /// </summary>
        public string? TargetUpdateGroupId { get; set; } = null;

        /// <summary>
        /// ios, android, or all (rollback to embedded).
        /// </summary>
        public string? Platform { get; set; } = null;

        /// <summary>
        /// Runtime version (rollback to embedded).
        /// </summary>
        public string? RuntimeVersion { get; set; } = null;

        /// <summary>
        /// Channel (rollback to embedded).
        /// </summary>
        public string? Channel { get; set; } = null;

        /// <summary>
        /// Rollout percentage, default 100.
        /// </summary>
        public decimal? RolloutPercentage { get; set; } = null;

        /// <summary>
        /// Message.
        /// </summary>
        public string? Message { get; set; } = null;
    }
}
