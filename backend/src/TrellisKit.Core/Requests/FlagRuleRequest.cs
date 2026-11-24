namespace TrellisKit.Core.Requests
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// One targeting rule in a request.
    /// </summary>
    public class FlagRuleRequest
    {
        /// <summary>
        /// Existing rule id to keep its rollout buckets, or null for a new rule.
        /// </summary>
        public string? Id { get; set; } = null;

        /// <summary>
        /// Description, or null.
        /// </summary>
        public string? Description { get; set; } = null;

        /// <summary>
        /// Conditions (all must match).
        /// </summary>
        public List<FlagConditionRequest> Conditions { get; set; } = new List<FlagConditionRequest>();

        /// <summary>
        /// Value when matched (kill switches: ignored, a match kills).
        /// </summary>
        public JsonElement? Value { get; set; } = null;

        /// <summary>
        /// Share of matching callers, default 100.
        /// </summary>
        public decimal? RolloutPercentage { get; set; } = null;
    }
}
