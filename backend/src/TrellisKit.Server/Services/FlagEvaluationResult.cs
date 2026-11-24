namespace TrellisKit.Server.Services
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// The outcome of evaluating one flag.
    /// </summary>
    public class FlagEvaluationResult
    {
        /// <summary>
        /// Resolved value (typed text).
        /// </summary>
        public string Value { get; set; } = String.Empty;

        /// <summary>
        /// Rule that decided, or null.
        /// </summary>
        public string? MatchedRuleId { get; set; } = null;

        /// <summary>
        /// Disabled, Rule, RuleOutsideRollout, or Default.
        /// </summary>
        public string Reason { get; set; } = "Default";

        /// <summary>
        /// Bucket of the last rule checked, or null.
        /// </summary>
        public int? Bucket { get; set; } = null;

        /// <summary>
        /// Whether the matching rule names SystemAdmin or a user id explicitly.
        /// </summary>
        public bool RuleTargetsAdmins { get; set; } = false;
    }
}
