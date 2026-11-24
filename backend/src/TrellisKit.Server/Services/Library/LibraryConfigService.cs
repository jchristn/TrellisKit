namespace TrellisKit.Server.Services
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;
    using System.Security.Cryptography;
    using System.Text;
    using System.Text.Json;
    using System.Text.RegularExpressions;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core.Enumeration;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Exceptions;
    using TrellisKit.Core.Helpers;
    using TrellisKit.Core.Models;
    using TrellisKit.Core.PropertyConfig;
    using TrellisKit.Core.Requests;
    using TrellisKit.Core.Responses;

    /// <summary>
    /// The library configuration of a property (docs/PHASE4_CONTRACT.md sections 0.2 and 5.10): seeding from
    /// property.json section library (in SingleProperty the folder wins when the section changes on disk, like profile
    /// fields), levels from the configured source (None, ProfileField, Custom), per-property overrides of the library
    /// behavior settings, and the library section of GET /api/v1/property.
    /// Thread safety: safe to call concurrently (the cached configuration is replaced atomically).
    /// </summary>
    public class LibraryConfigService
    {
        #region Public-Members

        /// <summary>
        /// Most levels a property may define (fixed bound).
        /// </summary>
        public const int MaxLevels = 50;

        /// <summary>
        /// Most levels on one series or lesson (fixed bound).
        /// </summary>
        public const int MaxLevelsPerObject = 10;

        /// <summary>
        /// How long the cached configuration is reused before it is read again, in seconds. Default 15. Minimum 0,
        /// maximum 600.
        /// </summary>
        public int CacheSeconds
        {
            get { return _CacheSeconds; }
            set { _CacheSeconds = Math.Clamp(value, 0, 600); }
        }

        /// <summary>
        /// The honest limits of offline downloads, shown where downloads are enabled.
        /// </summary>
        public const string DownloadsNotice = "Downloaded lessons are not DRM protected: a determined user with a jailbroken or rooted device can copy a downloaded file. Your terms of use should forbid redistribution.";

        #endregion

        #region Private-Members

        private static readonly Regex _Key = new Regex("^[A-Za-z0-9_-]{1,40}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
        private static readonly Regex _Color = new Regex("^#[0-9A-Fa-f]{6}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
        private readonly ApplicationServices _Services;
        private readonly SemaphoreSlim _SeedLock = new SemaphoreSlim(1, 1);
        private LibraryConfig? _Cached = null;
        private DateTime _CachedUtc = DateTime.MinValue;
        private int _Seeded = 0;
        private int _CacheSeconds = 15;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="services">Property services.</param>
        /// <exception cref="ArgumentNullException">The services are null.</exception>
        public LibraryConfigService(ApplicationServices services)
        {
            _Services = services ?? throw new ArgumentNullException(nameof(services));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// The current configuration, seeding it from property.json on first use.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Configuration.</returns>
        public async Task<LibraryConfig> CurrentAsync(CancellationToken token = default)
        {
            LibraryConfig? cached = Volatile.Read(ref _Cached);
            if (cached != null && (DateTime.UtcNow - _CachedUtc).TotalSeconds < _CacheSeconds) return cached;
            await EnsureSeededAsync(token).ConfigureAwait(false);
            LibraryConfig config = await _Services.Database.Library.Catalog.ReadConfigAsync(token).ConfigureAwait(false) ?? new LibraryConfig();
            _CachedUtc = DateTime.UtcNow;
            Volatile.Write(ref _Cached, config);
            return config;
        }

        /// <summary>
        /// Drop the cached configuration (after a write).
        /// </summary>
        public void Invalidate()
        {
            Volatile.Write(ref _Cached, null);
        }

        /// <summary>
        /// Seed libraryconfig and librarylevels from property.json section library: when the property has no row, and
        /// in SingleProperty whenever the section changed on disk since the last seed (the folder wins).
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True when this call wrote the configuration.</returns>
        public async Task<bool> EnsureSeededAsync(CancellationToken token = default)
        {
            if (Volatile.Read(ref _Seeded) != 0) return false;
            await _SeedLock.WaitAsync(token).ConfigureAwait(false);
            try
            {
                if (Volatile.Read(ref _Seeded) != 0) return false;
                PropertyLibrarySection section = _Services.Property.Property.LibrarySection ?? new PropertyLibrarySection();
                string hash = SectionHash(section);
                LibraryConfig? existing = await _Services.Database.Library.Catalog.ReadConfigAsync(token).ConfigureAwait(false);
                bool single = _Services.Settings.Deployment.Mode == DeploymentModeEnum.SingleProperty;
                bool write = existing == null || (single && !String.Equals(existing.SourceHash, hash, StringComparison.Ordinal));
                if (write)
                {
                    DateTime now = DateTime.UtcNow;
                    LibraryConfig config = existing ?? new LibraryConfig();
                    config.LevelSource = ModerationService.TryParseEnum(section.LevelSource, out LibraryLevelSourceEnum source) ? source : LibraryLevelSourceEnum.None;
                    config.LevelFieldKey = String.IsNullOrWhiteSpace(section.LevelFieldKey) ? null : section.LevelFieldKey.Trim();
                    config.SourceHash = hash;
                    config.UpdatedUtc = now;
                    await _Services.Database.Library.Catalog.WriteConfigAsync(config, token).ConfigureAwait(false);
                    List<LibraryLevel> levels = new List<LibraryLevel>();
                    HashSet<string> keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    int order = 10;
                    foreach (PropertyOptionSeed seed in (section.Levels ?? new List<PropertyOptionSeed>()).Take(MaxLevels))
                    {
                        if (seed == null || String.IsNullOrWhiteSpace(seed.Key) || !_Key.IsMatch(seed.Key.Trim()) || !keys.Add(seed.Key.Trim())) continue;
                        levels.Add(new LibraryLevel
                        {
                            Id = IdGenerator.Generate(LibraryIdPrefixes.Level),
                            Key = seed.Key.Trim(),
                            Label = String.IsNullOrWhiteSpace(seed.Label) ? seed.Key.Trim() : Cut(seed.Label.Trim(), 40),
                            Color = seed.Color != null && _Color.IsMatch(seed.Color) ? seed.Color.ToUpperInvariant() : null,
                            SortOrder = order,
                            CreatedUtc = now,
                            UpdatedUtc = now
                        });
                        order += 10;
                    }
                    if (existing == null || levels.Count > 0 || (section.Levels != null)) await _Services.Database.Library.Catalog.ReplaceLevelsAsync(levels, token).ConfigureAwait(false);
                    Invalidate();
                }
                Interlocked.Exchange(ref _Seeded, 1);
                return write;
            }
            finally
            {
                _SeedLock.Release();
            }
        }

        /// <summary>
        /// The effective library.downloads.enabled.
        /// </summary>
        /// <param name="config">Configuration.</param>
        /// <returns>Value.</returns>
        public bool DownloadsEnabled(LibraryConfig config)
        {
            return config?.DownloadsEnabled ?? _Services.Settings.Library.DownloadsEnabled;
        }

        /// <summary>
        /// The effective library.completionThresholdPercent.
        /// </summary>
        /// <param name="config">Configuration.</param>
        /// <returns>Value.</returns>
        public int CompletionThreshold(LibraryConfig config)
        {
            return Math.Clamp(config?.CompletionThresholdPercent ?? _Services.Settings.Library.CompletionThresholdPercent, 50, 100);
        }

        /// <summary>
        /// The effective library.publicSamplesEnabled.
        /// </summary>
        /// <param name="config">Configuration.</param>
        /// <returns>Value.</returns>
        public bool PublicSamplesEnabled(LibraryConfig config)
        {
            return config?.PublicSamplesEnabled ?? _Services.Settings.Library.PublicSamplesEnabled;
        }

        /// <summary>
        /// The effective library.autoFollowOnProgress.
        /// </summary>
        /// <param name="config">Configuration.</param>
        /// <returns>Value.</returns>
        public bool AutoFollowOnProgress(LibraryConfig config)
        {
            return config?.AutoFollowOnProgress ?? _Services.Settings.Library.AutoFollowOnProgress;
        }

        /// <summary>
        /// The effective library.commentsEnabledByDefault.
        /// </summary>
        /// <param name="config">Configuration.</param>
        /// <returns>Value.</returns>
        public bool CommentsEnabledByDefault(LibraryConfig config)
        {
            return config?.CommentsEnabledByDefault ?? _Services.Settings.Library.CommentsEnabledByDefault;
        }

        /// <summary>
        /// The levels of the configured source, in order.
        /// </summary>
        /// <param name="includeRetired">Include retired Custom levels.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Levels.</returns>
        public async Task<List<LibraryLevelView>> LevelsAsync(bool includeRetired, CancellationToken token = default)
        {
            LibraryConfig config = await CurrentAsync(token).ConfigureAwait(false);
            switch (config.LevelSource)
            {
                case LibraryLevelSourceEnum.Custom:
                    List<LibraryLevel> levels = await _Services.Database.Library.Catalog.ListLevelsAsync(includeRetired, token).ConfigureAwait(false);
                    return levels.Select(l => new LibraryLevelView { Id = l.Id, Key = l.Key, Label = l.Label, Color = l.Color, SortOrder = l.SortOrder, IsRetired = l.IsRetired, CreatedUtc = l.CreatedUtc, UpdatedUtc = l.UpdatedUtc }).ToList();
                case LibraryLevelSourceEnum.ProfileField:
                    if (String.IsNullOrEmpty(config.LevelFieldKey)) return new List<LibraryLevelView>();
                    IReadOnlyList<ProfileField> fields = await _Services.ProfileFields.AllAsync(token).ConfigureAwait(false);
                    ProfileField? field = fields.FirstOrDefault(f => String.Equals(f.Key, config.LevelFieldKey, StringComparison.OrdinalIgnoreCase) &&
                        (f.Type == ProfileFieldTypeEnum.Rank || f.Type == ProfileFieldTypeEnum.SingleSelect));
                    if (field == null) return new List<LibraryLevelView>();
                    int order = 0;
                    return field.Options.Where(o => includeRetired || !o.Retired).Take(200).Select(o => new LibraryLevelView { Key = o.Key, Label = o.Label, Color = o.Color, SortOrder = (order += 10), IsRetired = o.Retired, CreatedUtc = field.CreatedUtc, UpdatedUtc = field.UpdatedUtc }).ToList();
                default:
                    return new List<LibraryLevelView>();
            }
        }

        /// <summary>
        /// The configured levels by key (retired included, so stored keys keep rendering).
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Levels by key.</returns>
        public async Task<Dictionary<string, LibraryLevelView>> LevelMapAsync(CancellationToken token = default)
        {
            List<LibraryLevelView> levels = await LevelsAsync(true, token).ConfigureAwait(false);
            Dictionary<string, LibraryLevelView> map = new Dictionary<string, LibraryLevelView>(StringComparer.Ordinal);
            foreach (LibraryLevelView level in levels) map.TryAdd(level.Key, level);
            return map;
        }

        /// <summary>
        /// Validate level keys of a series or lesson write: at most 10, each configured and not retired.
        /// </summary>
        /// <param name="keys">Keys.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Distinct keys in configured order.</returns>
        /// <exception cref="ApiErrorException">400 UnknownOption or ValidationFailed.</exception>
        public async Task<List<string>> ValidateLevelsAsync(List<string>? keys, CancellationToken token = default)
        {
            List<string> wanted = (keys ?? new List<string>()).Where(k => !String.IsNullOrWhiteSpace(k)).Select(k => k.Trim()).Distinct(StringComparer.Ordinal).ToList();
            if (wanted.Count == 0) return wanted;
            if (wanted.Count > MaxLevelsPerObject) throw ApiErrorException.Field("levels", "At most " + MaxLevelsPerObject + " levels.");
            List<LibraryLevelView> levels = await LevelsAsync(false, token).ConfigureAwait(false);
            List<string> unknown = wanted.Where(k => !levels.Any(l => String.Equals(l.Key, k, StringComparison.Ordinal))).ToList();
            if (unknown.Count > 0)
                throw new ApiErrorException(400, "UnknownOption", "Unknown level: " + String.Join(", ", unknown.Take(5)) + ".", new Dictionary<string, string> { { "levels", "Use the configured level keys." } });
            return levels.Where(l => wanted.Contains(l.Key)).Select(l => l.Key).ToList();
        }

        /// <summary>
        /// POST /api/v1/library/levels/enumerate (member) and the admin level list.
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="query">Query.</param>
        /// <param name="admin">Admin list (SystemAdmin; includeRetired honored).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Page of levels.</returns>
        /// <exception cref="ApiErrorException">400 ValidationFailed, 403 Forbidden.</exception>
        public async Task<EnumerationResult<LibraryLevelView>> EnumerateLevelsAsync(ContentActor actor, EnumerationQuery? query, bool admin, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            if (admin) LibraryAccess.RequireAdmin(actor);
            EnumerationQuery q = query ?? new EnumerationQuery();
            Dictionary<string, string> errors = new Dictionary<string, string>();
            bool includeRetired = admin && (q.FilterBool("includeRetired", errors) ?? false);
            if (errors.Count > 0) throw ApiErrorException.Validation(errors);
            EnumerationRequest request = _Services.Enumeration.Prepare(q, admin ? LibraryCollections.AdminLevels : LibraryCollections.Levels, actor.IsLibraryStaff, "library-levels");
            List<LibraryLevelView> all = await LevelsAsync(includeRetired, token).ConfigureAwait(false);
            List<string> ids = all.Where(l => l.Id != null).Select(l => l.Id!).ToList();
            Dictionary<string, ObjectMetadata> metadata = ids.Count == 0 ? new Dictionary<string, ObjectMetadata>(StringComparer.Ordinal) : await _Services.Metadata.LoadAsync(TargetTypeEnum.LibraryLevel, ids, actor.IsLibraryStaff, token).ConfigureAwait(false);
            foreach (LibraryLevelView level in all)
            {
                if (level.Id == null || !metadata.TryGetValue(level.Id, out ObjectMetadata? m)) continue;
                level.Labels = m.Labels;
                level.Tags = m.Tags;
            }
            return CatalogEnumeration.Page(request, all, level => level.Id ?? "profile:" + level.Key,
                level => level.CreatedUtc, level => level.Key + " " + level.Label, level => level.Labels, level => level.Tags,
                level => request.Ordering.Name == "LabelAscending" ? level.Label : level.SortOrder.ToString(CultureInfo.InvariantCulture));
        }

        /// <summary>
        /// POST /api/v1/admin/library/levels (Custom level source only).
        /// </summary>
        /// <param name="actor">Caller (SystemAdmin).</param>
        /// <param name="info">Request information.</param>
        /// <param name="request">Body.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Level.</returns>
        /// <exception cref="ApiErrorException">400 ValidationFailed, 403 Forbidden, 409 Conflict.</exception>
        public async Task<LibraryLevelView> CreateLevelAsync(ContentActor actor, RequestInfo info, LibraryLevelRequest request, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            ArgumentNullException.ThrowIfNull(info);
            ArgumentNullException.ThrowIfNull(request);
            LibraryAccess.RequireAdmin(actor);
            await RequireCustomAsync(token).ConfigureAwait(false);
            Dictionary<string, string> errors = new Dictionary<string, string>();
            string key = (request.Key ?? String.Empty).Trim();
            if (!_Key.IsMatch(key)) errors["key"] = "Use 1 to 40 of letters, digits, hyphen, and underscore.";
            LibraryLevel level = new LibraryLevel { Id = IdGenerator.Generate(LibraryIdPrefixes.Level), Key = key, CreatedUtc = DateTime.UtcNow, UpdatedUtc = DateTime.UtcNow };
            ApplyLevel(level, request, errors, true);
            if (errors.Count > 0) throw ApiErrorException.Validation(errors);
            List<LibraryLevel> existing = await _Services.Database.Library.Catalog.ListLevelsAsync(true, token).ConfigureAwait(false);
            if (existing.Count >= MaxLevels) throw new ApiErrorException(409, "LevelLimitReached", "A property may define at most " + MaxLevels + " levels.");
            if (request.SortOrder == null) level.SortOrder = existing.Count == 0 ? 10 : existing.Max(l => l.SortOrder) + 10;
            try
            {
                await _Services.Database.Library.Catalog.CreateLevelAsync(level, token).ConfigureAwait(false);
            }
            catch (DuplicateRecordException)
            {
                throw new ApiErrorException(409, "Conflict", "Another level already uses this key.", new Dictionary<string, string> { { "key", "Choose another key." } });
            }
            await _Services.Audit.WriteAsync(info, "library.level.create", TargetTypeEnum.LibraryLevel.ToString(), level.Id, level.Key, token).ConfigureAwait(false);
            return new LibraryLevelView { Id = level.Id, Key = level.Key, Label = level.Label, Color = level.Color, SortOrder = level.SortOrder, IsRetired = level.IsRetired, CreatedUtc = level.CreatedUtc, UpdatedUtc = level.UpdatedUtc };
        }

        /// <summary>
        /// PUT /api/v1/admin/library/levels/{id}: label, color, sort order, retired (the key is fixed).
        /// </summary>
        /// <param name="actor">Caller (SystemAdmin).</param>
        /// <param name="info">Request information.</param>
        /// <param name="id">Level.</param>
        /// <param name="request">Body.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Level.</returns>
        /// <exception cref="ApiErrorException">400 ValidationFailed, 403 Forbidden, 404 NotFound.</exception>
        public async Task<LibraryLevelView> UpdateLevelAsync(ContentActor actor, RequestInfo info, string id, LibraryLevelRequest request, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            ArgumentNullException.ThrowIfNull(info);
            ArgumentNullException.ThrowIfNull(request);
            LibraryAccess.RequireAdmin(actor);
            await RequireCustomAsync(token).ConfigureAwait(false);
            LibraryLevel level = await _Services.Database.Library.Catalog.ReadLevelAsync((id ?? String.Empty).Trim(), token).ConfigureAwait(false) ?? throw ApiErrorException.NotFound("Level");
            Dictionary<string, string> errors = new Dictionary<string, string>();
            if (request.Key != null && !String.Equals(request.Key.Trim(), level.Key, StringComparison.Ordinal)) errors["key"] = "The key cannot change.";
            ApplyLevel(level, request, errors, false);
            if (errors.Count > 0) throw ApiErrorException.Validation(errors);
            level.UpdatedUtc = DateTime.UtcNow;
            await _Services.Database.Library.Catalog.UpdateLevelAsync(level, token).ConfigureAwait(false);
            await _Services.Audit.WriteAsync(info, "library.level.update", TargetTypeEnum.LibraryLevel.ToString(), level.Id, level.Key, token).ConfigureAwait(false);
            return new LibraryLevelView { Id = level.Id, Key = level.Key, Label = level.Label, Color = level.Color, SortOrder = level.SortOrder, IsRetired = level.IsRetired, CreatedUtc = level.CreatedUtc, UpdatedUtc = level.UpdatedUtc };
        }

        /// <summary>
        /// DELETE /api/v1/admin/library/levels/{id} (existing objects keep the key; it renders as the key).
        /// </summary>
        /// <param name="actor">Caller (SystemAdmin).</param>
        /// <param name="info">Request information.</param>
        /// <param name="id">Level.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        /// <exception cref="ApiErrorException">403 Forbidden, 404 NotFound.</exception>
        public async Task DeleteLevelAsync(ContentActor actor, RequestInfo info, string id, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            ArgumentNullException.ThrowIfNull(info);
            LibraryAccess.RequireAdmin(actor);
            await RequireCustomAsync(token).ConfigureAwait(false);
            LibraryLevel level = await _Services.Database.Library.Catalog.ReadLevelAsync((id ?? String.Empty).Trim(), token).ConfigureAwait(false) ?? throw ApiErrorException.NotFound("Level");
            await _Services.Database.Library.Catalog.DeleteLevelAsync(level.Id, token).ConfigureAwait(false);
            await _Services.Audit.WriteAsync(info, "library.level.delete", TargetTypeEnum.LibraryLevel.ToString(), level.Id, level.Key, token).ConfigureAwait(false);
        }

        /// <summary>
        /// PUT /api/v1/admin/library/levels/order ({ levelIds }): every custom level exactly once; sort orders become
        /// 10, 20, 30.
        /// </summary>
        /// <param name="actor">Caller (SystemAdmin).</param>
        /// <param name="info">Request information.</param>
        /// <param name="request">Body.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Rows reordered.</returns>
        /// <exception cref="ApiErrorException">400 ValidationFailed, 403 Forbidden, 409 LevelSourceNotCustom.</exception>
        public async Task<LibraryOrderResponse> OrderLevelsAsync(ContentActor actor, RequestInfo info, LibraryOrderRequest request, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            ArgumentNullException.ThrowIfNull(info);
            ArgumentNullException.ThrowIfNull(request);
            LibraryAccess.RequireAdmin(actor);
            await RequireCustomAsync(token).ConfigureAwait(false);
            List<string> ids = (request.LevelIds ?? new List<string>()).Select(i => (i ?? String.Empty).Trim()).ToList();
            List<LibraryLevel> levels = await _Services.Database.Library.Catalog.ListLevelsAsync(true, token).ConfigureAwait(false);
            if (ids.Distinct(StringComparer.Ordinal).Count() != ids.Count || ids.Count != levels.Count || ids.Any(i => !levels.Any(l => l.Id == i)))
                throw ApiErrorException.Field("levelIds", "List every level exactly once (" + levels.Count + " levels).");
            DateTime now = DateTime.UtcNow;
            for (int i = 0; i < ids.Count; i++)
            {
                LibraryLevel level = levels.First(l => l.Id == ids[i]);
                level.SortOrder = (i + 1) * 10;
                level.UpdatedUtc = now;
                await _Services.Database.Library.Catalog.UpdateLevelAsync(level, token).ConfigureAwait(false);
            }
            await _Services.Audit.WriteAsync(info, "library.level.order", TargetTypeEnum.LibraryLevel.ToString(), null, ids.Count + " levels", token).ConfigureAwait(false);
            return new LibraryOrderResponse { Updated = ids.Count };
        }

        /// <summary>
        /// GET /api/v1/admin/library/settings.
        /// </summary>
        /// <param name="actor">Caller (library staff read; SystemAdmin writes).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Settings.</returns>
        /// <exception cref="ApiErrorException">403 Forbidden.</exception>
        public async Task<LibrarySettingsView> GetSettingsAsync(ContentActor actor, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            LibraryAccess.RequireStaff(actor);
            LibraryConfig config = await CurrentAsync(token).ConfigureAwait(false);
            return SettingsView(config);
        }

        /// <summary>
        /// PUT /api/v1/admin/library/settings (SystemAdmin): level source and the per-property overrides.
        /// </summary>
        /// <param name="actor">Caller (SystemAdmin).</param>
        /// <param name="info">Request information.</param>
        /// <param name="request">Body.</param>
        /// <param name="present">Fields present in the body (a present null clears an override).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Settings.</returns>
        /// <exception cref="ApiErrorException">400 ValidationFailed, 403 Forbidden.</exception>
        public async Task<LibrarySettingsView> UpdateSettingsAsync(ContentActor actor, RequestInfo info, LibrarySettingsRequest request, HashSet<string> present, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            ArgumentNullException.ThrowIfNull(info);
            ArgumentNullException.ThrowIfNull(request);
            ArgumentNullException.ThrowIfNull(present);
            LibraryAccess.RequireAdmin(actor);
            await EnsureSeededAsync(token).ConfigureAwait(false);
            LibraryConfig config = await _Services.Database.Library.Catalog.ReadConfigAsync(token).ConfigureAwait(false) ?? new LibraryConfig();
            Dictionary<string, string> errors = new Dictionary<string, string>();
            if (present.Contains("levelSource") && request.LevelSource != null) config.LevelSource = request.LevelSource.Value;
            if (present.Contains("levelFieldKey")) config.LevelFieldKey = String.IsNullOrWhiteSpace(request.LevelFieldKey) ? null : request.LevelFieldKey.Trim();
            if (config.LevelSource == LibraryLevelSourceEnum.ProfileField)
            {
                IReadOnlyList<ProfileField> fields = await _Services.ProfileFields.AllAsync(token).ConfigureAwait(false);
                if (String.IsNullOrEmpty(config.LevelFieldKey) || !fields.Any(f => String.Equals(f.Key, config.LevelFieldKey, StringComparison.OrdinalIgnoreCase) && (f.Type == ProfileFieldTypeEnum.Rank || f.Type == ProfileFieldTypeEnum.SingleSelect)))
                    errors["levelFieldKey"] = "Use the key of a Rank or SingleSelect profile field.";
            }
            if (present.Contains("downloadsEnabled")) config.DownloadsEnabled = request.DownloadsEnabled;
            if (present.Contains("completionThresholdPercent"))
            {
                if (request.CompletionThresholdPercent != null && (request.CompletionThresholdPercent < 50 || request.CompletionThresholdPercent > 100)) errors["completionThresholdPercent"] = "Use 50 to 100, or null for the platform setting.";
                else config.CompletionThresholdPercent = request.CompletionThresholdPercent;
            }
            if (present.Contains("publicSamplesEnabled")) config.PublicSamplesEnabled = request.PublicSamplesEnabled;
            if (present.Contains("autoFollowOnProgress")) config.AutoFollowOnProgress = request.AutoFollowOnProgress;
            if (present.Contains("commentsEnabledByDefault")) config.CommentsEnabledByDefault = request.CommentsEnabledByDefault;
            if (errors.Count > 0) throw ApiErrorException.Validation(errors);
            config.UpdatedUtc = DateTime.UtcNow;
            await _Services.Database.Library.Catalog.WriteConfigAsync(config, token).ConfigureAwait(false);
            Invalidate();
            await _Services.Audit.WriteAsync(info, "library.settings.update", "Library", null, "levelSource " + config.LevelSource, token).ConfigureAwait(false);
            return SettingsView(await CurrentAsync(token).ConfigureAwait(false));
        }

        /// <summary>
        /// Replace the library section of GET /api/v1/property with the database copy.
        /// </summary>
        /// <param name="response">Response.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        public async Task DecorateAsync(PublicPropertyResponse response, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(response);
            LibraryConfig config = await CurrentAsync(token).ConfigureAwait(false);
            List<LibraryLevelView> levels = await LevelsAsync(false, token).ConfigureAwait(false);
            response.Library = new PublicLibraryConfig
            {
                LevelSource = config.LevelSource.ToString(),
                LevelFieldKey = config.LevelFieldKey,
                Levels = levels.Select(l => new PublicOption { Key = l.Key, Label = l.Label, Color = l.Color }).ToList(),
                DownloadsEnabled = DownloadsEnabled(config),
                ProgressIntervalSeconds = _Services.Settings.Library.ProgressIntervalSeconds,
                CompletionThresholdPercent = CompletionThreshold(config),
                ResumeTailSeconds = _Services.Settings.Library.ResumeTailSeconds,
                PublicSamplesEnabled = PublicSamplesEnabled(config)
            };
        }

        /// <summary>
        /// Hash of a property.json library section (folder change detection).
        /// </summary>
        /// <param name="section">Section.</param>
        /// <returns>Hex SHA-256.</returns>
        public static string SectionHash(PropertyLibrarySection section)
        {
            ArgumentNullException.ThrowIfNull(section);
            string json = JsonSerializer.Serialize(section);
            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json))).ToLowerInvariant();
        }

        #endregion

        #region Private-Methods

        private LibrarySettingsView SettingsView(LibraryConfig config)
        {
            return new LibrarySettingsView
            {
                LevelSource = config.LevelSource,
                LevelFieldKey = config.LevelFieldKey,
                DownloadsEnabled = DownloadsEnabled(config),
                CompletionThresholdPercent = CompletionThreshold(config),
                PublicSamplesEnabled = PublicSamplesEnabled(config),
                AutoFollowOnProgress = AutoFollowOnProgress(config),
                CommentsEnabledByDefault = CommentsEnabledByDefault(config),
                DownloadsNotice = DownloadsNotice,
                UpdatedUtc = config.UpdatedUtc
            };
        }

        private async Task RequireCustomAsync(CancellationToken token)
        {
            LibraryConfig config = await CurrentAsync(token).ConfigureAwait(false);
            if (config.LevelSource != LibraryLevelSourceEnum.Custom)
                throw new ApiErrorException(409, "LevelSourceNotCustom", "Levels are managed here only when library.levelSource is Custom (it is " + config.LevelSource + ").");
        }

        private static void ApplyLevel(LibraryLevel level, LibraryLevelRequest request, Dictionary<string, string> errors, bool creating)
        {
            if (request.Label != null || creating)
            {
                string label = (request.Label ?? String.Empty).Trim();
                if (label.Length < 1 || label.Length > 40) errors["label"] = "Use 1 to 40 characters.";
                else level.Label = label;
            }
            if (request.Color != null)
            {
                string color = request.Color.Trim();
                if (color.Length == 0) level.Color = null;
                else if (!_Color.IsMatch(color)) errors["color"] = "Use #RRGGBB.";
                else level.Color = color.ToUpperInvariant();
            }
            if (request.SortOrder != null) level.SortOrder = Math.Clamp(request.SortOrder.Value, 0, 1000000);
            if (request.IsRetired != null) level.IsRetired = request.IsRetired.Value;
        }

        private static string Cut(string value, int max)
        {
            return value.Length > max ? value.Substring(0, max) : value;
        }

        #endregion
    }
}
