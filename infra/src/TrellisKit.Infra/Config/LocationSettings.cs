namespace TrellisKit.Infra.Config
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text.Json;
    using System.Text.RegularExpressions;

    /// <summary>
    /// Amazon Location Service resources for the directory geocoder and map (docs/PHASE4_CONTRACT.md section 6): an
    /// optional place index (geo:SearchPlaceIndexForText from the task role, used by the AwsLocation geocoder) and an
    /// optional map resource (geo:GetMap* from the task role). Read from the location section of a property's
    /// deploy/infra.json; in the shared topology each property's own section decides its own resources. Off by
    /// default: without it no Location resource or permission exists, so it costs nothing. The live property settings
    /// (directory.geocoder.provider, directory.geocoder.placeIndexName, directory.geocoder.region, directory.map.*) are
    /// never set by the stacks; an administrator enters them in the admin console from the stack outputs.
    /// Thread safety: not thread safe; configure before building stacks.
    /// </summary>
    public class LocationSettings
    {
        /// <summary>
        /// Data providers a place index accepts.
        /// </summary>
        public static readonly IReadOnlyList<string> DataProviders = new List<string> { "Esri", "Here", "Grab" };

        /// <summary>
        /// Intended uses a place index accepts. Storage allows keeping results (the geocoder caches answers and stores
        /// venue coordinates); SingleUse forbids it.
        /// </summary>
        public static readonly IReadOnlyList<string> IntendedUses = new List<string> { "Storage", "SingleUse" };

        private static readonly Regex _StylePattern = new Regex("^[A-Za-z0-9]{1,100}$");

        private string _DataProvider = "Esri";
        private string _IntendedUse = "Storage";
        private string _MapStyle = "VectorEsriNavigation";

        /// <summary>
        /// Create the place index {prefix}-places and grant the task role geo:SearchPlaceIndexForText on it. Default false.
        /// </summary>
        public bool PlaceIndexEnabled { get; set; } = false;

        /// <summary>
        /// Place index data provider: Esri, Here, or Grab. Default Esri.
        /// </summary>
        /// <exception cref="ArgumentException">The value is not a known provider.</exception>
        public string DataProvider
        {
            get { return _DataProvider; }
            set { _DataProvider = OneOf(value, DataProviders, "location.dataProvider"); }
        }

        /// <summary>
        /// Place index intended use: Storage or SingleUse. Default Storage (the geocoder caches and stores results).
        /// </summary>
        /// <exception cref="ArgumentException">The value is not a known intended use.</exception>
        public string IntendedUse
        {
            get { return _IntendedUse; }
            set { _IntendedUse = OneOf(value, IntendedUses, "location.intendedUse"); }
        }

        /// <summary>
        /// Create the map resource {prefix}-map and grant the task role the geo:GetMap* actions on it. Default false.
        /// </summary>
        public bool MapEnabled { get; set; } = false;

        /// <summary>
        /// Map style of the map resource, for example VectorEsriNavigation or VectorHereExplore. Default VectorEsriNavigation.
        /// </summary>
        /// <exception cref="ArgumentException">The value is not a style name.</exception>
        public string MapStyle
        {
            get
            {
                return _MapStyle;
            }
            set
            {
                string text = (value ?? String.Empty).Trim();
                if (!_StylePattern.IsMatch(text)) throw new ArgumentException("location.mapStyle must be a map style name, got '" + text + "'.", nameof(MapStyle));
                _MapStyle = text;
            }
        }

        /// <summary>
        /// Whether any Location resource is created.
        /// </summary>
        public bool AnyEnabled
        {
            get { return PlaceIndexEnabled || MapEnabled; }
        }

        /// <summary>
        /// Read the location section of a deploy/infra.json file: placeIndex (boolean), dataProvider, intendedUse, map
        /// (boolean), and mapStyle. A missing or non-object section yields the defaults (nothing created).
        /// </summary>
        /// <param name="root">Root element of the file.</param>
        /// <param name="file">File path, used in error messages.</param>
        /// <returns>Settings.</returns>
        /// <exception cref="ArgumentException">A value is invalid.</exception>
        public static LocationSettings FromJson(JsonElement root, string file)
        {
            LocationSettings settings = new LocationSettings();
            if (root.ValueKind != JsonValueKind.Object) return settings;
            if (!root.TryGetProperty("location", out JsonElement location) || location.ValueKind != JsonValueKind.Object) return settings;

            try
            {
                settings.PlaceIndexEnabled = Flag(location, "placeIndex");
                settings.MapEnabled = Flag(location, "map");
                if (location.TryGetProperty("dataProvider", out JsonElement provider) && provider.ValueKind == JsonValueKind.String) settings.DataProvider = provider.GetString() ?? String.Empty;
                if (location.TryGetProperty("intendedUse", out JsonElement use) && use.ValueKind == JsonValueKind.String) settings.IntendedUse = use.GetString() ?? String.Empty;
                if (location.TryGetProperty("mapStyle", out JsonElement style) && style.ValueKind == JsonValueKind.String) settings.MapStyle = style.GetString() ?? String.Empty;
            }
            catch (ArgumentException e)
            {
                throw new ArgumentException("Invalid location section in " + file + ": " + e.Message, e);
            }

            return settings;
        }

        private static bool Flag(JsonElement element, string name)
        {
            if (!element.TryGetProperty(name, out JsonElement value)) return false;
            if (value.ValueKind != JsonValueKind.True && value.ValueKind != JsonValueKind.False)
            {
                throw new ArgumentException("location." + name + " must be true or false.");
            }

            return value.GetBoolean();
        }

        private static string OneOf(string value, IReadOnlyList<string> allowed, string name)
        {
            string text = (value ?? String.Empty).Trim();
            if (!allowed.Contains(text)) throw new ArgumentException(name + " must be one of " + String.Join(", ", allowed) + ", got '" + text + "'.", name);
            return text;
        }
    }
}
