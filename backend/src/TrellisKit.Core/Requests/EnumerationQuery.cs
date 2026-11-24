namespace TrellisKit.Core.Requests
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Text.Json;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// Input of every list route (docs/ENUMERATION_AND_METADATA.md section 2.2): the POST .../enumerate body, or the GET
    /// form's query parameters. Validated and clamped by the enumeration normalizer.
    /// </summary>
    public class EnumerationQuery
    {
        #region Public-Members

        /// <summary>
        /// Requested page size, or null for the default (25). Values above the route maximum are clamped.
        /// </summary>
        public int? MaxResults { get; set; } = null;

        /// <summary>
        /// Opaque token from a previous result, or null for the first page. When present its ordering and position win.
        /// </summary>
        public string? ContinuationToken { get; set; } = null;

        /// <summary>
        /// Records to skip (admin routes; 0 to 100000). Ignored when a continuation token is present.
        /// </summary>
        public int Skip { get; set; } = 0;

        /// <summary>
        /// Ordering name, for example CreatedDescending (the default), or null.
        /// </summary>
        public string? Ordering { get; set; } = null;

        /// <summary>
        /// Free text over the collection's searchable fields (1 to 200 characters), or null.
        /// </summary>
        public string? Search { get; set; } = null;

        /// <summary>
        /// Only objects created at or after this time, or null.
        /// </summary>
        public DateTime? CreatedAfterUtc { get; set; } = null;

        /// <summary>
        /// Only objects created before this time, or null.
        /// </summary>
        public DateTime? CreatedBeforeUtc { get; set; } = null;

        /// <summary>
        /// Labels objects must carry (at most 20). Never null.
        /// </summary>
        public List<string> Labels
        {
            get { return _Labels; }
            set { _Labels = value ?? new List<string>(); }
        }

        /// <summary>
        /// How several labels combine. Default All.
        /// </summary>
        public LabelMatchEnum LabelMatch { get; set; } = LabelMatchEnum.All;

        /// <summary>
        /// Tags objects must carry (key compared case-insensitively, value exactly; "*" means any value). At most 20. Never null.
        /// </summary>
        public Dictionary<string, string> Tags
        {
            get { return _Tags; }
            set { _Tags = value ?? new Dictionary<string, string>(); }
        }

        /// <summary>
        /// Collection-specific filters; unknown keys are rejected. Never null.
        /// </summary>
        public Dictionary<string, JsonElement> Filters
        {
            get { return _Filters; }
            set { _Filters = value ?? new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase); }
        }

        #endregion

        #region Private-Members

        private List<string> _Labels = new List<string>();
        private Dictionary<string, string> _Tags = new Dictionary<string, string>();
        private Dictionary<string, JsonElement> _Filters = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);

        #endregion

        #region Public-Methods

        /// <summary>
        /// Set a filter from a query-string value (stored as a JSON string).
        /// </summary>
        /// <param name="name">Filter name.</param>
        /// <param name="value">Raw value.</param>
        public void SetFilter(string name, string value)
        {
            if (String.IsNullOrEmpty(name)) return;
            using (JsonDocument doc = JsonDocument.Parse(JsonSerializer.Serialize(value ?? String.Empty)))
            {
                Filters[name] = doc.RootElement.Clone();
            }
        }

        /// <summary>
        /// Whether a filter is present (and not JSON null).
        /// </summary>
        /// <param name="name">Filter name.</param>
        /// <returns>True when present.</returns>
        public bool HasFilter(string name)
        {
            return FindFilter(name, out JsonElement value) && value.ValueKind != JsonValueKind.Null && value.ValueKind != JsonValueKind.Undefined;
        }

        /// <summary>
        /// A filter as text (strings as-is, numbers and booleans as their JSON text), or null when absent.
        /// </summary>
        /// <param name="name">Filter name.</param>
        /// <returns>Text, or null.</returns>
        public string? FilterString(string name)
        {
            if (!FindFilter(name, out JsonElement value)) return null;
            switch (value.ValueKind)
            {
                case JsonValueKind.String:
                    string? text = value.GetString();
                    return String.IsNullOrWhiteSpace(text) ? null : text.Trim();
                case JsonValueKind.Number:
                case JsonValueKind.True:
                case JsonValueKind.False:
                    return value.GetRawText();
                default:
                    return null;
            }
        }

        /// <summary>
        /// A boolean filter (JSON boolean, or the strings true and false).
        /// </summary>
        /// <param name="name">Filter name.</param>
        /// <param name="errors">Field errors (filters.name on a malformed value).</param>
        /// <returns>Value, or null when absent or malformed.</returns>
        public bool? FilterBool(string name, Dictionary<string, string> errors)
        {
            string? raw = FilterString(name);
            if (raw == null) return null;
            if (Boolean.TryParse(raw, out bool value)) return value;
            errors?.TryAdd("filters." + name, "Must be true or false.");
            return null;
        }

        /// <summary>
        /// An integer filter.
        /// </summary>
        /// <param name="name">Filter name.</param>
        /// <param name="errors">Field errors.</param>
        /// <returns>Value, or null when absent or malformed.</returns>
        public long? FilterLong(string name, Dictionary<string, string> errors)
        {
            string? raw = FilterString(name);
            if (raw == null) return null;
            if (Int64.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out long value)) return value;
            errors?.TryAdd("filters." + name, "Must be a whole number.");
            return null;
        }

        /// <summary>
        /// A UTC timestamp filter (ISO 8601).
        /// </summary>
        /// <param name="name">Filter name.</param>
        /// <param name="errors">Field errors.</param>
        /// <returns>Value, or null when absent or malformed.</returns>
        public DateTime? FilterUtc(string name, Dictionary<string, string> errors)
        {
            string? raw = FilterString(name);
            if (raw == null) return null;
            if (DateTime.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out DateTime value)) return DateTime.SpecifyKind(value, DateTimeKind.Utc);
            errors?.TryAdd("filters." + name, "Must be an ISO 8601 timestamp.");
            return null;
        }

        /// <summary>
        /// An enum filter (member name, case-insensitive).
        /// </summary>
        /// <typeparam name="T">Enum type.</typeparam>
        /// <param name="name">Filter name.</param>
        /// <param name="errors">Field errors.</param>
        /// <returns>Value, or null when absent or unrecognized.</returns>
        public T? FilterEnum<T>(string name, Dictionary<string, string> errors) where T : struct, Enum
        {
            string? raw = FilterString(name);
            if (raw == null) return null;
            if (!Int32.TryParse(raw, out int _) && Enum.TryParse(raw, true, out T value) && Enum.IsDefined(value)) return value;
            errors?.TryAdd("filters." + name, "Use one of " + String.Join(", ", Enum.GetNames<T>()) + ".");
            return null;
        }

        #endregion

        #region Private-Methods

        private bool FindFilter(string name, out JsonElement value)
        {
            value = default;
            foreach (KeyValuePair<string, JsonElement> pair in Filters)
            {
                if (String.Equals(pair.Key, name, StringComparison.OrdinalIgnoreCase))
                {
                    value = pair.Value;
                    return true;
                }
            }
            return false;
        }

        #endregion
    }
}
