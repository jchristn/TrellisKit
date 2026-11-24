namespace TrellisKit.Core.Responses
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// FeatureFlag payload (section 12.1). Kill switches add message and blocksServer.
    /// </summary>
    public class FeatureFlagView
    {
        /// <summary>
        /// Identifier (ffl_).
        /// </summary>
        public string Id { get; set; } = String.Empty;

        /// <summary>
        /// Key.
        /// </summary>
        public string Key { get; set; } = String.Empty;

        /// <summary>
        /// Name.
        /// </summary>
        public string Name { get; set; } = String.Empty;

        /// <summary>
        /// Description, or null.
        /// </summary>
        public string? Description { get; set; } = null;

        /// <summary>
        /// Kind.
        /// </summary>
        public FeatureFlagKindEnum Kind { get; set; } = FeatureFlagKindEnum.Boolean;

        /// <summary>
        /// Value type.
        /// </summary>
        public FeatureFlagValueTypeEnum ValueType { get; set; } = FeatureFlagValueTypeEnum.Boolean;

        /// <summary>
        /// Variants (at most 20).
        /// </summary>
        public List<string> Variants { get; set; } = new List<string>();

        /// <summary>
        /// Default value.
        /// </summary>
        public object? DefaultValue { get; set; } = null;

        /// <summary>
        /// Value when disabled.
        /// </summary>
        public object? OffValue { get; set; } = null;

        /// <summary>
        /// Enabled.
        /// </summary>
        public bool Enabled { get; set; } = false;

        /// <summary>
        /// Client types (at most 3).
        /// </summary>
        public List<string> Clients { get; set; } = new List<string>();

        /// <summary>
        /// Rules (bounded by appConfig.maxRulesPerFlag).
        /// </summary>
        public List<FlagRuleView> Rules { get; set; } = new List<FlagRuleView>();

        /// <summary>
        /// Kill switch message, or null.
        /// </summary>
        public string? Message { get; set; } = null;

        /// <summary>
        /// Kill switch blocks the API.
        /// </summary>
        public bool BlocksServer { get; set; } = false;

        /// <summary>
        /// State.
        /// </summary>
        public FeatureFlagStateEnum State { get; set; } = FeatureFlagStateEnum.Draft;

        /// <summary>
        /// Whether a draft is pending.
        /// </summary>
        public bool HasDraftChanges { get; set; } = false;

        /// <summary>
        /// Content version of this revision, or null for drafts.
        /// </summary>
        public long? PublishedInVersion { get; set; } = null;

        /// <summary>
        /// Revision tag for If-Match.
        /// </summary>
        public string ETag { get; set; } = String.Empty;

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

        /// <summary>
        /// Revision time.
        /// </summary>
        public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;
    }
}
