namespace TrellisKit.Server.Services
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Exceptions;
    using TrellisKit.Core.Observability;
    using TrellisKit.Core.Requests;
    using TrellisKit.Core.Responses;
    using TrellisKit.Core.Settings;
    using TrellisKit.Server.Settings;

    /// <summary>
    /// Reads and updates runtime settings. Reads reflect the settings file plus environment overrides; updates are
    /// validated, written to the file, and applied to the running server when the field is live.
    /// Thread safety: updates are serialized.
    /// </summary>
    public class SettingsService : IDisposable
    {
        #region Public-Members

        /// <summary>
        /// Listener settings of the running instance (live changes apply).
        /// </summary>
        public RestSettings Rest
        {
            get { return _Running.Rest; }
        }

        #endregion

        #region Private-Members

        private readonly AppSettings _Running;
        private readonly SettingsStore _Store;
        private readonly AppLogger _Logger;
        private readonly SemaphoreSlim _UpdateLock = new SemaphoreSlim(1, 1);
        private bool _Disposed = false;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the service.
        /// </summary>
        /// <param name="running">Settings used by the running server; live changes are applied here.</param>
        /// <param name="store">Settings store.</param>
        /// <param name="logger">Logger.</param>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        public SettingsService(AppSettings running, SettingsStore store, AppLogger logger)
        {
            _Running = running ?? throw new ArgumentNullException(nameof(running));
            _Store = store ?? throw new ArgumentNullException(nameof(store));
            _Logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Describe every editable setting with its configured value. Secret values are never included.
        /// </summary>
        /// <returns>Settings response.</returns>
        /// <exception cref="InvalidOperationException">The settings file is invalid.</exception>
        public TrellisKit.Core.Responses.EnumerationResult<SettingField> Enumerate(TrellisKit.Core.Requests.EnumerationQuery query, string scope = "deployment")
        {
            TrellisKit.Core.Enumeration.EnumerationRequest request = new EnumerationService(_Running).Prepare(query, TrellisKit.Core.Enumeration.AdministrativeCatalogs.Settings, true, scope);
            return TrellisKit.Core.Enumeration.CatalogEnumeration.Page(request, Describe().Fields, field => field.Key, search: field => field.Key + " " + field.Section + " " + field.Label + " " + field.Description);
        }

        /// <summary>Describe every editable setting for a mutation response.</summary>
        public SettingsResponse Describe()
        {
            AppSettings configured = _Store.LoadFile();
            HashSet<string> overridden = _Store.ApplyEnvironment(configured);

            SettingsResponse response = new SettingsResponse();
            foreach (SettingFieldDefinition definition in SettingsRegistry.All)
            {
                bool isSecret = definition.Type == SettingTypeEnum.Secret;
                object? value = definition.GetValue(configured);
                response.Fields.Add(new SettingField
                {
                    Key = definition.Key,
                    Section = definition.Section,
                    Label = definition.Label,
                    Description = definition.Description,
                    Type = definition.Type,
                    Value = isSecret ? null : value,
                    Options = definition.Options,
                    Min = definition.Min,
                    Max = definition.Max,
                    MinDecimal = definition.MinDecimal,
                    MaxDecimal = definition.MaxDecimal,
                    ApplyMode = definition.ApplyMode,
                    OverriddenByEnvironment = overridden.Contains(definition.Key),
                    EnvironmentVariable = definition.EnvironmentVariable,
                    IsSet = isSecret ? !String.IsNullOrEmpty(value as string) : null
                });
            }
            return response;
        }

        /// <summary>
        /// Validate and save changed settings, applying live fields immediately.
        /// </summary>
        /// <param name="request">Changed values keyed by setting key.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The updated settings description.</returns>
        /// <exception cref="ArgumentNullException">The request is null.</exception>
        /// <exception cref="SettingsValidationException">One or more values are invalid; nothing was saved.</exception>
        public async Task<SettingsResponse> UpdateAsync(SettingsUpdateRequest request, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            await _UpdateLock.WaitAsync(token).ConfigureAwait(false);
            try
            {
                AppSettings fileSettings = _Store.LoadFile();
                AppSettings probe = _Store.LoadFile();
                HashSet<string> overridden = _Store.ApplyEnvironment(probe);

                Dictionary<string, string> errors = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                List<SettingFieldDefinition> changed = new List<SettingFieldDefinition>();
                Dictionary<string, JsonElement> accepted = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);

                foreach (KeyValuePair<string, JsonElement> pair in request.Values)
                {
                    SettingFieldDefinition? definition = SettingsRegistry.Find(pair.Key);
                    if (definition == null)
                    {
                        errors[pair.Key] = "Unknown setting.";
                        continue;
                    }
                    if (overridden.Contains(definition.Key))
                    {
                        errors[definition.Key] = "Set by environment variable " + definition.EnvironmentVariable + "; change it there.";
                        continue;
                    }

                    try
                    {
                        definition.SetValue(fileSettings, pair.Value);
                        changed.Add(definition);
                        accepted[definition.Key] = pair.Value;
                    }
                    catch (ArgumentException e)
                    {
                        errors[definition.Key] = CleanMessage(e);
                    }
                }

                if (errors.Count > 0) throw new SettingsValidationException(errors);
                if (changed.Count == 0) return Describe();

                _Store.Save(fileSettings);

                foreach (SettingFieldDefinition definition in changed.Where(d => d.ApplyMode == SettingApplyModeEnum.Live))
                {
                    definition.SetValue(_Running, accepted[definition.Key]);
                }

                foreach (SettingFieldDefinition definition in changed)
                {
                    TrellisKitTelemetry.Increment(TrellisKitTelemetry.SettingsChanges, "apply_mode", definition.ApplyMode.ToString().ToLowerInvariant());
                }

                _Logger.Info("[SettingsService] settings saved: " + String.Join(", ", changed.Select(d => d.Key + " (" + d.ApplyMode + ")")));
                return Describe();
            }
            finally
            {
                _UpdateLock.Release();
            }
        }

        /// <summary>
        /// Dispose the service.
        /// </summary>
        public void Dispose()
        {
            if (_Disposed) return;
            _Disposed = true;
            _UpdateLock.Dispose();
            GC.SuppressFinalize(this);
        }

        #endregion

        #region Private-Methods

        private static string CleanMessage(ArgumentException e)
        {
            string message = e.Message;
            int index = message.IndexOf(" (Parameter", StringComparison.Ordinal);
            return index > 0 ? message.Substring(0, index) : message;
        }

        #endregion
    }
}
