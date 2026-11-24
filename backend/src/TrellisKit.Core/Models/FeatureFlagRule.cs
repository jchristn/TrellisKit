namespace TrellisKit.Core.Models
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// One ordered targeting rule of a flag or kill switch (table featureflagrules).
    /// </summary>
    public class FeatureFlagRule
    {
        /// <summary>
        /// Identifier (ffr_).
        /// </summary>
        public string Id { get; set; } = String.Empty;

        /// <summary>
        /// Position (1 first).
        /// </summary>
        public int Position { get; set; } = 0;

        /// <summary>
        /// Description, or null.
        /// </summary>
        public string? Description { get; set; } = null;

        /// <summary>
        /// Value returned when the rule matches (typed text).
        /// </summary>
        public string Value { get; set; } = String.Empty;

        /// <summary>
        /// Share of matching callers in basis points.
        /// </summary>
        public int RolloutBasisPoints { get; set; } = 10000;

        /// <summary>
        /// Conditions (all must match).
        /// </summary>
        public List<FeatureFlagCondition> Conditions { get; set; } = new List<FeatureFlagCondition>();
    }
}
