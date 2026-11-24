namespace TrellisKit.Server.Settings
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;
    using System.Text.Json;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Settings;

    /// <summary>
    /// Definition of one runtime-editable setting: metadata for the console, plus typed access to
    /// <see cref="AppSettings"/>. Create instances with the static factory methods.
    /// </summary>
    public class SettingFieldDefinition
    {
        #region Public-Members

        /// <summary>
        /// Setting key, for example "requestHistory.retentionDays".
        /// </summary>
        public string Key { get; }

        /// <summary>
        /// Section heading.
        /// </summary>
        public string Section { get; }

        /// <summary>
        /// Field label.
        /// </summary>
        public string Label { get; }

        /// <summary>
        /// Help text.
        /// </summary>
        public string Description { get; }

        /// <summary>
        /// Data type.
        /// </summary>
        public SettingTypeEnum Type { get; }

        /// <summary>
        /// When a change takes effect.
        /// </summary>
        public SettingApplyModeEnum ApplyMode { get; }

        /// <summary>
        /// Environment variable that overrides this setting, or null.
        /// </summary>
        public string? EnvironmentVariable { get; }

        /// <summary>
        /// Minimum for Integer fields, otherwise null.
        /// </summary>
        public long? Min { get; private set; }

        /// <summary>
        /// Maximum for Integer fields, otherwise null.
        /// </summary>
        public long? Max { get; private set; }

        /// <summary>
        /// Allowed values for Enum fields, otherwise null.
        /// </summary>
        public List<string>? Options { get; private set; }

        /// <summary>
        /// Minimum of a Decimal setting, or null.
        /// </summary>
        public double? MinDecimal { get; private set; }

        /// <summary>
        /// Maximum of a Decimal setting, or null.
        /// </summary>
        public double? MaxDecimal { get; private set; }

        #endregion

        #region Private-Members

        private Func<AppSettings, object?> _Getter = settings => null;
        private Action<AppSettings, JsonElement> _Setter = (settings, value) => { };

        #endregion

        #region Constructors-and-Factories

        private SettingFieldDefinition(string key, string section, string label, string description, SettingTypeEnum type, SettingApplyModeEnum applyMode, string? environmentVariable)
        {
            if (String.IsNullOrWhiteSpace(key)) throw new ArgumentNullException(nameof(key));
            Key = key;
            Section = section ?? String.Empty;
            Label = label ?? String.Empty;
            Description = description ?? String.Empty;
            Type = type;
            ApplyMode = applyMode;
            EnvironmentVariable = environmentVariable;
        }

        /// <summary>
        /// Define a boolean setting.
        /// </summary>
        /// <param name="key">Key.</param>
        /// <param name="section">Section.</param>
        /// <param name="label">Label.</param>
        /// <param name="description">Description.</param>
        /// <param name="applyMode">Apply mode.</param>
        /// <param name="environmentVariable">Overriding environment variable, or null.</param>
        /// <param name="getter">Reads the value.</param>
        /// <param name="setter">Writes the value.</param>
        /// <returns>Definition.</returns>
        public static SettingFieldDefinition Boolean(string key, string section, string label, string description, SettingApplyModeEnum applyMode, string? environmentVariable, Func<AppSettings, bool> getter, Action<AppSettings, bool> setter)
        {
            SettingFieldDefinition def = new SettingFieldDefinition(key, section, label, description, SettingTypeEnum.Boolean, applyMode, environmentVariable);
            def._Getter = settings => getter(settings);
            def._Setter = (settings, value) =>
            {
                if (value.ValueKind != JsonValueKind.True && value.ValueKind != JsonValueKind.False)
                    throw new ArgumentException("Must be true or false.");
                setter(settings, value.GetBoolean());
            };
            return def;
        }

        /// <summary>
        /// Define an integer setting. Out-of-range values are rejected.
        /// </summary>
        /// <param name="key">Key.</param>
        /// <param name="section">Section.</param>
        /// <param name="label">Label.</param>
        /// <param name="description">Description.</param>
        /// <param name="applyMode">Apply mode.</param>
        /// <param name="environmentVariable">Overriding environment variable, or null.</param>
        /// <param name="min">Minimum.</param>
        /// <param name="max">Maximum.</param>
        /// <param name="getter">Reads the value.</param>
        /// <param name="setter">Writes the value.</param>
        /// <returns>Definition.</returns>
        public static SettingFieldDefinition Integer(string key, string section, string label, string description, SettingApplyModeEnum applyMode, string? environmentVariable, int min, int max, Func<AppSettings, int> getter, Action<AppSettings, int> setter)
        {
            SettingFieldDefinition def = new SettingFieldDefinition(key, section, label, description, SettingTypeEnum.Integer, applyMode, environmentVariable);
            def.Min = min;
            def.Max = max;
            def._Getter = settings => getter(settings);
            def._Setter = (settings, value) =>
            {
                if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out int number))
                    throw new ArgumentException("Must be a whole number.");
                if (number < min || number > max)
                    throw new ArgumentException("Must be between " + min.ToString(CultureInfo.InvariantCulture) + " and " + max.ToString(CultureInfo.InvariantCulture) + ".");
                setter(settings, number);
            };
            return def;
        }

        /// <summary>
        /// Define a decimal setting (double, invariant culture) with inclusive bounds.
        /// </summary>
        /// <param name="key">Dotted key.</param>
        /// <param name="section">Console section.</param>
        /// <param name="label">Console label.</param>
        /// <param name="description">Description.</param>
        /// <param name="applyMode">Live or Restart.</param>
        /// <param name="environmentVariable">Overriding environment variable, or null.</param>
        /// <param name="min">Minimum (inclusive).</param>
        /// <param name="max">Maximum (inclusive).</param>
        /// <param name="getter">Reads the value.</param>
        /// <param name="setter">Writes the value.</param>
        /// <returns>Definition.</returns>
        public static SettingFieldDefinition Decimal(string key, string section, string label, string description, SettingApplyModeEnum applyMode, string? environmentVariable, double min, double max, Func<AppSettings, double> getter, Action<AppSettings, double> setter)
        {
            SettingFieldDefinition def = new SettingFieldDefinition(key, section, label, description, SettingTypeEnum.Decimal, applyMode, environmentVariable);
            def.MinDecimal = min;
            def.MaxDecimal = max;
            def._Getter = settings => getter(settings);
            def._Setter = (settings, value) =>
            {
                double number;
                if (value.ValueKind == JsonValueKind.Number) number = value.GetDouble();
                else if (value.ValueKind != JsonValueKind.String || !Double.TryParse(value.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out number))
                    throw new ArgumentException("Must be a number.");
                if (Double.IsNaN(number) || Double.IsInfinity(number) || number < min || number > max)
                    throw new ArgumentException("Must be between " + min.ToString(CultureInfo.InvariantCulture) + " and " + max.ToString(CultureInfo.InvariantCulture) + ".");
                setter(settings, number);
            };
            return def;
        }

        /// <summary>
        /// Define a text setting.
        /// </summary>
        /// <param name="key">Dotted key.</param>
        /// <param name="section">Console section.</param>
        /// <param name="label">Console label.</param>
        /// <param name="description">Description.</param>
        /// <param name="applyMode">Live or Restart.</param>
        /// <param name="environmentVariable">Overriding environment variable, or null.</param>
        /// <param name="getter">Reads the value.</param>
        /// <param name="setter">Writes the value.</param>
        /// <returns>Definition.</returns>
        public static SettingFieldDefinition Text(string key, string section, string label, string description, SettingApplyModeEnum applyMode, string? environmentVariable, Func<AppSettings, string> getter, Action<AppSettings, string> setter)
        {
            SettingFieldDefinition def = new SettingFieldDefinition(key, section, label, description, SettingTypeEnum.String, applyMode, environmentVariable);
            def._Getter = settings => getter(settings);
            def._Setter = (settings, value) =>
            {
                if (value.ValueKind != JsonValueKind.String) throw new ArgumentException("Must be text.");
                setter(settings, value.GetString() ?? String.Empty);
            };
            return def;
        }

        /// <summary>
        /// Define a secret setting. Its value is never returned; only whether it is set.
        /// </summary>
        /// <param name="key">Key.</param>
        /// <param name="section">Section.</param>
        /// <param name="label">Label.</param>
        /// <param name="description">Description.</param>
        /// <param name="applyMode">Apply mode.</param>
        /// <param name="environmentVariable">Overriding environment variable, or null.</param>
        /// <param name="getter">Reads the value.</param>
        /// <param name="setter">Writes the value; may throw ArgumentException for invalid input.</param>
        /// <returns>Definition.</returns>
        public static SettingFieldDefinition Secret(string key, string section, string label, string description, SettingApplyModeEnum applyMode, string? environmentVariable, Func<AppSettings, string> getter, Action<AppSettings, string> setter)
        {
            SettingFieldDefinition def = Text(key, section, label, description, applyMode, environmentVariable, getter, setter);
            return new SettingFieldDefinition(key, section, label, description, SettingTypeEnum.Secret, applyMode, environmentVariable)
            {
                _Getter = def._Getter,
                _Setter = def._Setter
            };
        }

        /// <summary>
        /// Define an enum setting. Values are enum member names.
        /// </summary>
        /// <typeparam name="T">Enum type.</typeparam>
        /// <param name="key">Key.</param>
        /// <param name="section">Section.</param>
        /// <param name="label">Label.</param>
        /// <param name="description">Description.</param>
        /// <param name="applyMode">Apply mode.</param>
        /// <param name="environmentVariable">Overriding environment variable, or null.</param>
        /// <param name="getter">Reads the value.</param>
        /// <param name="setter">Writes the value.</param>
        /// <returns>Definition.</returns>
        public static SettingFieldDefinition Choice<T>(string key, string section, string label, string description, SettingApplyModeEnum applyMode, string? environmentVariable, Func<AppSettings, T> getter, Action<AppSettings, T> setter) where T : struct, System.Enum
        {
            SettingFieldDefinition def = new SettingFieldDefinition(key, section, label, description, SettingTypeEnum.Enum, applyMode, environmentVariable);
            def.Options = System.Enum.GetNames<T>().ToList();
            def._Getter = settings => getter(settings).ToString();
            def._Setter = (settings, value) =>
            {
                string? text = value.ValueKind == JsonValueKind.String ? value.GetString() : null;
                if (text == null || !System.Enum.TryParse<T>(text, true, out T parsed) || !System.Enum.IsDefined(parsed))
                    throw new ArgumentException("Must be one of: " + String.Join(", ", System.Enum.GetNames<T>()) + ".");
                setter(settings, parsed);
            };
            return def;
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Read the current value. Callers must not expose the value of Secret fields.
        /// </summary>
        /// <param name="settings">Settings.</param>
        /// <returns>Value.</returns>
        public object? GetValue(AppSettings settings)
        {
            ArgumentNullException.ThrowIfNull(settings);
            return _Getter(settings);
        }

        /// <summary>
        /// Validate and apply a value.
        /// </summary>
        /// <param name="settings">Settings to modify.</param>
        /// <param name="value">Value.</param>
        /// <exception cref="ArgumentNullException">Settings are null.</exception>
        /// <exception cref="ArgumentException">The value is invalid; the message is suitable for display.</exception>
        public void SetValue(AppSettings settings, JsonElement value)
        {
            ArgumentNullException.ThrowIfNull(settings);
            _Setter(settings, value);
        }

        /// <summary>
        /// Convert a raw environment variable value into a JSON value of this field's type.
        /// </summary>
        /// <param name="raw">Raw text.</param>
        /// <returns>JSON value.</returns>
        /// <exception cref="ArgumentException">The text cannot be converted.</exception>
        public JsonElement ParseEnvironmentValue(string raw)
        {
            string text = (raw ?? String.Empty).Trim();
            switch (Type)
            {
                case SettingTypeEnum.Boolean:
                    if (text == "1" || String.Equals(text, "true", StringComparison.OrdinalIgnoreCase)) return JsonSerializer.SerializeToElement(true);
                    if (text == "0" || String.Equals(text, "false", StringComparison.OrdinalIgnoreCase)) return JsonSerializer.SerializeToElement(false);
                    throw new ArgumentException("Must be true or false.");
                case SettingTypeEnum.Integer:
                    if (Int32.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int number)) return JsonSerializer.SerializeToElement(number);
                    throw new ArgumentException("Must be a whole number.");
                case SettingTypeEnum.Decimal:
                    if (Double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double value)) return JsonSerializer.SerializeToElement(value);
                    throw new ArgumentException("Must be a number.");
                default:
                    return JsonSerializer.SerializeToElement(text);
            }
        }

        #endregion
    }
}
