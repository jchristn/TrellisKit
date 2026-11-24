namespace TrellisKit.Core.Helpers
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Reflection;
    using System.Text.Json;
    using System.Text.RegularExpressions;
    using TrellisKit.Core.Models;

    /// <summary>
    /// The engine's palette preset catalog (docs/TRELLISKIT.md section 2.1), read once from the embedded asset
    /// Assets/palette-presets.json. The same file is the single source of the preset colors: the API serves it (admin
    /// Branding view and, filtered by the property's member choices, GET /api/v1/property brand.presets) and the shared
    /// JavaScript tests read it, so hex values are not duplicated across languages.
    /// Thread safety: the catalog is loaded once and never mutated; callers get copies.
    /// </summary>
    public static class BrandPresetCatalog
    {
        #region Public-Members

        /// <summary>
        /// Every preset, in catalog order. Never mutate the entries; clone them first.
        /// </summary>
        /// <exception cref="InvalidOperationException">The embedded catalog is missing or invalid.</exception>
        public static IReadOnlyList<BrandPreset> All
        {
            get { return _Presets.Value; }
        }

        /// <summary>
        /// The most presets the catalog may hold (bounds member choices and the API lists).
        /// </summary>
        public const int MaxPresets = 32;

        #endregion

        #region Private-Members

        private const string _Resource = "TrellisKit.Core.Assets.palette-presets.json";
        private static readonly Regex _Id = new Regex("^[a-z0-9]+(-[a-z0-9]+)*$", RegexOptions.CultureInvariant);
        private static readonly Lazy<IReadOnlyList<BrandPreset>> _Presets = new Lazy<IReadOnlyList<BrandPreset>>(Load);

        #endregion

        #region Public-Methods

        /// <summary>
        /// Find a preset by id (case-sensitive).
        /// </summary>
        /// <param name="id">Preset id, or null.</param>
        /// <returns>The catalog entry (do not mutate), or null.</returns>
        public static BrandPreset? Find(string? id)
        {
            if (String.IsNullOrEmpty(id)) return null;
            foreach (BrandPreset preset in All)
            {
                if (String.Equals(preset.Id, id, StringComparison.Ordinal)) return preset;
            }
            return null;
        }

        /// <summary>
        /// Parse and validate a catalog document: version 1, at least one and at most <see cref="MaxPresets"/> presets,
        /// unique ids, a name, and every brand color key in both schemes as a hex color (normalized to uppercase
        /// #RRGGBB). Contrast is not checked here (the tests assert it for every preset).
        /// </summary>
        /// <param name="json">Catalog JSON.</param>
        /// <returns>Presets in document order.</returns>
        /// <exception cref="ArgumentNullException">The JSON is null.</exception>
        /// <exception cref="InvalidOperationException">The document is invalid; the message names the problem.</exception>
        public static List<BrandPreset> Parse(string json)
        {
            ArgumentNullException.ThrowIfNull(json);
            List<BrandPreset> presets = new List<BrandPreset>();
            using (JsonDocument doc = JsonDocument.Parse(json))
            {
                JsonElement root = doc.RootElement;
                if (!root.TryGetProperty("version", out JsonElement version) || version.ValueKind != JsonValueKind.Number || version.GetInt32() != 1)
                    throw new InvalidOperationException("Palette preset catalog: version must be 1.");
                if (!root.TryGetProperty("presets", out JsonElement list) || list.ValueKind != JsonValueKind.Array)
                    throw new InvalidOperationException("Palette preset catalog: presets must be an array.");
                foreach (JsonElement item in list.EnumerateArray())
                {
                    string id = Text(item, "id");
                    if (!_Id.IsMatch(id)) throw new InvalidOperationException("Palette preset catalog: id '" + id + "' must be lowercase letters, digits, and hyphens.");
                    if (presets.Any(p => p.Id == id)) throw new InvalidOperationException("Palette preset catalog: duplicate id '" + id + "'.");
                    string name = Text(item, "name");
                    if (name.Length == 0) throw new InvalidOperationException("Palette preset catalog: preset '" + id + "' needs a name.");
                    presets.Add(new BrandPreset
                    {
                        Id = id,
                        Name = name,
                        Description = Text(item, "description"),
                        Light = Scheme(item, "light", id),
                        Dark = Scheme(item, "dark", id)
                    });
                }
            }
            if (presets.Count == 0 || presets.Count > MaxPresets) throw new InvalidOperationException("Palette preset catalog: between 1 and " + MaxPresets + " presets are required.");
            return presets;
        }

        #endregion

        #region Private-Methods

        private static IReadOnlyList<BrandPreset> Load()
        {
            Assembly assembly = typeof(BrandPresetCatalog).Assembly;
            using (Stream? stream = assembly.GetManifestResourceStream(_Resource))
            {
                if (stream == null) throw new InvalidOperationException("Palette preset catalog resource " + _Resource + " is missing.");
                using (StreamReader reader = new StreamReader(stream))
                {
                    return Parse(reader.ReadToEnd()).AsReadOnly();
                }
            }
        }

        private static string Text(JsonElement item, string name)
        {
            if (!item.TryGetProperty(name, out JsonElement value) || value.ValueKind != JsonValueKind.String) return String.Empty;
            return (value.GetString() ?? String.Empty).Trim();
        }

        private static BrandColorSet Scheme(JsonElement item, string scheme, string id)
        {
            if (!item.TryGetProperty(scheme, out JsonElement element) || element.ValueKind != JsonValueKind.Object)
                throw new InvalidOperationException("Palette preset catalog: preset '" + id + "' needs a " + scheme + " scheme.");
            BrandColorSet set = new BrandColorSet();
            foreach (string key in BrandColorSet.Keys)
            {
                string? value = BrandContrast.NormalizeHex(Text(element, key));
                if (value == null) throw new InvalidOperationException("Palette preset catalog: preset '" + id + "' " + scheme + "." + key + " must be a hex color.");
                set.Set(key, value);
            }
            return set;
        }

        #endregion
    }
}
