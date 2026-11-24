namespace TrellisKit.Core.Models
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// A feature flag or kill switch revision (table featureflags with its variants, clients, and rules).
    /// </summary>
    public class FeatureFlag
    {
        /// <summary>
        /// Revision identifier (ffv_).
        /// </summary>
        public string RevisionId { get; set; } = String.Empty;

        /// <summary>
        /// Object identifier (ffl_), stable across revisions.
        /// </summary>
        public string Id { get; set; } = String.Empty;

        /// <summary>
        /// Flag key, or the engine feature key of a kill switch.
        /// </summary>
        public string Key { get; set; } = String.Empty;

        /// <summary>
        /// Display name.
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
        /// Variant values (Variant kind).
        /// </summary>
        public List<string> Variants { get; set; } = new List<string>();

        /// <summary>
        /// Value when enabled and no rule matches (typed text).
        /// </summary>
        public string DefaultValue { get; set; } = String.Empty;

        /// <summary>
        /// Value when disabled (typed text).
        /// </summary>
        public string OffValue { get; set; } = String.Empty;

        /// <summary>
        /// Whether rules are evaluated.
        /// </summary>
        public bool Enabled { get; set; } = true;

        /// <summary>
        /// Client types the flag is sent to (mobile, web, server).
        /// </summary>
        public List<string> Clients { get; set; } = new List<string>();

        /// <summary>
        /// Ordered rules.
        /// </summary>
        public List<FeatureFlagRule> Rules { get; set; } = new List<FeatureFlagRule>();

        /// <summary>
        /// Kill switch message shown to members, or null.
        /// </summary>
        public string? Message { get; set; } = null;

        /// <summary>
        /// Whether a killed switch blocks the feature's API routes.
        /// </summary>
        public bool BlocksServer { get; set; } = true;

        /// <summary>
        /// Publication state.
        /// </summary>
        public FeatureFlagStateEnum State { get; set; } = FeatureFlagStateEnum.Draft;

        /// <summary>
        /// First content version of this revision, or null for drafts.
        /// </summary>
        public long? ValidFromVersion { get; set; } = null;

        /// <summary>
        /// Version that replaced this revision, or null when current.
        /// </summary>
        public long? ValidToVersion { get; set; } = null;

        /// <summary>
        /// Whether this is the pending draft revision.
        /// </summary>
        public bool IsDraft { get; set; } = false;

        /// <summary>
        /// Last editor, or null.
        /// </summary>
        public string? EditedByUserId { get; set; } = null;

        /// <summary>
        /// Object created time.
        /// </summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Revision time.
        /// </summary>
        public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;
    }
}
