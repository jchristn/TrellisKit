namespace TrellisKit.Server.Services
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text.Json;
    using TrellisKit.Core.Requests;

    /// <summary>
    /// Filter helpers for the forum enumerations: list filters accept a JSON array or a comma-separated string, and
    /// boolean filters accept JSON booleans or the strings "true" and "false" (Phase 3 client assumption 9).
    /// </summary>
    public static class ForumFilters
    {
        #region Public-Methods

        /// <summary>
        /// Read a list filter (JSON array of strings, or a comma-separated string), or null when absent.
        /// </summary>
        /// <param name="query">Query.</param>
        /// <param name="name">Filter name.</param>
        /// <returns>Values, or null.</returns>
        public static List<string>? List(EnumerationQuery query, string name)
        {
            ArgumentNullException.ThrowIfNull(query);
            foreach (KeyValuePair<string, JsonElement> pair in query.Filters)
            {
                if (!String.Equals(pair.Key, name, StringComparison.OrdinalIgnoreCase)) continue;
                JsonElement value = pair.Value;
                if (value.ValueKind == JsonValueKind.Array)
                {
                    return value.EnumerateArray()
                        .Select(e => e.ValueKind == JsonValueKind.String ? e.GetString() : e.ValueKind == JsonValueKind.Null ? null : e.GetRawText())
                        .Where(s => !String.IsNullOrWhiteSpace(s)).Select(s => s!.Trim()).Distinct(StringComparer.Ordinal).ToList();
                }
                if (value.ValueKind == JsonValueKind.String)
                {
                    return (value.GetString() ?? String.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Distinct(StringComparer.Ordinal).ToList();
                }
                return null;
            }
            return null;
        }

        /// <summary>
        /// Read a list filter of enum values, recording a field error for unknown values.
        /// </summary>
        /// <typeparam name="T">Enum type.</typeparam>
        /// <param name="query">Query.</param>
        /// <param name="name">Filter name.</param>
        /// <param name="errors">Field errors.</param>
        /// <returns>Values (empty when absent).</returns>
        public static List<T> Enums<T>(EnumerationQuery query, string name, Dictionary<string, string> errors) where T : struct, Enum
        {
            ArgumentNullException.ThrowIfNull(errors);
            List<T> result = new List<T>();
            foreach (string raw in List(query, name) ?? new List<string>())
            {
                if (ModerationService.TryParseEnum(raw, out T value))
                {
                    if (!result.Contains(value)) result.Add(value);
                }
                else errors.TryAdd("filters." + name, "Use one or more of " + String.Join(", ", Enum.GetNames<T>()) + ".");
            }
            return result;
        }

        #endregion
    }
}
