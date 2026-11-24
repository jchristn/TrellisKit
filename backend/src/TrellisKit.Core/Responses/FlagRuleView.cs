namespace TrellisKit.Core.Responses
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// A targeting rule.
    /// </summary>
    public class FlagRuleView
    {
        /// <summary>
        /// Identifier (ffr_).
        /// </summary>
        public string Id { get; set; } = String.Empty;

        /// <summary>
        /// Position.
        /// </summary>
        public int Position { get; set; } = 0;

        /// <summary>
        /// Description, or null.
        /// </summary>
        public string? Description { get; set; } = null;

        /// <summary>
        /// Conditions (bounded by appConfig.maxConditionsPerRule).
        /// </summary>
        public List<FlagConditionView> Conditions { get; set; } = new List<FlagConditionView>();

        /// <summary>
        /// Value when matched.
        /// </summary>
        public object? Value { get; set; } = null;

        /// <summary>
        /// Share of matching callers.
        /// </summary>
        public decimal RolloutPercentage { get; set; } = 0m;
    }
}
