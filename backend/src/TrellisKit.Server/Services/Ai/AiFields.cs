namespace TrellisKit.Server.Services.Ai
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Text.Json;

    /// <summary>
    /// Reads fields of the nested JSON objects in AI request bodies (parameters, limits, knowledge, and similar) with
    /// presence semantics: a field that is absent is not changed, a field set to null resets it, and a field of the
    /// wrong type or out of range adds a field error under its path.
    /// </summary>
    public static class AiFields
    {
        #region Public-Methods

        /// <summary>
        /// Whether an object has a field (case-insensitive name match).
        /// </summary>
        /// <param name="values">Object, or null.</param>
        /// <param name="name">Field name.</param>
        /// <param name="value">Value when present.</param>
        /// <returns>True when present (including null).</returns>
        public static bool Has(Dictionary<string, JsonElement>? values, string name, out JsonElement value)
        {
            value = default;
            if (values == null) return false;
            foreach (KeyValuePair<string, JsonElement> pair in values)
            {
                if (!String.Equals(pair.Key, name, StringComparison.OrdinalIgnoreCase)) continue;
                value = pair.Value;
                return true;
            }
            return false;
        }

        /// <summary>
        /// Whether a present field is JSON null.
        /// </summary>
        /// <param name="value">Value.</param>
        /// <returns>True when null.</returns>
        public static bool IsNull(JsonElement value)
        {
            return value.ValueKind == JsonValueKind.Null || value.ValueKind == JsonValueKind.Undefined;
        }

        /// <summary>
        /// Read a nullable number within a range.
        /// </summary>
        /// <param name="value">Value.</param>
        /// <param name="path">Field path for errors.</param>
        /// <param name="min">Minimum.</param>
        /// <param name="max">Maximum.</param>
        /// <param name="errors">Field errors.</param>
        /// <returns>Value, or null (null JSON or invalid).</returns>
        public static double? Double(JsonElement value, string path, double min, double max, Dictionary<string, string> errors)
        {
            if (IsNull(value)) return null;
            if (value.ValueKind != JsonValueKind.Number || !value.TryGetDouble(out double number) || number < min || number > max)
            {
                errors[path] = "Must be a number from " + min.ToString(CultureInfo.InvariantCulture) + " to " + max.ToString(CultureInfo.InvariantCulture) + ", or null.";
                return null;
            }
            return number;
        }

        /// <summary>
        /// Read a nullable whole number within a range.
        /// </summary>
        /// <param name="value">Value.</param>
        /// <param name="path">Field path for errors.</param>
        /// <param name="min">Minimum.</param>
        /// <param name="max">Maximum.</param>
        /// <param name="errors">Field errors.</param>
        /// <returns>Value, or null (null JSON or invalid).</returns>
        public static int? Int(JsonElement value, string path, int min, int max, Dictionary<string, string> errors)
        {
            if (IsNull(value)) return null;
            if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out int number) || number < min || number > max)
            {
                errors[path] = "Must be a whole number from " + min.ToString(CultureInfo.InvariantCulture) + " to " + max.ToString(CultureInfo.InvariantCulture) + ".";
                return null;
            }
            return number;
        }

        /// <summary>
        /// Read a nullable boolean.
        /// </summary>
        /// <param name="value">Value.</param>
        /// <param name="path">Field path for errors.</param>
        /// <param name="errors">Field errors.</param>
        /// <returns>Value, or null.</returns>
        public static bool? Bool(JsonElement value, string path, Dictionary<string, string> errors)
        {
            if (IsNull(value)) return null;
            if (value.ValueKind == JsonValueKind.True) return true;
            if (value.ValueKind == JsonValueKind.False) return false;
            errors[path] = "Must be true or false.";
            return null;
        }

        /// <summary>
        /// Read a nullable string.
        /// </summary>
        /// <param name="value">Value.</param>
        /// <param name="path">Field path for errors.</param>
        /// <param name="maxLength">Longest value.</param>
        /// <param name="errors">Field errors.</param>
        /// <returns>Trimmed value, or null.</returns>
        public static string? Str(JsonElement value, string path, int maxLength, Dictionary<string, string> errors)
        {
            if (IsNull(value)) return null;
            if (value.ValueKind != JsonValueKind.String)
            {
                errors[path] = "Must be a string.";
                return null;
            }
            string text = (value.GetString() ?? String.Empty).Trim();
            if (text.Length > maxLength)
            {
                errors[path] = "Must be at most " + maxLength + " characters.";
                return null;
            }
            return text;
        }

        /// <summary>
        /// Read a nullable enum by name (case-insensitive).
        /// </summary>
        /// <typeparam name="T">Enum type.</typeparam>
        /// <param name="value">Value.</param>
        /// <param name="path">Field path for errors.</param>
        /// <param name="errors">Field errors.</param>
        /// <returns>Value, or null.</returns>
        public static T? Enum<T>(JsonElement value, string path, Dictionary<string, string> errors) where T : struct, System.Enum
        {
            if (IsNull(value)) return null;
            if (value.ValueKind == JsonValueKind.String && System.Enum.TryParse(value.GetString(), true, out T parsed) && System.Enum.IsDefined(parsed)) return parsed;
            errors[path] = "Must be one of: " + String.Join(", ", System.Enum.GetNames<T>()) + ".";
            return null;
        }

        /// <summary>
        /// Read a list of strings (at most maxItems).
        /// </summary>
        /// <param name="value">Value.</param>
        /// <param name="path">Field path for errors.</param>
        /// <param name="maxItems">Most items.</param>
        /// <param name="errors">Field errors.</param>
        /// <returns>Trimmed non-empty strings, or an empty list for null.</returns>
        public static List<string> Strings(JsonElement value, string path, int maxItems, Dictionary<string, string> errors)
        {
            List<string> items = new List<string>();
            if (IsNull(value)) return items;
            if (value.ValueKind != JsonValueKind.Array)
            {
                errors[path] = "Must be a list of strings.";
                return items;
            }
            foreach (JsonElement item in value.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.String)
                {
                    errors[path] = "Must be a list of strings.";
                    return new List<string>();
                }
                string text = (item.GetString() ?? String.Empty).Trim();
                if (text.Length > 0) items.Add(text);
            }
            if (items.Count > maxItems) errors[path] = "At most " + maxItems + " items.";
            return items;
        }

        #endregion
    }
}
