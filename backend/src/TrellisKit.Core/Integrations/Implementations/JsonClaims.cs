namespace TrellisKit.Core.Integrations.Implementations
{
    using System;
    using System.Globalization;
    using System.Text.Json;

    /// <summary>
    /// Lenient readers for JSON properties whose type varies between providers (for example booleans sent as strings).
    /// Thread safe.
    /// </summary>
    internal static class JsonClaims
    {
        /// <summary>
        /// Read a property as a string. Numbers are returned as their raw text.
        /// </summary>
        /// <param name="element">Object.</param>
        /// <param name="name">Property name.</param>
        /// <returns>Value, or null when absent, null, or not a string or number.</returns>
        internal static string? GetString(JsonElement element, string name)
        {
            if (!TryGetProperty(element, name, out JsonElement value)) return null;
            if (value.ValueKind == JsonValueKind.String) return value.GetString();
            if (value.ValueKind == JsonValueKind.Number) return value.GetRawText();
            return null;
        }

        /// <summary>
        /// Read a property as a boolean. Accepts JSON booleans and the strings "true" and "false".
        /// </summary>
        /// <param name="element">Object.</param>
        /// <param name="name">Property name.</param>
        /// <returns>Value, or null when absent or not boolean-like.</returns>
        internal static bool? GetBoolean(JsonElement element, string name)
        {
            if (!TryGetProperty(element, name, out JsonElement value)) return null;
            if (value.ValueKind == JsonValueKind.True) return true;
            if (value.ValueKind == JsonValueKind.False) return false;
            if (value.ValueKind == JsonValueKind.String)
            {
                string? text = value.GetString();
                if (String.Equals(text, "true", StringComparison.OrdinalIgnoreCase)) return true;
                if (String.Equals(text, "false", StringComparison.OrdinalIgnoreCase)) return false;
            }
            return null;
        }

        /// <summary>
        /// Read a property as a 64-bit integer. Accepts JSON numbers and numeric strings.
        /// </summary>
        /// <param name="element">Object.</param>
        /// <param name="name">Property name.</param>
        /// <returns>Value, or null when absent or not an integer.</returns>
        internal static long? GetInt64(JsonElement element, string name)
        {
            if (!TryGetProperty(element, name, out JsonElement value)) return null;
            if (value.ValueKind == JsonValueKind.Number)
            {
                if (value.TryGetInt64(out long whole)) return whole;
                if (value.TryGetDouble(out double fractional)) return (long)Math.Floor(fractional);
                return null;
            }
            if (value.ValueKind == JsonValueKind.String
                && Int64.TryParse(value.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out long parsed))
            {
                return parsed;
            }
            return null;
        }

        /// <summary>
        /// Read a property holding Unix epoch milliseconds as a UTC time.
        /// </summary>
        /// <param name="element">Object.</param>
        /// <param name="name">Property name.</param>
        /// <returns>UTC time, or null when absent or out of range.</returns>
        internal static DateTime? GetUnixMilliseconds(JsonElement element, string name)
        {
            long? millis = GetInt64(element, name);
            if (millis == null) return null;
            try
            {
                return DateTimeOffset.FromUnixTimeMilliseconds(millis.Value).UtcDateTime;
            }
            catch (ArgumentOutOfRangeException)
            {
                return null;
            }
        }

        /// <summary>
        /// Read a property holding Unix epoch seconds as a UTC time.
        /// </summary>
        /// <param name="element">Object.</param>
        /// <param name="name">Property name.</param>
        /// <returns>UTC time, or null when absent or out of range.</returns>
        internal static DateTime? GetUnixSeconds(JsonElement element, string name)
        {
            long? seconds = GetInt64(element, name);
            if (seconds == null) return null;
            try
            {
                return DateTimeOffset.FromUnixTimeSeconds(seconds.Value).UtcDateTime;
            }
            catch (ArgumentOutOfRangeException)
            {
                return null;
            }
        }

        /// <summary>
        /// Read a property holding an RFC 3339 timestamp as a UTC time.
        /// </summary>
        /// <param name="element">Object.</param>
        /// <param name="name">Property name.</param>
        /// <returns>UTC time, or null when absent or unparseable.</returns>
        internal static DateTime? GetRfc3339(JsonElement element, string name)
        {
            string? text = GetString(element, name);
            if (String.IsNullOrEmpty(text)) return null;
            if (DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out DateTimeOffset parsed))
            {
                return parsed.UtcDateTime;
            }
            return null;
        }

        /// <summary>
        /// Read a property that is a JSON object.
        /// </summary>
        /// <param name="element">Object.</param>
        /// <param name="name">Property name.</param>
        /// <param name="value">The nested object when found.</param>
        /// <returns>True when the property exists and is an object.</returns>
        internal static bool TryGetObject(JsonElement element, string name, out JsonElement value)
        {
            if (TryGetProperty(element, name, out value) && value.ValueKind == JsonValueKind.Object) return true;
            value = default;
            return false;
        }

        /// <summary>
        /// Read a property that is a JSON array.
        /// </summary>
        /// <param name="element">Object.</param>
        /// <param name="name">Property name.</param>
        /// <param name="value">The array when found.</param>
        /// <returns>True when the property exists and is an array.</returns>
        internal static bool TryGetArray(JsonElement element, string name, out JsonElement value)
        {
            if (TryGetProperty(element, name, out value) && value.ValueKind == JsonValueKind.Array) return true;
            value = default;
            return false;
        }

        /// <summary>
        /// Read a property that is either an identifier string or an expanded object with an id (Stripe style).
        /// </summary>
        /// <param name="element">Object.</param>
        /// <param name="name">Property name.</param>
        /// <returns>Identifier, or null.</returns>
        internal static string? GetIdOrExpandedId(JsonElement element, string name)
        {
            if (!TryGetProperty(element, name, out JsonElement value)) return null;
            if (value.ValueKind == JsonValueKind.String) return value.GetString();
            if (value.ValueKind == JsonValueKind.Object) return GetString(value, "id");
            return null;
        }

        private static bool TryGetProperty(JsonElement element, string name, out JsonElement value)
        {
            if (element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out value) && value.ValueKind != JsonValueKind.Null && value.ValueKind != JsonValueKind.Undefined)
            {
                return true;
            }
            value = default;
            return false;
        }
    }
}
