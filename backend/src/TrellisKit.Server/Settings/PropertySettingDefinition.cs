namespace TrellisKit.Server.Settings
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;
    using System.Text.Json;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// One per-property setting (docs/MULTIPROPERTY_CONTRACT.md section 5.2): stored in propertysettings, edited by the
    /// property's SystemAdmin and platform admins, never set from the environment. Secret values are encrypted.
    /// </summary>
    public class PropertySettingDefinition
    {
        #region Public-Members

        /// <summary>
        /// Key, for example email.mailgunApiKey.
        /// </summary>
        public string Key { get; }

        /// <summary>
        /// Admin portal section.
        /// </summary>
        public string Section { get; }

        /// <summary>
        /// Label.
        /// </summary>
        public string Label { get; }

        /// <summary>
        /// Description.
        /// </summary>
        public string Description { get; }

        /// <summary>
        /// Type.
        /// </summary>
        public SettingTypeEnum Type { get; }

        /// <summary>
        /// Default value in string form (empty for none).
        /// </summary>
        public string Default { get; }

        /// <summary>
        /// Enum values, or null.
        /// </summary>
        public List<string>? Options { get; }

        /// <summary>
        /// Integer minimum, or null.
        /// </summary>
        public long? Min { get; }

        /// <summary>
        /// Integer maximum, or null.
        /// </summary>
        public long? Max { get; }

        /// <summary>
        /// Longest text value. Default 500.
        /// </summary>
        public int MaxLength { get; }

        /// <summary>
        /// Extra validation of text values: returns an error message, or null when valid. Default null.
        /// </summary>
        public Func<string, string?>? Validator { get; set; } = null;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate a definition.
        /// </summary>
        /// <param name="key">Key.</param>
        /// <param name="section">Section.</param>
        /// <param name="label">Label.</param>
        /// <param name="description">Description.</param>
        /// <param name="type">Type.</param>
        /// <param name="defaultValue">Default value.</param>
        /// <param name="options">Enum values, or null.</param>
        /// <param name="min">Integer minimum, or null.</param>
        /// <param name="max">Integer maximum, or null.</param>
        /// <param name="maxLength">Longest text value.</param>
        /// <exception cref="ArgumentNullException">The key is null or empty.</exception>
        public PropertySettingDefinition(string key, string section, string label, string description, SettingTypeEnum type, string defaultValue = "", List<string>? options = null, long? min = null, long? max = null, int maxLength = 500)
        {
            if (String.IsNullOrWhiteSpace(key)) throw new ArgumentNullException(nameof(key));
            Key = key;
            Section = section ?? String.Empty;
            Label = label ?? String.Empty;
            Description = description ?? String.Empty;
            Type = type;
            Default = defaultValue ?? String.Empty;
            Options = options;
            Min = min;
            Max = max;
            MaxLength = maxLength;
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Validate a JSON value and return its stored string form.
        /// </summary>
        /// <param name="value">Value.</param>
        /// <param name="error">Error message when invalid.</param>
        /// <returns>String form, or null when invalid.</returns>
        public string? Normalize(JsonElement value, out string? error)
        {
            error = null;
            switch (Type)
            {
                case SettingTypeEnum.Boolean:
                    if (value.ValueKind == JsonValueKind.True || value.ValueKind == JsonValueKind.False) return value.GetBoolean() ? "true" : "false";
                    error = "Must be true or false.";
                    return null;
                case SettingTypeEnum.Integer:
                    if (value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out long number) && (Min == null || number >= Min) && (Max == null || number <= Max))
                        return number.ToString(CultureInfo.InvariantCulture);
                    error = "Must be a whole number" + (Min != null && Max != null ? " between " + Min + " and " + Max : String.Empty) + ".";
                    return null;
                case SettingTypeEnum.Enum:
                    string? choice = value.ValueKind == JsonValueKind.String ? value.GetString() : null;
                    string? match = Options?.FirstOrDefault(o => String.Equals(o, choice, StringComparison.OrdinalIgnoreCase));
                    if (match != null) return match;
                    error = "Must be one of: " + String.Join(", ", Options ?? new List<string>()) + ".";
                    return null;
                default:
                    if (value.ValueKind != JsonValueKind.String) { error = "Must be text."; return null; }
                    string text = value.GetString()!.Trim();
                    if (text.Length > MaxLength) { error = "At most " + MaxLength + " characters."; return null; }
                    string? invalid = Validator?.Invoke(text);
                    if (invalid != null) { error = invalid; return null; }
                    return text;
            }
        }

        #endregion
    }
}
