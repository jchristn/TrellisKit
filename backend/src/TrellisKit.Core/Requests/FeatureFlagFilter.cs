namespace TrellisKit.Core.Requests
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// Typed filters of the feature flag enumeration.
    /// </summary>
    public class FeatureFlagFilter
    {
        /// <summary>
        /// Kind (never KillSwitch), or null.
        /// </summary>
        public FeatureFlagKindEnum? Kind { get; set; } = null;

        /// <summary>
        /// State, or null.
        /// </summary>
        public FeatureFlagStateEnum? State { get; set; } = null;

        /// <summary>
        /// Whether the flag has a pending draft, or null.
        /// </summary>
        public bool? HasDraftChanges { get; set; } = null;

        /// <summary>
        /// Client type (mobile, web, server), or null.
        /// </summary>
        public string? Client { get; set; } = null;
    }
}
