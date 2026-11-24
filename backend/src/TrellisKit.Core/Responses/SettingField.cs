namespace TrellisKit.Core.Responses
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// One editable setting with the metadata the admin console needs to render and validate it.
    /// </summary>
    public class SettingField : CatalogEntryMetadata
    {
        #region Public-Members

        /// <summary>
        /// Setting key, for example "requestHistory.retentionDays".
        /// </summary>
        public string Key { get; set; } = String.Empty;

        /// <summary>
        /// Section heading.
        /// </summary>
        public string Section { get; set; } = String.Empty;

        /// <summary>
        /// Field label.
        /// </summary>
        public string Label { get; set; } = String.Empty;

        /// <summary>
        /// Help text.
        /// </summary>
        public string Description { get; set; } = String.Empty;

        /// <summary>
        /// Data type.
        /// </summary>
        public SettingTypeEnum Type { get; set; } = SettingTypeEnum.String;

        /// <summary>
        /// Current configured value. Always null for secrets.
        /// </summary>
        public object? Value { get; set; } = null;

        /// <summary>
        /// Allowed values for Enum fields, otherwise null.
        /// </summary>
        public List<string>? Options { get; set; } = null;

        /// <summary>
        /// Minimum for Integer fields, otherwise null.
        /// </summary>
        public long? Min { get; set; } = null;

        /// <summary>
        /// Maximum for Integer fields, otherwise null.
        /// </summary>
        public long? Max { get; set; } = null;

        /// <summary>
        /// Minimum of a Decimal setting, or null.
        /// </summary>
        public double? MinDecimal { get; set; } = null;

        /// <summary>
        /// Maximum of a Decimal setting, or null.
        /// </summary>
        public double? MaxDecimal { get; set; } = null;

        /// <summary>
        /// Whether a change applies live or after restart.
        /// </summary>
        public SettingApplyModeEnum ApplyMode { get; set; } = SettingApplyModeEnum.Live;

        /// <summary>
        /// Whether an environment variable overrides the value (the field is read-only in the console).
        /// </summary>
        public bool OverriddenByEnvironment { get; set; } = false;

        /// <summary>
        /// Name of the overriding environment variable, or null.
        /// </summary>
        public string? EnvironmentVariable { get; set; } = null;

        /// <summary>
        /// For Secret fields, whether a value is set. Null for other types.
        /// </summary>
        public bool? IsSet { get; set; } = null;

        #endregion
    }
}
