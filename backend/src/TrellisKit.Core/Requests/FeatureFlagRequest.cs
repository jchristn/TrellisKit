namespace TrellisKit.Core.Requests
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// Body of POST and PUT /api/v1/admin/app/flags.
    /// </summary>
    public class FeatureFlagRequest
    {
        /// <summary>
        /// Key (immutable after the first publish).
        /// </summary>
        public string? Key { get; set; } = null;

        /// <summary>
        /// Name.
        /// </summary>
        public string? Name { get; set; } = null;

        /// <summary>
        /// Description.
        /// </summary>
        public string? Description { get; set; } = null;

        /// <summary>
        /// Boolean, Variant, or Value.
        /// </summary>
        public FeatureFlagKindEnum? Kind { get; set; } = null;

        /// <summary>
        /// Value type.
        /// </summary>
        public FeatureFlagValueTypeEnum? ValueType { get; set; } = null;

        /// <summary>
        /// Variants (Variant kind).
        /// </summary>
        public List<string>? Variants { get; set; } = null;

        /// <summary>
        /// Default value.
        /// </summary>
        public JsonElement? DefaultValue { get; set; } = null;

        /// <summary>
        /// Value when disabled.
        /// </summary>
        public JsonElement? OffValue { get; set; } = null;

        /// <summary>
        /// Enabled.
        /// </summary>
        public bool? Enabled { get; set; } = null;

        /// <summary>
        /// Client types (mobile, web, server).
        /// </summary>
        public List<string>? Clients { get; set; } = null;

        /// <summary>
        /// Rules (replace the whole list).
        /// </summary>
        public List<FlagRuleRequest>? Rules { get; set; } = null;

        /// <summary>
        /// Labels, or null.
        /// </summary>
        public List<string>? Labels { get; set; } = null;

        /// <summary>
        /// Tags, or null.
        /// </summary>
        public Dictionary<string, string>? Tags { get; set; } = null;
    }
}
