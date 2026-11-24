namespace TrellisKit.Server.Services.Ai
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text.Json;

    /// <summary>
    /// Validates tool arguments from the model against the tool's JSON schema (docs/AI_CONTRACT.md section 12.4).
    /// Supports the subset the engine tools use: type (one or a list), properties, required, additionalProperties
    /// false, enum, minimum and maximum, minLength and maxLength, items, and maxItems. Nesting is bounded; arrays are
    /// capped at 50 items unless the schema allows fewer. Thread safety: stateless.
    /// </summary>
    public static class ToolArgumentValidator
    {
        #region Private-Members

        private const int _MaxDepth = 8;
        private const int _DefaultMaxItems = 50;

        #endregion

        #region Public-Methods

        /// <summary>
        /// Validate arguments against a schema.
        /// </summary>
        /// <param name="schema">Schema (an object).</param>
        /// <param name="arguments">Arguments (an object).</param>
        /// <returns>Null when valid, otherwise a short message naming the first problem.</returns>
        public static string? Validate(JsonElement schema, JsonElement arguments)
        {
            if (schema.ValueKind != JsonValueKind.Object) return null;
            return Check(schema, arguments, "arguments", 0);
        }

        #endregion

        #region Private-Methods

        private static string? Check(JsonElement schema, JsonElement value, string path, int depth)
        {
            if (depth > _MaxDepth) return path + " is nested too deeply.";
            if (schema.ValueKind != JsonValueKind.Object) return null;
            if (schema.TryGetProperty("type", out JsonElement type))
            {
                List<string> types = new List<string>();
                if (type.ValueKind == JsonValueKind.String) types.Add(type.GetString() ?? String.Empty);
                else if (type.ValueKind == JsonValueKind.Array) types.AddRange(type.EnumerateArray().Where(t => t.ValueKind == JsonValueKind.String).Select(t => t.GetString() ?? String.Empty));
                if (types.Count > 0 && !types.Any(t => Matches(t, value))) return path + " must be " + String.Join(" or ", types) + ".";
            }
            if (schema.TryGetProperty("enum", out JsonElement options) && options.ValueKind == JsonValueKind.Array)
            {
                string raw = value.GetRawText();
                if (!options.EnumerateArray().Any(o => o.GetRawText() == raw)) return path + " must be one of " + String.Join(", ", options.EnumerateArray().Select(o => o.GetRawText())) + ".";
            }
            switch (value.ValueKind)
            {
                case JsonValueKind.Object:
                    return CheckObject(schema, value, path, depth);
                case JsonValueKind.Array:
                    int max = schema.TryGetProperty("maxItems", out JsonElement m) && m.TryGetInt32(out int mi) ? Math.Min(mi, _DefaultMaxItems) : _DefaultMaxItems;
                    if (value.GetArrayLength() > max) return path + " has more than " + max + " items.";
                    if (schema.TryGetProperty("items", out JsonElement items))
                    {
                        int i = 0;
                        foreach (JsonElement item in value.EnumerateArray())
                        {
                            string? problem = Check(items, item, path + "[" + i + "]", depth + 1);
                            if (problem != null) return problem;
                            i++;
                        }
                    }
                    return null;
                case JsonValueKind.Number:
                    double number = value.GetDouble();
                    if (schema.TryGetProperty("minimum", out JsonElement min) && min.ValueKind == JsonValueKind.Number && number < min.GetDouble()) return path + " must be at least " + min.GetRawText() + ".";
                    if (schema.TryGetProperty("maximum", out JsonElement mx) && mx.ValueKind == JsonValueKind.Number && number > mx.GetDouble()) return path + " must be at most " + mx.GetRawText() + ".";
                    return null;
                case JsonValueKind.String:
                    int length = (value.GetString() ?? String.Empty).Length;
                    if (schema.TryGetProperty("minLength", out JsonElement minLength) && minLength.TryGetInt32(out int least) && length < least) return path + " must be at least " + least + " characters.";
                    if (schema.TryGetProperty("maxLength", out JsonElement maxLength) && maxLength.TryGetInt32(out int most) && length > most) return path + " must be at most " + most + " characters.";
                    return null;
                default:
                    return null;
            }
        }

        private static string? CheckObject(JsonElement schema, JsonElement value, string path, int depth)
        {
            bool hasProperties = schema.TryGetProperty("properties", out JsonElement properties) && properties.ValueKind == JsonValueKind.Object;
            if (schema.TryGetProperty("required", out JsonElement required) && required.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement name in required.EnumerateArray())
                {
                    string key = name.GetString() ?? String.Empty;
                    if (key.Length > 0 && (!value.TryGetProperty(key, out JsonElement present) || present.ValueKind == JsonValueKind.Null)) return path + "." + key + " is required.";
                }
            }
            bool closed = schema.TryGetProperty("additionalProperties", out JsonElement additional) && additional.ValueKind == JsonValueKind.False;
            foreach (JsonProperty property in value.EnumerateObject())
            {
                if (hasProperties && properties.TryGetProperty(property.Name, out JsonElement propertySchema))
                {
                    // A null optional argument is treated as absent (models often send null for unused fields).
                    if (property.Value.ValueKind == JsonValueKind.Null) continue;
                    string? problem = Check(propertySchema, property.Value, path + "." + property.Name, depth + 1);
                    if (problem != null) return problem;
                }
                else if (closed)
                {
                    return path + "." + property.Name + " is not a known argument.";
                }
            }
            return null;
        }

        private static bool Matches(string type, JsonElement value)
        {
            switch (type)
            {
                case "object": return value.ValueKind == JsonValueKind.Object;
                case "array": return value.ValueKind == JsonValueKind.Array;
                case "string": return value.ValueKind == JsonValueKind.String;
                case "boolean": return value.ValueKind == JsonValueKind.True || value.ValueKind == JsonValueKind.False;
                case "null": return value.ValueKind == JsonValueKind.Null;
                case "number": return value.ValueKind == JsonValueKind.Number;
                case "integer": return value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out long _);
                default: return true;
            }
        }

        #endregion
    }
}
