namespace TrellisKit.Server.Services
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core.Database;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Exceptions;
    using TrellisKit.Core.Models;
    using TrellisKit.Core.Observability;
    using TrellisKit.Core.Requests;
    using TrellisKit.Core.Responses;
    using TrellisKit.Core.Security;
    using TrellisKit.Core.Settings;
    using TrellisKit.Server.Settings;

    /// <summary>
    /// Per-property settings (docs/MULTIPROPERTY_CONTRACT.md sections 5.2, 5.5, and 5.7): read and write through the admin
    /// portal only, secrets encrypted with the engine secrets key and bound to "&lt;propertyId&gt;|&lt;key&gt;", values
    /// cached per property and reloaded after a write.
    /// Thread safety: safe to call concurrently.
    /// </summary>
    public class PropertySettingsService
    {
        #region Public-Members

        /// <summary>
        /// Called after settings change (integrations rebuild).
        /// </summary>
        public Action? Changed { get; set; } = null;

        #endregion

        #region Private-Members

        private readonly DatabaseDriverBase _Database;
        private readonly AppSettings _Settings;
        private readonly string _PropertyId;
        private readonly AuditService _Audit;
        private readonly AppLogger _Logger;
        private Dictionary<string, PropertySettingRow>? _Rows = null;
        private DateTime _LoadedUtc = DateTime.MinValue;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the service.
        /// </summary>
        /// <param name="database">Property-bound database driver.</param>
        /// <param name="settings">Running settings (secrets key).</param>
        /// <param name="propertyId">Property id.</param>
        /// <param name="audit">Audit service.</param>
        /// <param name="logger">Logger.</param>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        public PropertySettingsService(DatabaseDriverBase database, AppSettings settings, string propertyId, AuditService audit, AppLogger logger)
        {
            _Database = database ?? throw new ArgumentNullException(nameof(database));
            _Settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _PropertyId = propertyId ?? throw new ArgumentNullException(nameof(propertyId));
            _Audit = audit ?? throw new ArgumentNullException(nameof(audit));
            _Logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// The effective value of a setting: the stored value (secrets decrypted), else null.
        /// </summary>
        /// <param name="key">Key.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Value, or null when not set (or a secret that no longer decrypts).</returns>
        public async Task<string?> GetAsync(string key, CancellationToken token = default)
        {
            Dictionary<string, PropertySettingRow> rows = await RowsAsync(token).ConfigureAwait(false);
            if (!rows.TryGetValue(key, out PropertySettingRow? row)) return null;
            if (row.Ciphertext == null) return row.ValueText;
            string? plain = SecretBoxProvider.For(_Settings.Secrets).Decrypt(row.Ciphertext, row.Nonce, row.KeyId, _PropertyId + "|" + key);
            if (plain == null)
            {
                TrellisKitTelemetry.Increment(TrellisKitTelemetry.SecretDecryptFailures, "key", key);
                _Logger.Warn("[PropertySettingsService] [p:" + _PropertyId + "] the stored value of " + key + " does not decrypt with the current secrets keys");
            }
            return plain;
        }

        /// <summary>
        /// The value of a setting, or its default.
        /// </summary>
        /// <param name="key">Key.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Value or default (empty when neither).</returns>
        public async Task<string> GetOrDefaultAsync(string key, CancellationToken token = default)
        {
            string? value = await GetAsync(key, token).ConfigureAwait(false);
            if (!String.IsNullOrEmpty(value)) return value;
            return PropertySettingsRegistry.Find(key)?.Default ?? String.Empty;
        }

        /// <summary>
        /// GET /api/v1/admin/property/settings.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Fields (secrets never returned).</returns>
        public async Task<PropertySettingsResponse> ReadAsync(CancellationToken token = default)
        {
            Dictionary<string, PropertySettingRow> rows = await RowsAsync(token).ConfigureAwait(false);
            PropertySettingsResponse response = new PropertySettingsResponse { PropertyId = _PropertyId };
            bool single = _Settings.Deployment.Mode == DeploymentModeEnum.SingleProperty;
            foreach (PropertySettingDefinition d in PropertySettingsRegistry.All)
            {
                rows.TryGetValue(d.Key, out PropertySettingRow? row);
                bool secret = d.Type == SettingTypeEnum.Secret;
                string? deployment = single ? DeploymentValue(d.Key) : null;
                PropertySettingField field = new PropertySettingField
                {
                    Key = d.Key,
                    Section = d.Section,
                    Label = d.Label,
                    Description = d.Description,
                    Type = d.Type,
                    Options = d.Options,
                    Min = d.Min,
                    Max = d.Max,
                    ApplyMode = SettingApplyModeEnum.Live,
                    Scope = "Property",
                    Inherited = row == null && !String.IsNullOrEmpty(deployment),
                    IsSet = secret ? row != null || !String.IsNullOrEmpty(deployment) : null
                };
                if (!secret) field.Value = Typed(d, row?.ValueText ?? (String.IsNullOrEmpty(deployment) ? d.Default : deployment));
                response.Fields.Add(field);
            }
            return response;
        }

        /// <summary>
        /// PUT /api/v1/admin/property/settings: all or nothing; secrets are encrypted; audited property.settings.update
        /// (key names only).
        /// </summary>
        /// <param name="request">Values and keys to clear.</param>
        /// <param name="info">Request (audit).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The settings after the change.</returns>
        /// <exception cref="ApiErrorException">400 ValidationFailed naming each invalid key.</exception>
        public async Task<PropertySettingsResponse> UpdateAsync(PropertySettingsUpdateRequest request, RequestInfo info, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            ArgumentNullException.ThrowIfNull(info);
            Dictionary<string, string> errors = new Dictionary<string, string>();
            List<PropertySettingRow> upserts = new List<PropertySettingRow>();
            List<string> deletes = new List<string>();
            SecretBox box = SecretBoxProvider.For(_Settings.Secrets);
            DateTime now = DateTime.UtcNow;
            foreach (KeyValuePair<string, JsonElement> pair in request.Values)
            {
                PropertySettingDefinition? d = PropertySettingsRegistry.Find(pair.Key);
                if (d == null)
                {
                    errors["values." + pair.Key] = "Unknown setting.";
                    continue;
                }
                if (pair.Value.ValueKind == JsonValueKind.Null || (pair.Value.ValueKind == JsonValueKind.String && String.IsNullOrWhiteSpace(pair.Value.GetString())))
                {
                    deletes.Add(d.Key);
                    continue;
                }
                string? text = d.Normalize(pair.Value, out string? error);
                if (text == null)
                {
                    errors["values." + d.Key] = error ?? "Invalid value.";
                    continue;
                }
                PropertySettingRow row = new PropertySettingRow { Key = d.Key, UpdatedByUserId = info.Context?.UserId, UpdatedUtc = now };
                if (d.Type == SettingTypeEnum.Secret)
                {
                    SecretCiphertext cipher = box.Encrypt(text, _PropertyId + "|" + d.Key);
                    row.Ciphertext = cipher.Ciphertext;
                    row.Nonce = cipher.Nonce;
                    row.KeyId = cipher.KeyId;
                }
                else row.ValueText = text;
                upserts.Add(row);
            }
            foreach (string key in request.Clear)
            {
                PropertySettingDefinition? d = PropertySettingsRegistry.Find(key);
                if (d == null) errors["clear"] = "Unknown setting " + key + ".";
                else deletes.Add(d.Key);
            }
            if (errors.Count > 0) throw ApiErrorException.Validation(errors, "One or more settings are invalid.");
            await _Database.PropertySettings.WriteAsync(upserts, deletes.Distinct(StringComparer.Ordinal).Where(k => !upserts.Any(u => u.Key == k)).ToList(), token).ConfigureAwait(false);
            _Rows = null;
            List<string> keys = upserts.Select(u => u.Key).Concat(deletes).Distinct(StringComparer.Ordinal).OrderBy(k => k, StringComparer.Ordinal).ToList();
            if (keys.Count > 0)
            {
                await _Audit.WriteAsync(info, "property.settings.update", "Property", _PropertyId, "Changed " + String.Join(", ", keys), token).ConfigureAwait(false);
                TrellisKitTelemetry.Increment(TrellisKitTelemetry.SettingsChanges, "scope", "property");
            }
            Changed?.Invoke();
            return await ReadAsync(token).ConfigureAwait(false);
        }

        /// <summary>
        /// Drop the cache.
        /// </summary>
        public void Invalidate()
        {
            _Rows = null;
        }

        #endregion

        #region Private-Methods

        private async Task<Dictionary<string, PropertySettingRow>> RowsAsync(CancellationToken token)
        {
            Dictionary<string, PropertySettingRow>? rows = _Rows;
            if (rows != null && (DateTime.UtcNow - _LoadedUtc).TotalSeconds < 15) return rows;
            List<PropertySettingRow> list = await _Database.PropertySettings.ReadAllAsync(token).ConfigureAwait(false);
            rows = list.ToDictionary(r => r.Key, StringComparer.Ordinal);
            _Rows = rows;
            _LoadedUtc = DateTime.UtcNow;
            return rows;
        }

        private string? DeploymentValue(string key)
        {
            // SingleProperty only: the deployment's SMTP and sender settings are the one property's defaults.
            EmailSettings e = _Settings.Email;
            switch (key)
            {
                case "email.smtpHost": return e.SmtpHost;
                case "email.smtpPort": return String.IsNullOrWhiteSpace(e.SmtpHost) ? null : e.SmtpPort.ToString(CultureInfo.InvariantCulture);
                case "email.smtpUsername": return e.Username;
                case "email.smtpPassword": return e.Password;
                case "email.smtpUseTls": return String.IsNullOrWhiteSpace(e.SmtpHost) ? null : (e.UseTls ? "true" : "false");
                case "email.fromName": return e.FromName;
                case "email.fromAddress": return e.FromAddress;
                default: return null;
            }
        }

        private static object? Typed(PropertySettingDefinition d, string? text)
        {
            if (String.IsNullOrEmpty(text)) return d.Type == SettingTypeEnum.Boolean ? false : d.Type == SettingTypeEnum.Integer ? null : String.Empty;
            if (d.Type == SettingTypeEnum.Boolean) return String.Equals(text, "true", StringComparison.OrdinalIgnoreCase);
            if (d.Type == SettingTypeEnum.Integer && Int64.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out long n)) return n;
            return text;
        }

        #endregion
    }
}
