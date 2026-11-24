namespace TrellisKit.Server.Settings
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Security.Cryptography;
    using System.Text.Json;
    using TrellisKit.Core.Settings;
    using TrellisKit.Server.Serialization;

    /// <summary>
    /// Loads and saves the settings JSON file and applies environment variable overrides.
    /// Thread safety: Save is atomic (write to a temporary file, then replace); callers serialize concurrent updates.
    /// </summary>
    public class SettingsStore
    {
        #region Public-Members

        /// <summary>
        /// Full path of the settings file.
        /// </summary>
        public string FilePath { get; }

        #endregion

        #region Private-Members

        private readonly Func<string, string?> _GetEnvironment;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the store.
        /// </summary>
        /// <param name="filePath">Settings file path.</param>
        /// <param name="getEnvironment">Environment variable reader. Default reads the process environment.</param>
        /// <exception cref="ArgumentNullException">The path is null or empty.</exception>
        public SettingsStore(string filePath, Func<string, string?>? getEnvironment = null)
        {
            if (String.IsNullOrWhiteSpace(filePath)) throw new ArgumentNullException(nameof(filePath));
            FilePath = Path.GetFullPath(filePath);
            _GetEnvironment = getEnvironment ?? Environment.GetEnvironmentVariable;
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Load the settings file without environment overrides. Returns defaults when the file does not exist.
        /// </summary>
        /// <returns>Settings.</returns>
        /// <exception cref="InvalidOperationException">The file exists but is not valid.</exception>
        public AppSettings LoadFile()
        {
            if (!File.Exists(FilePath)) return new AppSettings();
            try
            {
                string json = File.ReadAllText(FilePath);
                return JsonSerializer.Deserialize<AppSettings>(json, ApiJson.File) ?? new AppSettings();
            }
            catch (Exception e) when (e is JsonException || e is ArgumentException || e is NotSupportedException)
            {
                throw new InvalidOperationException("Settings file " + FilePath + " is invalid: " + e.Message, e);
            }
        }

        /// <summary>
        /// Write settings to the file atomically.
        /// </summary>
        /// <param name="settings">Settings.</param>
        /// <exception cref="ArgumentNullException">Settings are null.</exception>
        public void Save(AppSettings settings)
        {
            ArgumentNullException.ThrowIfNull(settings);
            string? directory = Path.GetDirectoryName(FilePath);
            if (!String.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

            string temp = FilePath + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(settings, ApiJson.File));
            File.Move(temp, FilePath, true);
        }

        /// <summary>
        /// Apply environment variable overrides to settings.
        /// </summary>
        /// <param name="settings">Settings to modify.</param>
        /// <returns>Keys of the settings that were overridden.</returns>
        /// <exception cref="ArgumentNullException">Settings are null.</exception>
        /// <exception cref="InvalidOperationException">An environment variable has an invalid value.</exception>
        public HashSet<string> ApplyEnvironment(AppSettings settings)
        {
            ArgumentNullException.ThrowIfNull(settings);
            HashSet<string> overridden = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (SettingFieldDefinition definition in SettingsRegistry.All)
            {
                if (definition.EnvironmentVariable == null) continue;
                string? raw = _GetEnvironment(definition.EnvironmentVariable);
                if (String.IsNullOrEmpty(raw)) continue;
                try
                {
                    definition.SetValue(settings, definition.ParseEnvironmentValue(raw));
                    overridden.Add(definition.Key);
                }
                catch (ArgumentException e)
                {
                    throw new InvalidOperationException(
                        "Environment variable " + definition.EnvironmentVariable + " is invalid for " + definition.Key + ": " + e.Message, e);
                }
            }
            return overridden;
        }

        /// <summary>
        /// Load the effective settings: the file (created with defaults if missing), then environment overrides.
        /// When no admin token is configured anywhere, a random token is generated and saved to the file; likewise a
        /// random access token encryption key (auth.tokenEncryptionKey).
        /// </summary>
        /// <param name="generatedAdminToken">The newly generated admin token, or null if one already existed.</param>
        /// <returns>Effective settings.</returns>
        /// <exception cref="InvalidOperationException">The file or an environment variable is invalid.</exception>
        public AppSettings LoadEffective(out string? generatedAdminToken)
        {
            generatedAdminToken = null;
            AppSettings fileSettings = LoadFile();
            bool save = !File.Exists(FilePath);

            AppSettings probe = LoadFileOrDefault(fileSettings);
            ApplyEnvironment(probe);
            if (String.IsNullOrWhiteSpace(probe.Auth.AdminToken))
            {
                generatedAdminToken = GenerateToken();
                fileSettings.Auth.AdminToken = generatedAdminToken;
                save = true;
            }

            if (String.IsNullOrWhiteSpace(probe.Auth.TokenEncryptionKey))
            {
                fileSettings.Auth.TokenEncryptionKey = TrellisKit.Core.Security.AesGcmTokenProtector.GenerateKey();
                save = true;
            }

            if (String.IsNullOrWhiteSpace(probe.Secrets.EncryptionKey) && probe.Secrets.AllowGeneratedKey)
            {
                // The engine secrets key encrypts per-property integration secrets and queued email payloads. Generated
                // once and saved like the token key only in local development (secrets.allowGeneratedKey); everywhere
                // else TK_SECRETS_ENCRYPTION_KEY comes from Secrets Manager and SecretKeyCheck stops startup without it.
                fileSettings.Secrets.EncryptionKey = TrellisKit.Core.Security.SecretBox.GenerateKey();
                save = true;
            }

            if (save) Save(fileSettings);

            AppSettings effective = LoadFile();
            ApplyEnvironment(effective);
            return effective;
        }

        /// <summary>
        /// Generate a random admin token: "tk_" followed by 48 URL-safe characters.
        /// </summary>
        /// <returns>Token.</returns>
        public static string GenerateToken()
        {
            const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-_";
            return TrellisKit.Core.Constants.AdminTokenPrefix + RandomNumberGenerator.GetString(alphabet, 48);
        }

        #endregion

        #region Private-Methods

        private AppSettings LoadFileOrDefault(AppSettings fallback)
        {
            return File.Exists(FilePath) ? LoadFile() : JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(fallback, ApiJson.File), ApiJson.File) ?? new AppSettings();
        }

        #endregion
    }
}
