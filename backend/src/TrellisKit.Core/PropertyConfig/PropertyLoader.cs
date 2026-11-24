namespace TrellisKit.Core.PropertyConfig
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Text.Json;
    using System.Text.RegularExpressions;
    using TrellisKit.Core.Exceptions;

    /// <summary>
    /// Locates, reads, and validates the active property (property.json and its brand folder).
    /// The property is selected only by directory path (setting property.directory, environment variable
    /// TK_PROPERTY_DIR). When no directory is configured the engine's neutral sample property is used: the loader
    /// searches upward from the content root and the application directory for samples/example/property.json, then
    /// falls back to the Docker mount at /app/property.
    /// Thread safety: stateless; safe to call concurrently.
    /// </summary>
    public static class PropertyLoader
    {
        #region Public-Members

        /// <summary>
        /// Property file name inside the property directory.
        /// </summary>
        public const string PropertyFileName = "property.json";

        /// <summary>
        /// Folder inside the property directory whose files are served as brand assets.
        /// </summary>
        public const string BrandFolder = "brand";

        /// <summary>
        /// Path of the engine's sample property relative to the engine root, used when no directory is configured.
        /// </summary>
        public const string SampleRelativePath = "samples/example";

        /// <summary>
        /// Directory where container deployments mount the property.
        /// </summary>
        public const string ContainerDirectory = "/app/property";

        #endregion

        #region Private-Members

        private static readonly Regex _AssetName = new Regex("^[A-Za-z0-9][A-Za-z0-9_-]*(\\.[A-Za-z0-9_-]+)*$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

        private static readonly JsonSerializerOptions _Json = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
            Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
        };

        #endregion

        #region Public-Methods

        /// <summary>
        /// Resolve the property directory. A configured absolute path is used as is. A configured relative path is
        /// tried against each search root and each of its ancestors (so it may be relative to the working directory or
        /// to the engine root). With no configured path, the sample property is searched for the same way, then the
        /// container mount is tried.
        /// </summary>
        /// <param name="configured">Configured directory (property.directory), or null or empty for the default.</param>
        /// <param name="searchRoots">Directories to search from. Null uses the working directory and the application base directory.</param>
        /// <returns>Full path of a directory that contains property.json.</returns>
        /// <exception cref="PropertyConfigurationException">No directory with a property.json was found; the message lists what was tried.</exception>
        public static string ResolveDirectory(string? configured, IEnumerable<string>? searchRoots = null)
        {
            List<string> roots = (searchRoots ?? new[] { System.IO.Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
                .Where(r => !String.IsNullOrWhiteSpace(r)).ToList();
            List<string> tried = new List<string>();

            if (!String.IsNullOrWhiteSpace(configured))
            {
                string value = configured.Trim();
                if (Path.IsPathRooted(value))
                {
                    string full = Path.GetFullPath(value);
                    if (HasPropertyFile(full)) return full;
                    throw new PropertyConfigurationException("Property directory " + full + " (property.directory / TK_PROPERTY_DIR) does not contain " + PropertyFileName + ".");
                }

                string? found = SearchUpward(value, roots, tried);
                if (found != null) return found;
                throw new PropertyConfigurationException("Property directory \"" + value + "\" (property.directory / TK_PROPERTY_DIR) was not found with a " + PropertyFileName + "; tried " + String.Join(", ", tried) + ".");
            }

            string? sample = SearchUpward(SampleRelativePath, roots, tried);
            if (sample != null) return sample;
            if (HasPropertyFile(ContainerDirectory)) return ContainerDirectory;
            tried.Add(ContainerDirectory);
            throw new PropertyConfigurationException("No property directory is configured and the sample property was not found. Set property.directory or TK_PROPERTY_DIR to a folder containing " + PropertyFileName + "; tried " + String.Join(", ", tried) + ".");
        }

        /// <summary>
        /// Read and validate the property in a directory.
        /// </summary>
        /// <param name="directory">Property directory.</param>
        /// <returns>Validated property with <see cref="PropertyConfiguration.Directory"/> set.</returns>
        /// <exception cref="ArgumentNullException">The directory is null or empty.</exception>
        /// <exception cref="PropertyConfigurationException">The file is missing, unreadable, not valid JSON, or fails validation; the message names each problem.</exception>
        public static PropertyConfiguration Load(string directory)
        {
            if (String.IsNullOrWhiteSpace(directory)) throw new ArgumentNullException(nameof(directory));
            string full = Path.GetFullPath(directory);
            string file = Path.Combine(full, PropertyFileName);
            if (!File.Exists(file)) throw new PropertyConfigurationException("Property file " + file + " was not found.");

            string json;
            try
            {
                json = File.ReadAllText(file);
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                throw new PropertyConfigurationException("Property file " + file + " could not be read: " + e.Message, e);
            }

            PropertyConfiguration property = Parse(json, file);
            property.Directory = full;

            List<string> problems = PropertyValidator.Validate(property);
            if (property.Brand != null)
            {
                MissingAsset(full, property.Brand.Logo, "brand.logo", problems);
                MissingAsset(full, property.Brand.Icon, "brand.icon", problems);
                MissingAsset(full, property.Brand.IconIco, "brand.iconIco", problems);
            }
            if (property.Documents != null)
            {
                for (int i = 0; i < property.Documents.Count; i++)
                {
                    PropertyDocument? document = property.Documents[i];
                    if (document == null || !IsSafeDocumentPath(document.File)) continue;
                    if (ResolveDocument(property, document.File) == null) problems.Add("documents[" + i + "].file file " + Path.GetFullPath(Path.Combine(full, document.File)) + " was not found");
                }
            }
            if (problems.Count > 0) throw new PropertyConfigurationException(file, problems);
            return property;
        }

        /// <summary>
        /// Parse property JSON without validating it.
        /// </summary>
        /// <param name="json">JSON text.</param>
        /// <param name="source">Source name used in error messages.</param>
        /// <returns>Property.</returns>
        /// <exception cref="PropertyConfigurationException">The text is empty or not valid JSON for a property.</exception>
        public static PropertyConfiguration Parse(string json, string source)
        {
            if (String.IsNullOrWhiteSpace(json)) throw new PropertyConfigurationException("Property file " + source + " is empty.");
            try
            {
                PropertyConfiguration? property = JsonSerializer.Deserialize<PropertyConfiguration>(json, _Json);
                if (property == null) throw new PropertyConfigurationException("Property file " + source + " does not contain a JSON object.");
                // membership is optional in the Open access model; the minimum age still comes from it (default 18).
                if (property.Membership == null && property.Access?.Model == Enums.AccessModelEnum.Open) property.Membership = new PropertyMembership();
                return property;
            }
            catch (JsonException e)
            {
                throw new PropertyConfigurationException("Property file " + source + " is not valid JSON: " + e.Message, e);
            }
        }

        /// <summary>
        /// Whether a file name is safe to serve from the brand folder: one path segment of letters, digits,
        /// underscores, hyphens, and single dots (no separators, no "..", no leading dot).
        /// </summary>
        /// <param name="name">File name.</param>
        /// <returns>True when safe.</returns>
        public static bool IsSafeAssetName(string? name)
        {
            return !String.IsNullOrEmpty(name) && name.Length <= 128 && _AssetName.IsMatch(name);
        }

        /// <summary>
        /// Full path of a brand asset, or null when the name is unsafe, the property has no directory, or the file
        /// does not exist directly under the brand folder.
        /// </summary>
        /// <param name="property">Property.</param>
        /// <param name="fileName">File name, for example logo.png.</param>
        /// <returns>Full path, or null.</returns>
        /// <exception cref="ArgumentNullException">The property is null.</exception>
        public static string? ResolveAsset(PropertyConfiguration property, string? fileName)
        {
            ArgumentNullException.ThrowIfNull(property);
            if (!IsSafeAssetName(fileName) || String.IsNullOrEmpty(property.Directory)) return null;
            string brand = Path.GetFullPath(Path.Combine(property.Directory, BrandFolder));
            string candidate = Path.GetFullPath(Path.Combine(brand, fileName!));
            string prefix = brand.EndsWith(Path.DirectorySeparatorChar) ? brand : brand + Path.DirectorySeparatorChar;
            if (!candidate.StartsWith(prefix, StringComparison.Ordinal)) return null;
            if (!String.Equals(Path.GetDirectoryName(candidate), brand.TrimEnd(Path.DirectorySeparatorChar), StringComparison.Ordinal)) return null;
            return File.Exists(candidate) ? candidate : null;
        }

        /// <summary>
        /// Full path of a property document's Markdown file (docs/TRELLISKIT.md section 14), or null when the property has
        /// no directory, the path is unsafe (absolute, outside the property folder, or not a .md file), or the file does
        /// not exist.
        /// </summary>
        /// <param name="property">Property.</param>
        /// <param name="relativePath">Path relative to the property directory, for example legal/terms.md.</param>
        /// <returns>Full path, or null.</returns>
        /// <exception cref="ArgumentNullException">The property is null.</exception>
        public static string? ResolveDocument(PropertyConfiguration property, string? relativePath)
        {
            ArgumentNullException.ThrowIfNull(property);
            if (!IsSafeDocumentPath(relativePath) || String.IsNullOrEmpty(property.Directory)) return null;
            string root = Path.GetFullPath(property.Directory);
            string candidate = Path.GetFullPath(Path.Combine(root, relativePath!.Replace('\\', '/')));
            string prefix = root.EndsWith(Path.DirectorySeparatorChar) ? root : root + Path.DirectorySeparatorChar;
            if (!candidate.StartsWith(prefix, StringComparison.Ordinal)) return null;
            return File.Exists(candidate) ? candidate : null;
        }

        /// <summary>
        /// Whether a document path is acceptable in property.json: relative, at most 256 characters, path segments of
        /// letters, digits, underscores, hyphens, and single dots (no "..", no leading dot), ending in .md.
        /// </summary>
        /// <param name="relativePath">Path.</param>
        /// <returns>True when safe.</returns>
        public static bool IsSafeDocumentPath(string? relativePath)
        {
            if (String.IsNullOrWhiteSpace(relativePath) || relativePath.Length > 256) return false;
            string normalized = relativePath.Replace('\\', '/');
            if (normalized.StartsWith("/", StringComparison.Ordinal) || !normalized.EndsWith(".md", StringComparison.OrdinalIgnoreCase)) return false;
            foreach (string segment in normalized.Split('/'))
            {
                if (!IsSafeAssetName(segment)) return false;
            }
            return true;
        }

        /// <summary>
        /// File name of a brand path such as brand/logo.png, or null when the path is empty.
        /// </summary>
        /// <param name="brandPath">Path relative to the property directory.</param>
        /// <returns>File name, or null.</returns>
        public static string? AssetFileName(string? brandPath)
        {
            if (String.IsNullOrWhiteSpace(brandPath)) return null;
            string normalized = brandPath.Replace('\\', '/');
            int slash = normalized.LastIndexOf('/');
            return slash >= 0 ? normalized.Substring(slash + 1) : normalized;
        }

        #endregion

        #region Private-Methods

        private static bool HasPropertyFile(string directory)
        {
            try
            {
                return File.Exists(Path.Combine(directory, PropertyFileName));
            }
            catch (Exception e) when (e is ArgumentException || e is IOException || e is UnauthorizedAccessException)
            {
                return false;
            }
        }

        private static string? SearchUpward(string relative, List<string> roots, List<string> tried)
        {
            foreach (string root in roots)
            {
                DirectoryInfo? current = new DirectoryInfo(Path.GetFullPath(root));
                while (current != null)
                {
                    string candidate = Path.GetFullPath(Path.Combine(current.FullName, relative));
                    if (!tried.Contains(candidate, StringComparer.Ordinal)) tried.Add(candidate);
                    if (HasPropertyFile(candidate)) return candidate;
                    current = current.Parent;
                }
            }
            return null;
        }

        private static void MissingAsset(string directory, string? brandPath, string field, List<string> problems)
        {
            if (String.IsNullOrWhiteSpace(brandPath)) return;
            string full = Path.GetFullPath(Path.Combine(directory, brandPath));
            if (!File.Exists(full)) problems.Add(field + " file " + full + " was not found");
        }

        #endregion
    }
}
