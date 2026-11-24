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
    using TrellisKit.Core;
    using TrellisKit.Core.Enumeration;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Exceptions;
    using TrellisKit.Core.Helpers;
    using TrellisKit.Core.Models;
    using TrellisKit.Core.PropertyConfig;
    using TrellisKit.Core.Requests;
    using TrellisKit.Core.Responses;
    using TrellisKit.Server.Settings;

    /// <summary>
    /// The directory configuration of a property (docs/PHASE4_CONTRACT.md sections 0.2, 3.6, and 3.9): labels, switches,
    /// and programs seeded from the property.json directory section (in SingleProperty the folder wins when the section
    /// changes on disk), the per-property map and geocoder settings, the admin settings route, and the directory section
    /// of GET /api/v1/property. Thread safe; the configuration is cached until a change.
    /// </summary>
    public class DirectoryConfigService
    {
        #region Public-Members

        /// <summary>
        /// Fixed bound of the program list.
        /// </summary>
        public const int ProgramBound = 200;

        #endregion

        #region Private-Members

        private static readonly Regex _Key = new Regex("^[A-Za-z0-9][A-Za-z0-9_-]{0,39}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
        private readonly ApplicationServices _Services;
        private readonly SemaphoreSlim _SeedLock = new SemaphoreSlim(1, 1);
        private DirectoryConfig? _Config = null;
        private List<DirectoryProgram>? _Programs = null;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="services">Property services.</param>
        /// <exception cref="ArgumentNullException">The services are null.</exception>
        public DirectoryConfigService(ApplicationServices services)
        {
            _Services = services ?? throw new ArgumentNullException(nameof(services));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// The configuration (seeded on first use).
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Configuration.</returns>
        public async Task<DirectoryConfig> CurrentAsync(CancellationToken token = default)
        {
            DirectoryConfig? cached = _Config;
            if (cached != null) return cached;
            await EnsureSeededAsync(token).ConfigureAwait(false);
            DirectoryConfig config = await _Services.Database.DirectoryListings.Config.ReadAsync(token).ConfigureAwait(false) ?? FromSection(Section(), null);
            _Config = config;
            return config;
        }

        /// <summary>
        /// Every program option including retired ones, in sort order.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Programs.</returns>
        public async Task<List<DirectoryProgram>> ProgramsAsync(CancellationToken token = default)
        {
            List<DirectoryProgram>? cached = _Programs;
            if (cached != null) return cached;
            await EnsureSeededAsync(token).ConfigureAwait(false);
            List<DirectoryProgram> programs = await _Services.Database.DirectoryListings.Config.ProgramsAsync(true, token).ConfigureAwait(false);
            _Programs = programs;
            return programs;
        }

        /// <summary>
        /// Drop the cache.
        /// </summary>
        public void Invalidate()
        {
            _Config = null;
            _Programs = null;
        }

        /// <summary>
        /// Seed the configuration and programs from property.json on the first load; in SingleProperty, merge the section
        /// again when it changed on disk (new program keys are created, existing keys take the file's label; programs
        /// missing from the file stay).
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        public async Task EnsureSeededAsync(CancellationToken token = default)
        {
            if (_Config != null) return;
            await _SeedLock.WaitAsync(token).ConfigureAwait(false);
            try
            {
                PropertyDirectorySection section = Section();
                string hash = Hash(section);
                DirectoryConfig? existing = await _Services.Database.DirectoryListings.Config.ReadAsync(token).ConfigureAwait(false);
                bool folderWins = _Services.Settings.Deployment.Mode == DeploymentModeEnum.SingleProperty;
                if (existing != null && (existing.SourceHash == hash || !folderWins)) return;
                DateTime now = DateTime.UtcNow;
                DirectoryConfig config = FromSection(section, hash);
                config.UpdatedUtc = now;
                await _Services.Database.DirectoryListings.Config.WriteAsync(config, token).ConfigureAwait(false);
                List<DirectoryProgram> programs = await _Services.Database.DirectoryListings.Config.ProgramsAsync(true, token).ConfigureAwait(false);
                int order = programs.Count == 0 ? 0 : programs.Max(p => p.SortOrder);
                foreach (PropertyOptionSeed seed in (section.Programs ?? new List<PropertyOptionSeed>()).Where(o => o != null).Take(_Services.Settings.Directory.MaxPrograms))
                {
                    string key = (seed.Key ?? String.Empty).Trim();
                    string label = (seed.Label ?? String.Empty).Trim();
                    if (!_Key.IsMatch(key) || label.Length == 0 || label.Length > 60) continue;
                    DirectoryProgram? current = programs.FirstOrDefault(p => String.Equals(p.Key, key, StringComparison.OrdinalIgnoreCase));
                    if (current == null)
                    {
                        order += 10;
                        DirectoryProgram created = new DirectoryProgram { Id = IdGenerator.Generate(DirectoryIdPrefixes.Program), Key = key, Label = label, SortOrder = order, CreatedUtc = now, UpdatedUtc = now };
                        try
                        {
                            programs.Add(await _Services.Database.DirectoryListings.Config.CreateProgramAsync(created, token).ConfigureAwait(false));
                        }
                        catch (DuplicateRecordException)
                        {
                        }
                    }
                    else if (existing != null && !String.Equals(current.Label, label, StringComparison.Ordinal))
                    {
                        current.Label = label;
                        current.UpdatedUtc = now;
                        await _Services.Database.DirectoryListings.Config.UpdateProgramAsync(current, token).ConfigureAwait(false);
                    }
                }
                Invalidate();
            }
            finally
            {
                _SeedLock.Release();
            }
        }

        /// <summary>
        /// The Rank profile field instructors carry, or null.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Field, or null when not configured or not a Rank field.</returns>
        public async Task<ProfileField?> RankFieldAsync(CancellationToken token = default)
        {
            DirectoryConfig config = await CurrentAsync(token).ConfigureAwait(false);
            if (String.IsNullOrWhiteSpace(config.InstructorRankFieldKey)) return null;
            IReadOnlyList<ProfileField> fields = await _Services.ProfileFields.AllAsync(token).ConfigureAwait(false);
            return fields.FirstOrDefault(f => f.Type == ProfileFieldTypeEnum.Rank && f.Enabled && String.Equals(f.Key, config.InstructorRankFieldKey, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// A per-property setting as text, or its default.
        /// </summary>
        /// <param name="key">Key.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Value.</returns>
        public Task<string> SettingAsync(string key, CancellationToken token = default)
        {
            return _Services.PropertySettings.GetOrDefaultAsync(key, token);
        }

        /// <summary>
        /// A boolean per-property setting.
        /// </summary>
        /// <param name="key">Key.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Value.</returns>
        public async Task<bool> FlagAsync(string key, CancellationToken token = default)
        {
            return String.Equals(await SettingAsync(key, token).ConfigureAwait(false), "true", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// An integer per-property setting.
        /// </summary>
        /// <param name="key">Key.</param>
        /// <param name="fallback">Value when unset or invalid.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Value.</returns>
        public async Task<int> IntegerAsync(string key, int fallback, CancellationToken token = default)
        {
            string text = await SettingAsync(key, token).ConfigureAwait(false);
            return Int32.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) ? value : fallback;
        }

        /// <summary>
        /// The effective map configuration without secrets (docs/PHASE4_CONTRACT.md section 3.6). With the tile proxy on,
        /// the style URLs point at the proxy.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Map configuration.</returns>
        public async Task<PublicMapConfig> MapAsync(CancellationToken token = default)
        {
            PublicMapConfig map = new PublicMapConfig
            {
                Provider = await SettingAsync(DirectoryPropertySettings.MapProvider, token).ConfigureAwait(false),
                StyleUrl = Empty(await SettingAsync(DirectoryPropertySettings.MapStyleUrl, token).ConfigureAwait(false)),
                DarkStyleUrl = Empty(await SettingAsync(DirectoryPropertySettings.MapDarkStyleUrl, token).ConfigureAwait(false)),
                PublicKey = Empty(await SettingAsync(DirectoryPropertySettings.MapPublicKey, token).ConfigureAwait(false)),
                Attribution = Empty(await SettingAsync(DirectoryPropertySettings.MapAttribution, token).ConfigureAwait(false)),
                DefaultCenter = new PublicMapCenter
                {
                    Lat = Math.Clamp(Decimal(await SettingAsync(DirectoryPropertySettings.MapDefaultCenterLat, token).ConfigureAwait(false)), -90, 90),
                    Lng = Math.Clamp(Decimal(await SettingAsync(DirectoryPropertySettings.MapDefaultCenterLng, token).ConfigureAwait(false)), -180, 180)
                },
                DefaultZoom = Math.Clamp(await IntegerAsync(DirectoryPropertySettings.MapDefaultZoom, 2, token).ConfigureAwait(false), 0, 22),
                MaxZoom = Math.Clamp(await IntegerAsync(DirectoryPropertySettings.MapMaxZoom, 18, token).ConfigureAwait(false), 0, 22),
                MaxFeatures = Math.Clamp(await IntegerAsync(DirectoryPropertySettings.MapMaxFeatures, 500, token).ConfigureAwait(false), 50, 1000)
            };
            if (String.IsNullOrWhiteSpace(map.Provider)) map.Provider = MapProviderEnum.None.ToString();
            if (await FlagAsync(DirectoryPropertySettings.MapTileProxy, token).ConfigureAwait(false))
            {
                map.StyleUrl = ProxiedPath(map.StyleUrl);
                map.DarkStyleUrl = ProxiedPath(map.DarkStyleUrl);
            }
            return map;
        }

        /// <summary>
        /// Most map features per enumeration (directory.map.maxFeatures).
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>50 to 1000.</returns>
        public async Task<int> MaxFeaturesAsync(CancellationToken token = default)
        {
            return Math.Clamp(await IntegerAsync(DirectoryPropertySettings.MapMaxFeatures, 500, token).ConfigureAwait(false), 50, 1000);
        }

        /// <summary>
        /// Replace the directory section of GET /api/v1/property with the stored configuration (a PropertyDecorators entry).
        /// </summary>
        /// <param name="response">Response.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        /// <exception cref="ArgumentNullException">The response is null.</exception>
        public async Task DecorateAsync(PublicPropertyResponse response, CancellationToken token)
        {
            ArgumentNullException.ThrowIfNull(response);
            DirectoryConfig config = await CurrentAsync(token).ConfigureAwait(false);
            List<DirectoryProgram> programs = await ProgramsAsync(token).ConfigureAwait(false);
            response.Directory = new PublicDirectoryConfig
            {
                VenueLabel = config.VenueLabel,
                VenueLabelPlural = config.VenueLabelPlural,
                InstructorLabel = config.InstructorLabel,
                InstructorLabelPlural = config.InstructorLabelPlural,
                Programs = programs.Where(p => !p.IsRetired).Select(p => new PublicOption { Key = p.Key, Label = p.Label }).ToList(),
                InstructorRankFieldKey = config.InstructorRankFieldKey,
                Submissions = config.SubmissionsEnabled,
                Claims = config.ClaimsEnabled,
                Map = await MapAsync(token).ConfigureAwait(false),
                GeocoderConfigured = await _Services.Directory.Geocoding.IsConfiguredAsync(token).ConfigureAwait(false),
                PublicListingsEnabled = _Services.Property.Property.Features.Directory && await FlagAsync(DirectoryPropertySettings.PublicListingsEnabled, token).ConfigureAwait(false),
                Reviews = new PublicReviewConfig
                {
                    Enabled = await FlagAsync(DirectoryPropertySettings.ReviewsEnabled, token).ConfigureAwait(false),
                    MinLength = _Services.Settings.Directory.ReviewMinLength,
                    MaxLength = _Services.Settings.Directory.ReviewMaxLength,
                    ReplyMaxLength = _Services.Settings.Directory.ReviewReplyMaxLength
                },
                MemberMedia = await FlagAsync(DirectoryPropertySettings.MemberMediaEnabled, token).ConfigureAwait(false)
            };
        }

        /// <summary>
        /// Program enumeration (members and staff).
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="query">Query; filters.includeRetired for staff.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Page.</returns>
        /// <exception cref="ArgumentNullException">The actor is null.</exception>
        public Task<EnumerationResult<DirectoryProgramView>> EnumerateProgramsAsync(ContentActor actor, EnumerationQuery? query, CancellationToken token = default)
        {
            return ProgramPageAsync(actor, query, false, token);
        }

        /// <summary>
        /// POST /api/v1/admin/directory/programs/enumerate (directory staff): every program including retired ones
        /// (filters.includeRetired false hides them), with exact totals.
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="query">Query.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Page.</returns>
        /// <exception cref="ArgumentNullException">The actor is null.</exception>
        /// <exception cref="ApiErrorException">403 Forbidden.</exception>
        public Task<EnumerationResult<DirectoryProgramView>> AdminEnumerateProgramsAsync(ContentActor actor, EnumerationQuery? query, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            DirectoryAccess.RequireStaff(actor);
            return ProgramPageAsync(actor, query, true, token);
        }

        private async Task<EnumerationResult<DirectoryProgramView>> ProgramPageAsync(ContentActor actor, EnumerationQuery? query, bool admin, CancellationToken token)
        {
            ArgumentNullException.ThrowIfNull(actor);
            await EnsureSeededAsync(token).ConfigureAwait(false);
            EnumerationRequest request = _Services.Enumeration.Prepare(query, admin ? DirectoryCollections.AdminPrograms : DirectoryCollections.Programs, actor.IsDirectoryStaff);
            Dictionary<string, string> errors = new Dictionary<string, string>();
            bool includeRetired = actor.IsDirectoryStaff && (request.Query.FilterBool("includeRetired", errors) ?? admin);
            DirectoryFilters.ThrowIfAny(errors);
            EnumerationResult<DirectoryProgram> page = await _Services.Database.DirectoryListings.Config.EnumerateProgramsAsync(request, includeRetired, token).ConfigureAwait(false);
            Dictionary<string, ObjectMetadata> metadata = await _Services.Metadata.LoadAsync(TargetTypeEnum.DirectoryProgram, page.Objects.Select(p => p.Id), actor.IsDirectoryStaff, token).ConfigureAwait(false);
            return page.With(page.Objects.Select(p => View(p, metadata)).ToList());
        }

        /// <summary>
        /// POST /api/v1/admin/directory/programs (SystemAdmin).
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="info">Request (audit).</param>
        /// <param name="request">Body.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Program.</returns>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        /// <exception cref="ApiErrorException">400 ValidationFailed, 403 Forbidden, 409 ProgramKeyInUse or ProgramLimitReached.</exception>
        public async Task<DirectoryProgramView> CreateProgramAsync(ContentActor actor, RequestInfo info, DirectoryProgramRequest request, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            ArgumentNullException.ThrowIfNull(info);
            ArgumentNullException.ThrowIfNull(request);
            DirectoryAccess.RequireAdmin(actor);
            Dictionary<string, string> errors = new Dictionary<string, string>();
            string key = (request.Key ?? String.Empty).Trim();
            string label = (request.Label ?? String.Empty).Trim();
            if (!_Key.IsMatch(key)) errors["key"] = "Use 1 to 40 letters, digits, hyphens, or underscores (starting with a letter or digit).";
            if (label.Length == 0 || label.Length > 60) errors["label"] = "Required, at most 60 characters.";
            if (errors.Count > 0) throw ApiErrorException.Validation(errors);
            await _Services.Metadata.ValidateAsync(actor, request.Labels, request.Tags, true, token, TargetTypeEnum.DirectoryProgram).ConfigureAwait(false);
            List<DirectoryProgram> programs = await ProgramsAsync(token).ConfigureAwait(false);
            if (programs.Count(p => !p.IsRetired) >= Math.Min(ProgramBound, _Services.Settings.Directory.MaxPrograms))
                throw new ApiErrorException(409, "ProgramLimitReached", "At most " + _Services.Settings.Directory.MaxPrograms + " programs.");
            DateTime now = DateTime.UtcNow;
            DirectoryProgram program = new DirectoryProgram
            {
                Id = IdGenerator.Generate(DirectoryIdPrefixes.Program),
                Key = key,
                Label = label,
                SortOrder = (programs.Count == 0 ? 0 : programs.Max(p => p.SortOrder)) + 10,
                CreatedUtc = now,
                UpdatedUtc = now
            };
            try
            {
                program = await _Services.Database.DirectoryListings.Config.CreateProgramAsync(program, token).ConfigureAwait(false);
            }
            catch (DuplicateRecordException)
            {
                throw new ApiErrorException(409, "ProgramKeyInUse", "A program with this key exists (keys are stable; a retired program keeps its key).");
            }
            await _Services.Metadata.ApplyAsync(actor, TargetTypeEnum.DirectoryProgram, program.Id, request.Labels, request.Tags, token).ConfigureAwait(false);
            Invalidate();
            await _Services.Audit.WriteAsync(info, "directory.settings.update", TargetTypeEnum.DirectoryProgram.ToString(), program.Id, "Program " + program.Key + " created", token).ConfigureAwait(false);
            return await ProgramViewAsync(actor, program, token).ConfigureAwait(false);
        }

        /// <summary>
        /// PUT /api/v1/admin/directory/programs/{id} (SystemAdmin): label, labels, tags; a retired program can be restored
        /// by sending its label.
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="info">Request (audit).</param>
        /// <param name="id">Program.</param>
        /// <param name="request">Body.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Program.</returns>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        /// <exception cref="ApiErrorException">400 ValidationFailed, 403 Forbidden, 404 NotFound.</exception>
        public async Task<DirectoryProgramView> UpdateProgramAsync(ContentActor actor, RequestInfo info, string id, DirectoryProgramRequest request, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            ArgumentNullException.ThrowIfNull(info);
            ArgumentNullException.ThrowIfNull(request);
            DirectoryAccess.RequireAdmin(actor);
            DirectoryProgram program = await _Services.Database.DirectoryListings.Config.ReadProgramAsync(id, token).ConfigureAwait(false) ?? throw ApiErrorException.NotFound("Program");
            if (request.Key != null && !String.Equals(request.Key.Trim(), program.Key, StringComparison.Ordinal)) throw ApiErrorException.Field("key", "Program keys cannot change.");
            if (request.Label != null)
            {
                string label = request.Label.Trim();
                if (label.Length == 0 || label.Length > 60) throw ApiErrorException.Field("label", "Required, at most 60 characters.");
                program.Label = label;
                program.IsRetired = false;
            }
            await _Services.Metadata.ValidateAsync(actor, request.Labels, request.Tags, true, token, TargetTypeEnum.DirectoryProgram).ConfigureAwait(false);
            program.UpdatedUtc = DateTime.UtcNow;
            program = await _Services.Database.DirectoryListings.Config.UpdateProgramAsync(program, token).ConfigureAwait(false) ?? throw ApiErrorException.NotFound("Program");
            await _Services.Metadata.ApplyAsync(actor, TargetTypeEnum.DirectoryProgram, program.Id, request.Labels, request.Tags, token).ConfigureAwait(false);
            Invalidate();
            await _Services.Audit.WriteAsync(info, "directory.settings.update", TargetTypeEnum.DirectoryProgram.ToString(), program.Id, "Program " + program.Key + " updated", token).ConfigureAwait(false);
            return await ProgramViewAsync(actor, program, token).ConfigureAwait(false);
        }

        /// <summary>
        /// DELETE /api/v1/admin/directory/programs/{id} (SystemAdmin): a program in use is retired (hidden from forms and
        /// filters, values kept); an unused one is deleted.
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="info">Request (audit).</param>
        /// <param name="id">Program.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True when retired, false when deleted.</returns>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        /// <exception cref="ApiErrorException">403 Forbidden, 404 NotFound.</exception>
        public async Task<bool> DeleteProgramAsync(ContentActor actor, RequestInfo info, string id, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            ArgumentNullException.ThrowIfNull(info);
            DirectoryAccess.RequireAdmin(actor);
            DirectoryProgram program = await _Services.Database.DirectoryListings.Config.ReadProgramAsync(id, token).ConfigureAwait(false) ?? throw ApiErrorException.NotFound("Program");
            bool inUse = await _Services.Database.DirectoryListings.Config.ProgramInUseAsync(program.Key, token).ConfigureAwait(false);
            if (inUse)
            {
                program.IsRetired = true;
                program.UpdatedUtc = DateTime.UtcNow;
                await _Services.Database.DirectoryListings.Config.UpdateProgramAsync(program, token).ConfigureAwait(false);
            }
            else
            {
                await _Services.Database.DirectoryListings.Config.DeleteProgramAsync(program.Id, token).ConfigureAwait(false);
            }
            Invalidate();
            await _Services.Audit.WriteAsync(info, "directory.settings.update", TargetTypeEnum.DirectoryProgram.ToString(), program.Id, "Program " + program.Key + (inUse ? " retired" : " deleted"), token).ConfigureAwait(false);
            return inUse;
        }

        /// <summary>
        /// PUT /api/v1/admin/directory/programs/order (SystemAdmin).
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="info">Request (audit).</param>
        /// <param name="request">Body.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Programs updated.</returns>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        /// <exception cref="ApiErrorException">400 ValidationFailed, 403 Forbidden.</exception>
        public async Task<int> OrderProgramsAsync(ContentActor actor, RequestInfo info, DirectoryProgramOrderRequest request, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            ArgumentNullException.ThrowIfNull(info);
            ArgumentNullException.ThrowIfNull(request);
            DirectoryAccess.RequireAdmin(actor);
            List<string> ids = (request.ProgramIds ?? new List<string>()).Where(i => !String.IsNullOrWhiteSpace(i)).Select(i => i.Trim()).ToList();
            List<DirectoryProgram> active = (await ProgramsAsync(token).ConfigureAwait(false)).Where(p => !p.IsRetired).ToList();
            HashSet<string> expected = new HashSet<string>(active.Select(p => p.Id), StringComparer.Ordinal);
            if (ids.Count != expected.Count || ids.Distinct(StringComparer.Ordinal).Count() != ids.Count || !ids.All(expected.Contains))
                throw ApiErrorException.Field("programIds", "List every non-retired program exactly once.");
            int updated = await _Services.Database.DirectoryListings.Config.ReorderProgramsAsync(ids, DateTime.UtcNow, token).ConfigureAwait(false);
            Invalidate();
            await _Services.Audit.WriteAsync(info, "directory.settings.update", "Directory", _Services.PropertyId, "Programs reordered", token).ConfigureAwait(false);
            return updated;
        }

        /// <summary>
        /// GET /api/v1/admin/directory/settings (SystemAdmin).
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Settings.</returns>
        /// <exception cref="ArgumentNullException">The actor is null.</exception>
        /// <exception cref="ApiErrorException">403 Forbidden.</exception>
        public async Task<DirectorySettingsView> SettingsAsync(ContentActor actor, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            DirectoryAccess.RequireAdmin(actor);
            DirectoryConfig config = await CurrentAsync(token).ConfigureAwait(false);
            PropertySettingsResponse all = await _Services.PropertySettings.ReadAsync(token).ConfigureAwait(false);
            return new DirectorySettingsView
            {
                Config = ConfigView(config),
                Settings = all.Fields.Where(f => f.Key.StartsWith(DirectoryPropertySettings.Prefix, StringComparison.Ordinal)).ToList(),
                GeocoderConfigured = await _Services.Directory.Geocoding.IsConfiguredAsync(token).ConfigureAwait(false)
            };
        }

        /// <summary>
        /// PUT /api/v1/admin/directory/settings (SystemAdmin): labels, switches, rank field, and directory.* property
        /// settings in one call. Audited as directory.settings.update (key names only).
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="info">Request (audit).</param>
        /// <param name="request">Body.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Settings after the change.</returns>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        /// <exception cref="ApiErrorException">400 ValidationFailed, 403 Forbidden.</exception>
        public async Task<DirectorySettingsView> UpdateSettingsAsync(ContentActor actor, RequestInfo info, DirectorySettingsRequest request, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            ArgumentNullException.ThrowIfNull(info);
            ArgumentNullException.ThrowIfNull(request);
            DirectoryAccess.RequireAdmin(actor);
            Dictionary<string, string> errors = new Dictionary<string, string>();
            DirectoryConfig config = await CurrentAsync(token).ConfigureAwait(false);
            DirectoryConfig next = new DirectoryConfig
            {
                VenueLabel = Label(request.VenueLabel, config.VenueLabel, "venueLabel", errors),
                VenueLabelPlural = Label(request.VenueLabelPlural, config.VenueLabelPlural, "venueLabelPlural", errors),
                InstructorLabel = Label(request.InstructorLabel, config.InstructorLabel, "instructorLabel", errors),
                InstructorLabelPlural = Label(request.InstructorLabelPlural, config.InstructorLabelPlural, "instructorLabelPlural", errors),
                InstructorRankFieldKey = request.InstructorRankFieldKey == null ? config.InstructorRankFieldKey : (request.InstructorRankFieldKey.Trim().Length == 0 ? null : request.InstructorRankFieldKey.Trim()),
                SubmissionsEnabled = request.Submissions ?? config.SubmissionsEnabled,
                ClaimsEnabled = request.Claims ?? config.ClaimsEnabled,
                SourceHash = config.SourceHash,
                UpdatedUtc = DateTime.UtcNow
            };
            if (next.InstructorRankFieldKey != null)
            {
                IReadOnlyList<ProfileField> fields = await _Services.ProfileFields.AllAsync(token).ConfigureAwait(false);
                if (!fields.Any(f => f.Type == ProfileFieldTypeEnum.Rank && String.Equals(f.Key, next.InstructorRankFieldKey, StringComparison.OrdinalIgnoreCase)))
                    errors["instructorRankFieldKey"] = "Must be the key of a Rank profile field.";
            }
            Dictionary<string, JsonElement> values = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
            foreach (KeyValuePair<string, JsonElement> pair in request.Values ?? new Dictionary<string, JsonElement>())
            {
                if (!pair.Key.StartsWith(DirectoryPropertySettings.Prefix, StringComparison.OrdinalIgnoreCase) || PropertySettingsRegistry.Find(pair.Key) == null)
                {
                    errors["values." + pair.Key] = "Unknown directory setting.";
                    continue;
                }
                values[pair.Key] = NormalizeValue(pair.Key, pair.Value, errors);
            }
            foreach (string key in request.Clear ?? new List<string>())
                if (!key.StartsWith(DirectoryPropertySettings.Prefix, StringComparison.OrdinalIgnoreCase) || PropertySettingsRegistry.Find(key) == null) errors["clear"] = "Unknown directory setting " + key + ".";
            if (errors.Count > 0) throw ApiErrorException.Validation(errors, "One or more directory settings are invalid.");
            if (values.Count > 0 || (request.Clear?.Count ?? 0) > 0)
                await _Services.PropertySettings.UpdateAsync(new PropertySettingsUpdateRequest { Values = values, Clear = request.Clear ?? new List<string>() }, info, token).ConfigureAwait(false);
            await _Services.Database.DirectoryListings.Config.WriteAsync(next, token).ConfigureAwait(false);
            Invalidate();
            _Services.Directory.Geocoding.Invalidate();
            _Services.Directory.Tiles.Invalidate();
            List<string> keys = new List<string>();
            if (request.VenueLabel != null || request.VenueLabelPlural != null || request.InstructorLabel != null || request.InstructorLabelPlural != null) keys.Add("labels");
            if (request.Submissions != null) keys.Add("submissions");
            if (request.Claims != null) keys.Add("claims");
            if (request.InstructorRankFieldKey != null) keys.Add("instructorRankFieldKey");
            keys.AddRange(values.Keys);
            keys.AddRange(request.Clear ?? new List<string>());
            await _Services.Audit.WriteAsync(info, "directory.settings.update", "Directory", _Services.PropertyId, "Changed " + String.Join(", ", keys.Distinct(StringComparer.Ordinal)), token).ConfigureAwait(false);
            return await SettingsAsync(actor, token).ConfigureAwait(false);
        }

        /// <summary>
        /// The label of a program key (retired programs keep their label), or the key itself.
        /// </summary>
        /// <param name="programs">Programs.</param>
        /// <param name="key">Key.</param>
        /// <returns>Option.</returns>
        public static PublicOption Option(IReadOnlyList<DirectoryProgram> programs, string key)
        {
            ArgumentNullException.ThrowIfNull(programs);
            DirectoryProgram? program = programs.FirstOrDefault(p => String.Equals(p.Key, key, StringComparison.OrdinalIgnoreCase));
            return new PublicOption { Key = program?.Key ?? key, Label = program?.Label ?? key };
        }

        /// <summary>
        /// The view of the stored configuration.
        /// </summary>
        /// <param name="config">Configuration.</param>
        /// <returns>View.</returns>
        /// <exception cref="ArgumentNullException">The configuration is null.</exception>
        public static DirectoryConfigView ConfigView(DirectoryConfig config)
        {
            ArgumentNullException.ThrowIfNull(config);
            return new DirectoryConfigView
            {
                VenueLabel = config.VenueLabel,
                VenueLabelPlural = config.VenueLabelPlural,
                InstructorLabel = config.InstructorLabel,
                InstructorLabelPlural = config.InstructorLabelPlural,
                InstructorRankFieldKey = config.InstructorRankFieldKey,
                Submissions = config.SubmissionsEnabled,
                Claims = config.ClaimsEnabled,
                UpdatedUtc = config.UpdatedUtc
            };
        }

        #endregion

        #region Private-Methods

        private PropertyDirectorySection Section()
        {
            return _Services.Property.Property.DirectorySection ?? new PropertyDirectorySection();
        }

        private static DirectoryConfig FromSection(PropertyDirectorySection section, string? hash)
        {
            return new DirectoryConfig
            {
                VenueLabel = Bounded(section.VenueLabel, "Venue"),
                VenueLabelPlural = Bounded(section.VenueLabelPlural, "Venues"),
                InstructorLabel = Bounded(section.InstructorLabel, "Instructor"),
                InstructorLabelPlural = Bounded(section.InstructorLabelPlural, "Instructors"),
                InstructorRankFieldKey = String.IsNullOrWhiteSpace(section.InstructorRankFieldKey) ? null : section.InstructorRankFieldKey.Trim(),
                SubmissionsEnabled = section.Submissions,
                ClaimsEnabled = section.Claims,
                SourceHash = hash
            };
        }

        private static string Bounded(string? value, string fallback)
        {
            string text = (value ?? String.Empty).Trim();
            return text.Length == 0 || text.Length > 40 ? fallback : text;
        }

        private static string Hash(PropertyDirectorySection section)
        {
            byte[] bytes = SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(section)));
            return Convert.ToHexString(bytes).Substring(0, 32).ToLowerInvariant();
        }

        private static string Label(string? value, string current, string field, Dictionary<string, string> errors)
        {
            if (value == null) return current;
            string text = value.Trim();
            if (text.Length == 0 || text.Length > 40)
            {
                errors[field] = "1 to 40 characters.";
                return current;
            }
            return text;
        }

        private static JsonElement NormalizeValue(string key, JsonElement value, Dictionary<string, string> errors)
        {
            PropertySettingDefinition? d = PropertySettingsRegistry.Find(key);
            if (d == null || value.ValueKind == JsonValueKind.Null) return value;
            if (d.Type == SettingTypeEnum.Decimal)
            {
                double number;
                bool ok = value.ValueKind == JsonValueKind.Number ? value.TryGetDouble(out number) : Double.TryParse(value.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out number);
                double limit = key.EndsWith("Lat", StringComparison.Ordinal) ? 90 : 180;
                if (!ok || Double.IsNaN(number) || number < -limit || number > limit)
                {
                    errors["values." + key] = "Must be a number from -" + limit + " to " + limit + ".";
                    return value;
                }
                return JsonSerializer.SerializeToElement(number.ToString("R", CultureInfo.InvariantCulture));
            }
            if ((key == DirectoryPropertySettings.GeocoderBaseUrl || key == DirectoryPropertySettings.MapStyleUrl || key == DirectoryPropertySettings.MapDarkStyleUrl || key == DirectoryPropertySettings.MapTileBaseUrl) &&
                value.ValueKind == JsonValueKind.String && !String.IsNullOrWhiteSpace(value.GetString()))
            {
                if (!Uri.TryCreate(value.GetString()!.Trim(), UriKind.Absolute, out Uri? uri) || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
                    errors["values." + key] = "Must be an absolute http or https URL.";
            }
            return value;
        }

        private static string? Empty(string? value)
        {
            return String.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }

        private static double Decimal(string? text)
        {
            return Double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double value) && !Double.IsNaN(value) ? value : 0;
        }

        private static string? ProxiedPath(string? styleUrl)
        {
            if (styleUrl == null || !Uri.TryCreate(styleUrl, UriKind.Absolute, out Uri? uri)) return styleUrl;
            return Constants.ApiPrefix + "/directory/tiles" + uri.PathAndQuery;
        }

        private async Task<DirectoryProgramView> ProgramViewAsync(ContentActor actor, DirectoryProgram program, CancellationToken token)
        {
            Dictionary<string, ObjectMetadata> metadata = await _Services.Metadata.LoadAsync(TargetTypeEnum.DirectoryProgram, new[] { program.Id }, actor.IsDirectoryStaff, token).ConfigureAwait(false);
            return View(program, metadata);
        }

        private static DirectoryProgramView View(DirectoryProgram program, Dictionary<string, ObjectMetadata> metadata)
        {
            metadata.TryGetValue(program.Id, out ObjectMetadata? m);
            return new DirectoryProgramView
            {
                Id = program.Id,
                Key = program.Key,
                Label = program.Label,
                SortOrder = program.SortOrder,
                IsRetired = program.IsRetired,
                Labels = m?.Labels ?? new List<string>(),
                Tags = m?.Tags ?? new Dictionary<string, string>(),
                CreatedUtc = program.CreatedUtc,
                UpdatedUtc = program.UpdatedUtc
            };
        }

        #endregion
    }
}
