namespace TrellisKit.Core.Responses
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Result of the flag Test as preview.
    /// </summary>
    public class FlagEvaluationView
    {
        /// <summary>
        /// Resolved value.
        /// </summary>
        public object? Value { get; set; } = null;

        /// <summary>
        /// Rule that decided, or null.
        /// </summary>
        public string? MatchedRuleId { get; set; } = null;

        /// <summary>
        /// Disabled, Rule, RuleOutsideRollout, or Default.
        /// </summary>
        public string Reason { get; set; } = String.Empty;

        /// <summary>
        /// Rollout bucket 0 to 9999 of the last rule checked, or null.
        /// </summary>
        public int? Bucket { get; set; } = null;
    }
}
