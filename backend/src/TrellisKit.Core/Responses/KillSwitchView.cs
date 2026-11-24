namespace TrellisKit.Core.Responses
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// One kill switch row of the console panel.
    /// </summary>
    public class KillSwitchView
    {
        /// <summary>
        /// Flag object id.
        /// </summary>
        public string Id { get; set; } = String.Empty;

        /// <summary>
        /// Engine feature key.
        /// </summary>
        public string FeatureKey { get; set; } = String.Empty;

        /// <summary>
        /// Whether any rule kills the feature.
        /// </summary>
        public bool Killed { get; set; } = false;

        /// <summary>
        /// Whether a rule without conditions kills it for everyone.
        /// </summary>
        public bool KilledForEveryone { get; set; } = false;

        /// <summary>
        /// Message, or null.
        /// </summary>
        public string? Message { get; set; } = null;

        /// <summary>
        /// Whether the API blocks the feature's routes.
        /// </summary>
        public bool BlocksServer { get; set; } = false;

        /// <summary>
        /// Rules.
        /// </summary>
        public int RuleCount { get; set; } = 0;

        /// <summary>
        /// Targeting rules (bounded by appConfig.maxRulesPerFlag).
        /// </summary>
        public List<FlagRuleView> Rules { get; set; } = new List<FlagRuleView>();

        /// <summary>
        /// Kill switch revision of the last change.
        /// </summary>
        public long Revision { get; set; } = 0;

        /// <summary>
        /// Last editor, or null.
        /// </summary>
        public string? LastChangedBy { get; set; } = null;

        /// <summary>
        /// Reason of the last change, or null.
        /// </summary>
        public string? LastChangedReason { get; set; } = null;

        /// <summary>
        /// Last change, or null.
        /// </summary>
        public DateTime? LastChangedUtc { get; set; } = null;

        /// <summary>
        /// Labels.
        /// </summary>
        public List<string> Labels { get; set; } = new List<string>();

        /// <summary>
        /// Tags.
        /// </summary>
        public Dictionary<string, string> Tags { get; set; } = new Dictionary<string, string>(StringComparer.Ordinal);

        /// <summary>
        /// Created time.
        /// </summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    }
}
