namespace TrellisKit.Server.Services
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Reflection;
    using System.Text.Json;
    using TrellisKit.Core.Models;

    /// <summary>
    /// The engine string catalog (docs/LIVE_UPDATES_CONTRACT.md section 14.3), generated from the website's and the mobile
    /// app's strings modules by shared/scripts/strings-catalog.mjs and embedded in the server.
    /// Thread safety: immutable after load.
    /// </summary>
    public class StringCatalog
    {
        #region Public-Members

        /// <summary>
        /// SHA-256 hex of the entries (changes whenever a key, default, or token changes).
        /// </summary>
        public string Sha256 { get; }

        /// <summary>
        /// Entries ordered by key.
        /// </summary>
        public List<StringCatalogEntry> Entries { get; }

        #endregion

        #region Private-Members

        private const string _Resource = "TrellisKit.Server.Assets.string-catalog.json";

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate a catalog.
        /// </summary>
        /// <param name="sha256">Catalog hash.</param>
        /// <param name="entries">Entries.</param>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        public StringCatalog(string sha256, List<StringCatalogEntry> entries)
        {
            Sha256 = sha256 ?? throw new ArgumentNullException(nameof(sha256));
            Entries = entries ?? throw new ArgumentNullException(nameof(entries));
        }

        /// <summary>
        /// Parse a catalog document ({ version, sha256, entries: [{ key, client, default, tokens, maxLength }] }).
        /// </summary>
        /// <param name="json">Document text.</param>
        /// <returns>Catalog.</returns>
        /// <exception cref="ArgumentNullException">The text is null.</exception>
        /// <exception cref="JsonException">The text is not a catalog document.</exception>
        public static StringCatalog Parse(string json)
        {
            ArgumentNullException.ThrowIfNull(json);
            using (JsonDocument document = JsonDocument.Parse(json))
            {
                JsonElement root = document.RootElement;
                string sha = root.TryGetProperty("sha256", out JsonElement s) && s.ValueKind == JsonValueKind.String ? s.GetString() ?? String.Empty : String.Empty;
                List<StringCatalogEntry> entries = new List<StringCatalogEntry>();
                if (root.TryGetProperty("entries", out JsonElement list) && list.ValueKind == JsonValueKind.Array)
                {
                    foreach (JsonElement e in list.EnumerateArray())
                    {
                        string key = e.TryGetProperty("key", out JsonElement k) ? k.GetString() ?? String.Empty : String.Empty;
                        if (key.Length == 0) continue;
                        entries.Add(new StringCatalogEntry
                        {
                            Key = key,
                            Client = e.TryGetProperty("client", out JsonElement c) ? c.GetString() ?? "web" : "web",
                            DefaultValue = e.TryGetProperty("default", out JsonElement d) ? d.GetString() ?? String.Empty : String.Empty,
                            Tokens = e.TryGetProperty("tokens", out JsonElement t) && t.ValueKind == JsonValueKind.Array ? t.EnumerateArray().Select(x => x.GetString() ?? String.Empty).Where(x => x.Length > 0).ToList() : new List<string>(),
                            MaxLength = e.TryGetProperty("maxLength", out JsonElement m) && m.TryGetInt32(out int max) ? Math.Clamp(max, 1, 2000) : 200
                        });
                    }
                }
                if (sha.Length == 0) sha = "unversioned-" + entries.Count;
                return new StringCatalog(sha, entries);
            }
        }

        /// <summary>
        /// Load the catalog embedded in the server assembly (an empty catalog when the resource is missing).
        /// </summary>
        /// <returns>Catalog.</returns>
        public static StringCatalog LoadEmbedded()
        {
            Assembly assembly = typeof(StringCatalog).Assembly;
            using (Stream? stream = assembly.GetManifestResourceStream(_Resource))
            {
                if (stream == null) return new StringCatalog("empty", new List<StringCatalogEntry>());
                using (StreamReader reader = new StreamReader(stream))
                {
                    return Parse(reader.ReadToEnd());
                }
            }
        }

        #endregion
    }
}
