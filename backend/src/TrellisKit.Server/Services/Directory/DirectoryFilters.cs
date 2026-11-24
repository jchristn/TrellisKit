namespace TrellisKit.Server.Services
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;
    using System.Text.Json;
    using TrellisKit.Core.Exceptions;
    using TrellisKit.Core.Models;
    using TrellisKit.Core.Requests;
    using TrellisKit.Core.Settings;

    /// <summary>
    /// Parsing of the directory enumeration filters (docs/PHASE4_CONTRACT.md section 3.2): lists (JSON arrays or comma
    /// separated text), near { lat, lng, radiusKm } (or "lat,lng[,radiusKm]" in GET form), and bbox { south, west, north,
    /// east } (or "south,west,north,east"). Geo problems answer 400 InvalidGeo. Thread safe (stateless).
    /// </summary>
    public static class DirectoryFilters
    {
        #region Public-Methods

        /// <summary>
        /// A list filter.
        /// </summary>
        /// <param name="query">Query.</param>
        /// <param name="name">Filter name.</param>
        /// <returns>Values (trimmed, distinct, at most 50).</returns>
        /// <exception cref="ArgumentNullException">The query is null.</exception>
        public static List<string> Strings(EnumerationQuery query, string name)
        {
            ArgumentNullException.ThrowIfNull(query);
            if (!Find(query, name, out JsonElement value)) return new List<string>();
            List<string> result = new List<string>();
            if (value.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement item in value.EnumerateArray())
                    if (item.ValueKind == JsonValueKind.String && !String.IsNullOrWhiteSpace(item.GetString())) result.Add(item.GetString()!.Trim());
                    else if (item.ValueKind == JsonValueKind.Number) result.Add(item.GetRawText());
            }
            else if (value.ValueKind == JsonValueKind.String)
            {
                result.AddRange((value.GetString() ?? String.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
            }
            else if (value.ValueKind == JsonValueKind.Number)
            {
                result.Add(value.GetRawText());
            }
            return result.Distinct(StringComparer.OrdinalIgnoreCase).Take(50).ToList();
        }

        /// <summary>
        /// A list filter of enum values.
        /// </summary>
        /// <typeparam name="T">Enum type.</typeparam>
        /// <param name="query">Query.</param>
        /// <param name="name">Filter name.</param>
        /// <param name="errors">Field errors.</param>
        /// <returns>Values.</returns>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        public static List<T> Enums<T>(EnumerationQuery query, string name, Dictionary<string, string> errors) where T : struct, Enum
        {
            ArgumentNullException.ThrowIfNull(errors);
            List<T> result = new List<T>();
            foreach (string raw in Strings(query, name))
            {
                if (!Int32.TryParse(raw, out int _) && Enum.TryParse(raw, true, out T value) && Enum.IsDefined(value)) result.Add(value);
                else errors.TryAdd("filters." + name, "Use one of " + String.Join(", ", Enum.GetNames<T>()) + ".");
            }
            return result.Distinct().ToList();
        }

        /// <summary>
        /// The near filter, or null.
        /// </summary>
        /// <param name="query">Query.</param>
        /// <param name="settings">Directory settings (default and maximum radius).</param>
        /// <returns>Near query, or null.</returns>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        /// <exception cref="ApiErrorException">400 InvalidGeo.</exception>
        public static GeoNear? Near(EnumerationQuery query, DirectorySettings settings)
        {
            ArgumentNullException.ThrowIfNull(query);
            ArgumentNullException.ThrowIfNull(settings);
            if (!Find(query, "near", out JsonElement value) || value.ValueKind == JsonValueKind.Null) return null;
            double? lat;
            double? lng;
            double? radius;
            if (value.ValueKind == JsonValueKind.Object)
            {
                lat = Number(value, "lat");
                lng = Number(value, "lng");
                radius = Number(value, "radiusKm");
            }
            else if (value.ValueKind == JsonValueKind.String)
            {
                List<double?> parts = Parts(value.GetString());
                if (parts.Count < 2 || parts.Count > 3) throw InvalidGeo("filters.near", "Use { lat, lng, radiusKm } or lat,lng[,radiusKm].");
                lat = parts[0];
                lng = parts[1];
                radius = parts.Count == 3 ? parts[2] : null;
            }
            else throw InvalidGeo("filters.near", "Use { lat, lng, radiusKm }.");
            if (lat == null || lat < -90 || lat > 90) throw InvalidGeo("filters.near.lat", "Latitude must be -90 to 90.");
            if (lng == null || lng < -180 || lng > 180) throw InvalidGeo("filters.near.lng", "Longitude must be -180 to 180.");
            double r = radius ?? settings.DefaultRadiusKm;
            if (r < 0.1 || r > settings.MaxRadiusKm) throw InvalidGeo("filters.near.radiusKm", "Radius must be 0.1 to " + settings.MaxRadiusKm.ToString(CultureInfo.InvariantCulture) + " km.");
            return new GeoNear { Lat = lat.Value, Lng = lng.Value, RadiusKm = r };
        }

        /// <summary>
        /// The bbox filter, or null.
        /// </summary>
        /// <param name="query">Query.</param>
        /// <returns>Box, or null.</returns>
        /// <exception cref="ArgumentNullException">The query is null.</exception>
        /// <exception cref="ApiErrorException">400 InvalidGeo.</exception>
        public static GeoBox? Box(EnumerationQuery query)
        {
            ArgumentNullException.ThrowIfNull(query);
            if (!Find(query, "bbox", out JsonElement value) || value.ValueKind == JsonValueKind.Null) return null;
            if (value.ValueKind == JsonValueKind.Object)
                return ValidateBox(new GeoBox { South = Number(value, "south") ?? Double.NaN, West = Number(value, "west") ?? Double.NaN, North = Number(value, "north") ?? Double.NaN, East = Number(value, "east") ?? Double.NaN }, "filters.bbox");
            if (value.ValueKind == JsonValueKind.String)
            {
                List<double?> parts = Parts(value.GetString());
                if (parts.Count != 4 || parts.Any(p => p == null)) throw InvalidGeo("filters.bbox", "Use { south, west, north, east } or south,west,north,east.");
                return ValidateBox(new GeoBox { South = parts[0]!.Value, West = parts[1]!.Value, North = parts[2]!.Value, East = parts[3]!.Value }, "filters.bbox");
            }
            throw InvalidGeo("filters.bbox", "Use { south, west, north, east }.");
        }

        /// <summary>
        /// Validate a box: latitudes -90 to 90 with south below north, longitudes -180 to 180 (west greater than east
        /// crosses the antimeridian).
        /// </summary>
        /// <param name="box">Box, or null.</param>
        /// <param name="field">Field name for errors.</param>
        /// <returns>The box.</returns>
        /// <exception cref="ApiErrorException">400 InvalidGeo.</exception>
        public static GeoBox ValidateBox(GeoBox? box, string field)
        {
            if (box == null) throw InvalidGeo(field, "A bounding box is required.");
            if (Double.IsNaN(box.South) || Double.IsNaN(box.North) || Double.IsNaN(box.West) || Double.IsNaN(box.East)) throw InvalidGeo(field, "south, west, north, and east are required.");
            if (box.South < -90 || box.South > 90 || box.North < -90 || box.North > 90 || box.South >= box.North) throw InvalidGeo(field, "Latitudes must be -90 to 90 with south below north.");
            if (box.West < -180 || box.West > 180 || box.East < -180 || box.East > 180 || box.West == box.East) throw InvalidGeo(field, "Longitudes must be -180 to 180 (west greater than east crosses the antimeridian).");
            return box;
        }

        /// <summary>
        /// A 400 InvalidGeo error.
        /// </summary>
        /// <param name="field">Field.</param>
        /// <param name="message">Problem.</param>
        /// <returns>Exception.</returns>
        public static ApiErrorException InvalidGeo(string field, string message)
        {
            return new ApiErrorException(400, "InvalidGeo", message, new Dictionary<string, string> { { field, message } });
        }

        /// <summary>
        /// Throw 400 ValidationFailed when there are errors.
        /// </summary>
        /// <param name="errors">Field errors.</param>
        /// <exception cref="ArgumentNullException">The errors are null.</exception>
        /// <exception cref="ApiErrorException">400 ValidationFailed.</exception>
        public static void ThrowIfAny(Dictionary<string, string> errors)
        {
            ArgumentNullException.ThrowIfNull(errors);
            if (errors.Count > 0) throw ApiErrorException.Validation(errors, "One or more filters are invalid.");
        }

        #endregion

        #region Private-Methods

        private static bool Find(EnumerationQuery query, string name, out JsonElement value)
        {
            value = default;
            foreach (KeyValuePair<string, JsonElement> pair in query.Filters)
            {
                if (!String.Equals(pair.Key, name, StringComparison.OrdinalIgnoreCase)) continue;
                value = pair.Value;
                return value.ValueKind != JsonValueKind.Undefined;
            }
            return false;
        }

        private static double? Number(JsonElement obj, string name)
        {
            foreach (JsonProperty p in obj.EnumerateObject())
            {
                if (!String.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)) continue;
                if (p.Value.ValueKind == JsonValueKind.Number && p.Value.TryGetDouble(out double d) && !Double.IsNaN(d) && !Double.IsInfinity(d)) return d;
                if (p.Value.ValueKind == JsonValueKind.String && Double.TryParse(p.Value.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out double s)) return s;
                return null;
            }
            return null;
        }

        private static List<double?> Parts(string? text)
        {
            List<double?> result = new List<double?>();
            foreach (string part in (text ?? String.Empty).Split(',', StringSplitOptions.TrimEntries))
                result.Add(Double.TryParse(part, NumberStyles.Float, CultureInfo.InvariantCulture, out double d) ? d : null);
            return result;
        }

        #endregion
    }
}
