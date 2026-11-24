namespace TrellisKit.Server.Services
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core.Database;
    using TrellisKit.Core.Enumeration;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Exceptions;
    using TrellisKit.Core.Helpers;
    using TrellisKit.Core.Models;
    using TrellisKit.Core.Observability;
    using TrellisKit.Core.PropertyConfig;
    using TrellisKit.Core.Requests;
    using TrellisKit.Core.Responses;
    using TrellisKit.Core.Settings;

    /// <summary>
    /// One property's brand palette (docs/TRELLISKIT.md section 2.1): seeded from property.json brand.colors into
    /// propertybrandpalettes on the first load, replaced by the folder's colors in SingleProperty when they changed on
    /// disk (the folder wins on change, as for the access model and profile fields), edited and reverted by property
    /// administrators and platform administrators (audited, versioned), and served to every request from an in-memory
    /// snapshot. Edits are validated (hex values) and checked for WCAG 2.1 AA contrast; a failing pair is saved only
    /// with an explicit override, recorded as a warning in the audit trail. An edit touches the property registry so
    /// other instances reload within deployment.propertyCacheSeconds.
    /// Thread safety: <see cref="Current"/> is replaced atomically; changes are serialized per instance.
    /// </summary>
    public class BrandPaletteService
    {
        #region Public-Members

        /// <summary>
        /// The palette every request of this property uses now. Never null.
        /// </summary>
        public BrandPalette Current
        {
            get { return Volatile.Read(ref _Current); }
        }

        /// <summary>
        /// Whether the palette follows the property folder (set by <see cref="EnsureSeededAsync"/>).
        /// </summary>
        public bool SyncsFromFolder
        {
            get { return Volatile.Read(ref _SyncsFromFolder); }
        }

        /// <summary>
        /// Which engine palette presets members may choose now (loaded by <see cref="EnsureSeededAsync"/>; every
        /// preset until a choice is stored). Never null.
        /// </summary>
        public MemberPaletteChoices MemberChoices
        {
            get { return Volatile.Read(ref _MemberChoices); }
        }

        #endregion

        #region Private-Members

        private const int _MaxNote = 200;
        private const int _MaxReason = 200;
        private readonly DatabaseDriverBase _Database;
        private readonly PropertyConfiguration _Property;
        private readonly AppSettings _Settings;
        private readonly AuditService _Audit;
        private readonly AppLogger _Logger;
        private readonly SemaphoreSlim _Lock = new SemaphoreSlim(1, 1);
        private BrandPalette _Current;
        private bool _SyncsFromFolder = false;
        private MemberPaletteChoices _MemberChoices = new MemberPaletteChoices();

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the service. Until <see cref="EnsureSeededAsync"/> runs, <see cref="Current"/> is the property
        /// document's palette.
        /// </summary>
        /// <param name="database">Database driver bound to the property.</param>
        /// <param name="property">Property configuration.</param>
        /// <param name="settings">Running settings (property.maxPaletteVersions, read live).</param>
        /// <param name="audit">Audit service.</param>
        /// <param name="logger">Logger.</param>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        public BrandPaletteService(DatabaseDriverBase database, PropertyConfiguration property, AppSettings settings, AuditService audit, AppLogger logger)
        {
            _Database = database ?? throw new ArgumentNullException(nameof(database));
            _Property = property ?? throw new ArgumentNullException(nameof(property));
            _Settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _Audit = audit ?? throw new ArgumentNullException(nameof(audit));
            _Logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _Current = BrandPalette.FromDocument(property.Brand?.Colors);
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Load the stored palette, seeding it from the property document when none is stored, or replacing it with the
        /// document's colors (a new version, source DirectorySync, audited property.branding.sync) when
        /// <paramref name="mergeOnChange"/> is set and the document's brand.colors changed since it was last seeded.
        /// Seeded palettes are not refused for contrast; failing pairs are logged and shown on the Branding page.
        /// </summary>
        /// <param name="mergeOnChange">Whether the property folder wins on change (SingleProperty folder property).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The current palette.</returns>
        public async Task<BrandPalette> EnsureSeededAsync(bool mergeOnChange, CancellationToken token = default)
        {
            await _Lock.WaitAsync(token).ConfigureAwait(false);
            try
            {
                Volatile.Write(ref _SyncsFromFolder, mergeOnChange);
                MemberPaletteChoices? choices = await _Database.BrandPalettes.ReadMemberChoicesAsync(token).ConfigureAwait(false);
                Interlocked.Exchange(ref _MemberChoices, choices ?? new MemberPaletteChoices());
                BrandPalette document = BrandPalette.FromDocument(_Property.Brand?.Colors);
                BrandPalette? stored = await _Database.BrandPalettes.ReadCurrentAsync(token).ConfigureAwait(false);
                DateTime now = DateTime.UtcNow;
                if (stored == null)
                {
                    document.CreatedUtc = now;
                    try
                    {
                        await _Database.BrandPalettes.CreateAsync(document, token).ConfigureAwait(false);
                    }
                    catch (DuplicateRecordException)
                    {
                        // Another instance seeded first; use its row.
                        BrandPalette? raced = await _Database.BrandPalettes.ReadCurrentAsync(token).ConfigureAwait(false);
                        if (raced != null) document = raced;
                    }
                    _Logger.Info("[BrandPaletteService] [p:" + _Property.Id + "] seeded brand palette version " + document.Version + " from the property document");
                    WarnFailing("property.json brand.colors", document);
                    Publish(document);
                    return document;
                }
                if (mergeOnChange && !String.Equals(stored.SeedHash, document.SeedHash, StringComparison.Ordinal))
                {
                    document.Version = stored.Version + 1;
                    document.Source = BrandPaletteSourceEnum.DirectorySync;
                    document.CreatedUtc = now;
                    await _Database.BrandPalettes.CreateAsync(document, token).ConfigureAwait(false);
                    _Logger.Info("[BrandPaletteService] [p:" + _Property.Id + "] brand.colors changed on disk; brand palette is now version " + document.Version);
                    await _Audit.WriteAsync((TrellisKit.Core.Security.RequestContext?)null, null, "property.branding.sync", "Property", _Property.Id, "version " + document.Version + "; " + Changes(stored, document) + FailingSuffix(document), token).ConfigureAwait(false);
                    WarnFailing("property.json brand.colors", document);
                    await PruneAsync(token).ConfigureAwait(false);
                    Publish(document);
                    return document;
                }
                Publish(stored);
                return stored;
            }
            finally
            {
                _Lock.Release();
            }
        }

        /// <summary>
        /// GET /api/v1/admin/property/branding.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>View.</returns>
        public async Task<BrandingView> ViewAsync(CancellationToken token = default)
        {
            BrandPalette current = Current;
            BrandPalette? previous = await _Database.BrandPalettes.ReadBeforeAsync(current.Version, token).ConfigureAwait(false);
            return Build(current, previous);
        }

        /// <summary>
        /// PUT /api/v1/admin/property/branding: apply the changed keys, validate hex values and WCAG 2.1 AA contrast, and
        /// store a new version (source Edit). Audited property.branding.update; each override of a failing pair is
        /// audited as property.branding.contrastOverride with a warning. A body that changes nothing stores nothing.
        /// </summary>
        /// <param name="info">Request information (actor and source IP).</param>
        /// <param name="request">Body.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>View.</returns>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        /// <exception cref="ApiErrorException">400 ValidationFailed (fieldErrors name the key or the pair and its ratio), 409 Conflict.</exception>
        public async Task<BrandingView> UpdateAsync(RequestInfo info, BrandingUpdateRequest request, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(info);
            ArgumentNullException.ThrowIfNull(request);
            await _Lock.WaitAsync(token).ConfigureAwait(false);
            try
            {
                BrandPalette before = await _Database.BrandPalettes.ReadCurrentAsync(token).ConfigureAwait(false) ?? Current;
                BrandPalette next = before.Clone();
                Dictionary<string, string> errors = new Dictionary<string, string>(StringComparer.Ordinal);
                Apply(request.Light, "light", next.Light, errors);
                Apply(request.Dark, "dark", next.Dark, errors);
                string? note = Note(request.Note, errors);
                Dictionary<string, string?> requested = OverrideRequests(request.ContrastOverrides, errors);
                if (errors.Count > 0) throw ApiErrorException.Validation(errors);

                List<BrandContrastCheck> failing = BrandContrast.Check(next.Light, next.Dark, null).Where(c => !c.Passes).ToList();
                foreach (BrandContrastCheck check in failing)
                {
                    if (requested.ContainsKey(OverrideKey(check.Scheme, check.Pair))) continue;
                    errors["contrast." + check.Scheme + "." + check.Pair] = BrandContrast.Describe(check) + " Change a color, or send a contrastOverrides entry { scheme: \"" + check.Scheme + "\", pair: \"" + check.Pair + "\" } to save it anyway.";
                }
                if (errors.Count > 0) throw ApiErrorException.Validation(errors, "One or more color pairs fail WCAG 2.1 AA contrast.");

                next.Overrides = failing.Select(c => new BrandContrastOverride { Scheme = c.Scheme, Pair = c.Pair, Ratio = c.Ratio, Required = c.Required, Reason = requested[OverrideKey(c.Scheme, c.Pair)] }).ToList();
                if (next.SameColors(before) && SameOverrides(next.Overrides, before.Overrides)) return Build(Current, await _Database.BrandPalettes.ReadBeforeAsync(Current.Version, token).ConfigureAwait(false));

                DateTime now = DateTime.UtcNow;
                next.Version = before.Version + 1;
                next.Source = BrandPaletteSourceEnum.Edit;
                next.Note = note;
                next.CreatedByUserId = info.Context?.UserId ?? info.Context?.PlatformAdminId;
                next.CreatedUtc = now;
                next.SeedHash = before.SeedHash;
                await CreateAsync(next, token).ConfigureAwait(false);
                await _Audit.WriteAsync(info, "property.branding.update", "Property", _Property.Id, "version " + next.Version + "; " + Changes(before, next), token).ConfigureAwait(false);
                foreach (BrandContrastCheck check in failing)
                {
                    string? reason = requested[OverrideKey(check.Scheme, check.Pair)];
                    await _Audit.WriteAsync(info, "property.branding.contrastOverride", "Property", _Property.Id,
                        "Warning: version " + next.Version + " saved below WCAG 2.1 AA by explicit override. " + BrandContrast.Describe(check) + (String.IsNullOrWhiteSpace(reason) ? String.Empty : " Reason: " + reason), token).ConfigureAwait(false);
                    _Logger.Warn("[BrandPaletteService] [p:" + _Property.Id + "] contrast override saved: " + BrandContrast.Describe(check));
                }
                await AfterChangeAsync(next, now, "property.branding.update", token).ConfigureAwait(false);
                return Build(next, before);
            }
            finally
            {
                _Lock.Release();
            }
        }

        /// <summary>
        /// POST /api/v1/admin/property/branding/revert: store the version before the current one (Previous, with its
        /// overrides), any retained version (Version, with version), or the property.json seed (Seed) as a new version. Audited property.branding.revert; pairs the
        /// restored palette fails are listed as a warning.
        /// </summary>
        /// <param name="info">Request information (actor and source IP).</param>
        /// <param name="request">Body.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>View.</returns>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        /// <exception cref="ApiErrorException">400 ValidationFailed, 404 NotFound (version not retained), 409 NoPreviousVersion, AlreadyCurrent, or Conflict.</exception>
        public async Task<BrandingView> RevertAsync(RequestInfo info, BrandingRevertRequest request, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(info);
            ArgumentNullException.ThrowIfNull(request);
            Dictionary<string, string> errors = new Dictionary<string, string>(StringComparer.Ordinal);
            string to = (request.To ?? String.Empty).Trim();
            bool toSeed = String.Equals(to, "Seed", StringComparison.OrdinalIgnoreCase);
            bool toPrevious = String.Equals(to, "Previous", StringComparison.OrdinalIgnoreCase);
            bool toVersion = String.Equals(to, "Version", StringComparison.OrdinalIgnoreCase);
            if (!toSeed && !toPrevious && !toVersion) errors["to"] = "Must be Previous, Seed, or Version.";
            if (toVersion && (request.Version == null || request.Version.Value < 1)) errors["version"] = "Required with to Version: a retained version number (1 or more).";
            string? note = Note(request.Note, errors);
            if (errors.Count > 0) throw ApiErrorException.Validation(errors);

            await _Lock.WaitAsync(token).ConfigureAwait(false);
            try
            {
                BrandPalette before = await _Database.BrandPalettes.ReadCurrentAsync(token).ConfigureAwait(false) ?? Current;
                BrandPalette next;
                int restored = 0;
                if (toSeed)
                {
                    next = BrandPalette.FromDocument(_Property.Brand?.Colors);
                    next.Source = BrandPaletteSourceEnum.RevertSeed;
                }
                else
                {
                    BrandPalette? previous;
                    if (toVersion)
                    {
                        if (request.Version!.Value == before.Version) throw new ApiErrorException(409, "AlreadyCurrent", "Version " + before.Version + " is already the current palette.");
                        previous = await _Database.BrandPalettes.ReadVersionAsync(request.Version.Value, token).ConfigureAwait(false);
                        if (previous == null) throw ApiErrorException.NotFound("Palette version");
                    }
                    else
                    {
                        previous = await _Database.BrandPalettes.ReadBeforeAsync(before.Version, token).ConfigureAwait(false);
                        if (previous == null) throw new ApiErrorException(409, "NoPreviousVersion", "There is no earlier palette version to revert to.");
                    }
                    restored = previous.Version;
                    next = previous.Clone();
                    next.Source = toVersion ? BrandPaletteSourceEnum.RevertVersion : BrandPaletteSourceEnum.RevertPrevious;
                    next.SeedHash = before.SeedHash;
                    List<string> stillFailing = BrandContrast.Check(next.Light, next.Dark, null).Where(c => !c.Passes).Select(c => OverrideKey(c.Scheme, c.Pair)).ToList();
                    next.Overrides = next.Overrides.Where(o => stillFailing.Contains(OverrideKey(o.Scheme, o.Pair))).ToList();
                }
                DateTime now = DateTime.UtcNow;
                next.Version = before.Version + 1;
                next.Note = note;
                next.CreatedByUserId = info.Context?.UserId ?? info.Context?.PlatformAdminId;
                next.CreatedUtc = now;
                await CreateAsync(next, token).ConfigureAwait(false);
                await _Audit.WriteAsync(info, "property.branding.revert", "Property", _Property.Id, "version " + next.Version + " restores " + (toSeed ? "the property.json seed" : "version " + restored) + "; " + Changes(before, next) + FailingSuffix(next), token).ConfigureAwait(false);
                await AfterChangeAsync(next, now, "property.branding.revert", token).ConfigureAwait(false);
                return Build(next, before);
            }
            finally
            {
                _Lock.Release();
            }
        }

        /// <summary>
        /// GET /api/v1/admin/property/branding/versions (and POST .../versions/enumerate): the retained versions,
        /// newest first by default, each with its colors, a failing pair count, and whether it is current. Bounded by
        /// property.maxPaletteVersions; filter source; label and tag filters are 400 ValidationFailed (versions carry none).
        /// </summary>
        /// <param name="query">Query, or null for the defaults.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Page.</returns>
        /// <exception cref="ApiErrorException">400 ValidationFailed or InvalidContinuationToken.</exception>
        public async Task<EnumerationResult<BrandingVersionView>> VersionsAsync(EnumerationQuery? query, CancellationToken token = default)
        {
            EnumerationRequest request = EnumerationNormalizer.Normalize(query, BrandingCollections.Versions, _Settings.Enumeration, true, _Property.Id + "|branding-versions", null);
            Dictionary<string, string> errors = new Dictionary<string, string>(StringComparer.Ordinal);
            BrandPaletteSourceEnum? source = request.Query.FilterEnum<BrandPaletteSourceEnum>("source", errors);
            if (errors.Count > 0) throw ApiErrorException.Validation(errors);
            BrandPalette current = await _Database.BrandPalettes.ReadCurrentAsync(token).ConfigureAwait(false) ?? Current;
            EnumerationResult<BrandPalette> page = await _Database.BrandPalettes.EnumerateAsync(request, source?.ToString(), token).ConfigureAwait(false);
            return page.With(page.Objects.Select(p => new BrandingVersionView
            {
                Version = p.Version,
                Source = p.Source.ToString(),
                Note = p.Note,
                Light = p.Light.Clone(),
                Dark = p.Dark.Clone(),
                FailingPairs = BrandContrast.Check(p.Light, p.Dark, null).Count(c => !c.Passes),
                OverrideCount = p.Overrides.Count,
                IsCurrent = p.Version == current.Version,
                CreatedByUserId = p.CreatedByUserId,
                CreatedUtc = p.CreatedUtc
            }).ToList());
        }

        /// <summary>
        /// PUT /api/v1/admin/property/branding/member-presets: set which engine palette presets members may choose
        /// (All, None, or Selected with presetIds). Members whose preference is no longer allowed see the property's
        /// brand palette (their stored preference is kept, so it applies again if the preset is allowed again).
        /// Audited property.branding.memberPresets; a body that changes nothing stores nothing.
        /// </summary>
        /// <param name="info">Request information (actor and source IP).</param>
        /// <param name="request">Body.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The branding view.</returns>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        /// <exception cref="ApiErrorException">400 ValidationFailed (fieldErrors mode, presetIds, or presetIds[i]).</exception>
        public async Task<BrandingView> UpdateMemberPresetsAsync(RequestInfo info, MemberPresetsRequest request, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(info);
            ArgumentNullException.ThrowIfNull(request);
            Dictionary<string, string> errors = new Dictionary<string, string>(StringComparer.Ordinal);
            MemberPaletteModeEnum mode = MemberPaletteModeEnum.All;
            if (String.IsNullOrWhiteSpace(request.Mode) || !Enum.TryParse(request.Mode.Trim(), true, out mode) || !Enum.IsDefined(mode))
                errors["mode"] = "Must be All, None, or Selected.";
            List<string> ids = new List<string>();
            if (errors.Count == 0 && mode == MemberPaletteModeEnum.Selected)
            {
                List<string> requested = request.PresetIds ?? new List<string>();
                if (requested.Count == 0) errors["presetIds"] = "List at least one preset with mode Selected (or use None).";
                else if (requested.Count > BrandPresetCatalog.All.Count) errors["presetIds"] = "At most " + BrandPresetCatalog.All.Count + " ids.";
                else
                {
                    for (int i = 0; i < requested.Count; i++)
                    {
                        string id = (requested[i] ?? String.Empty).Trim();
                        if (BrandPresetCatalog.Find(id) == null) errors["presetIds[" + i + "]"] = "Unknown preset; expected one of " + String.Join(", ", BrandPresetCatalog.All.Select(p => p.Id)) + ".";
                        else if (!ids.Contains(id)) ids.Add(id);
                    }
                }
            }
            if (errors.Count > 0) throw ApiErrorException.Validation(errors);
            // Catalog order, so equal sets compare equal.
            ids = BrandPresetCatalog.All.Select(p => p.Id).Where(ids.Contains).ToList();

            await _Lock.WaitAsync(token).ConfigureAwait(false);
            try
            {
                MemberPaletteChoices before = await _Database.BrandPalettes.ReadMemberChoicesAsync(token).ConfigureAwait(false) ?? new MemberPaletteChoices();
                if (before.Mode == mode && before.PresetIds.SequenceEqual(ids, StringComparer.Ordinal))
                {
                    Interlocked.Exchange(ref _MemberChoices, before);
                    return await ViewUnlockedAsync(token).ConfigureAwait(false);
                }
                DateTime now = DateTime.UtcNow;
                MemberPaletteChoices next = before.Clone();
                if (before.Revision == 0) next.CreatedUtc = now;
                next.Mode = mode;
                next.PresetIds = ids;
                next.Revision = before.Revision + 1;
                next.UpdatedByUserId = info.Context?.UserId ?? info.Context?.PlatformAdminId;
                next.UpdatedUtc = now;
                await _Database.BrandPalettes.WriteMemberChoicesAsync(next, token).ConfigureAwait(false);
                await _Audit.WriteAsync(info, "property.branding.memberPresets", "Property", _Property.Id, "member presets " + DescribeChoices(before) + " -> " + DescribeChoices(next), token).ConfigureAwait(false);
                Interlocked.Exchange(ref _MemberChoices, next);
                await TouchRegistryAsync(now, token).ConfigureAwait(false);
                TrellisKitTelemetry.Increment(TrellisKitTelemetry.PlatformActions, "action", "property.branding.memberPresets");
                return await ViewUnlockedAsync(token).ConfigureAwait(false);
            }
            finally
            {
                _Lock.Release();
            }
        }

        /// <summary>
        /// GET /api/v1/me/appearance: a member's palette preference and the preset that applies.
        /// </summary>
        /// <param name="userId">Member id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>View.</returns>
        /// <exception cref="ArgumentException">The id is empty.</exception>
        /// <exception cref="ApiErrorException">404 NotFound for an unknown or deleted member.</exception>
        public async Task<AppearanceView> AppearanceAsync(string userId, CancellationToken token = default)
        {
            if (String.IsNullOrWhiteSpace(userId)) throw new ArgumentException("A member id is required.", nameof(userId));
            User? user = await _Database.Users.ReadAsync(userId, token).ConfigureAwait(false);
            if (user == null || user.IsDeleted) throw ApiErrorException.NotFound("Member");
            return AppearanceOf(user.PalettePreset);
        }

        /// <summary>
        /// PUT /api/v1/me/appearance: set the member's palette preference to an allowed preset, or clear it (null or
        /// empty) for the property's brand palette.
        /// </summary>
        /// <param name="userId">Member id.</param>
        /// <param name="request">Body.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>View.</returns>
        /// <exception cref="ArgumentException">The id is empty.</exception>
        /// <exception cref="ArgumentNullException">The request is null.</exception>
        /// <exception cref="ApiErrorException">400 ValidationFailed (fieldErrors.palettePreset: unknown or not allowed), 404 NotFound.</exception>
        public async Task<AppearanceView> UpdateAppearanceAsync(string userId, AppearanceRequest request, CancellationToken token = default)
        {
            if (String.IsNullOrWhiteSpace(userId)) throw new ArgumentException("A member id is required.", nameof(userId));
            ArgumentNullException.ThrowIfNull(request);
            string? presetId = String.IsNullOrWhiteSpace(request.PalettePreset) ? null : request.PalettePreset.Trim();
            if (presetId != null)
            {
                if (BrandPresetCatalog.Find(presetId) == null)
                    throw ApiErrorException.Validation(new Dictionary<string, string> { { "palettePreset", "Unknown preset; send null for the property palette or one of brand.presets[].id from GET /api/v1/property." } });
                if (!MemberChoices.Allows(presetId))
                    throw ApiErrorException.Validation(new Dictionary<string, string> { { "palettePreset", "This palette is not available in this community; send null for the property palette or one of brand.presets[].id from GET /api/v1/property." } });
            }
            User? user = await _Database.Users.ReadAsync(userId, token).ConfigureAwait(false);
            if (user == null || user.IsDeleted) throw ApiErrorException.NotFound("Member");
            if (!String.Equals(user.PalettePreset, presetId, StringComparison.Ordinal))
                await _Database.Users.SetPalettePresetAsync(userId, presetId, token).ConfigureAwait(false);
            return AppearanceOf(presetId);
        }

        /// <summary>
        /// The presets members may choose now (GET /api/v1/property brand.presets), in catalog order.
        /// </summary>
        /// <returns>Copies.</returns>
        public List<BrandPreset> AllowedPresets()
        {
            return MemberChoices.Allowed();
        }

        #endregion

        #region Private-Methods

        private async Task<BrandingView> ViewUnlockedAsync(CancellationToken token)
        {
            BrandPalette current = Current;
            BrandPalette? previous = await _Database.BrandPalettes.ReadBeforeAsync(current.Version, token).ConfigureAwait(false);
            return Build(current, previous);
        }

        private AppearanceView AppearanceOf(string? presetId)
        {
            MemberPaletteChoices choices = MemberChoices;
            return new AppearanceView
            {
                PalettePreset = presetId,
                EffectivePreset = choices.Allows(presetId) ? presetId : null,
                PresetsAvailable = BrandPresetCatalog.All.Any(p => choices.Allows(p.Id))
            };
        }

        private static MemberPresetsView MemberPresetsOf(MemberPaletteChoices choices)
        {
            return new MemberPresetsView
            {
                Mode = choices.Mode,
                PresetIds = choices.Mode == MemberPaletteModeEnum.Selected ? choices.PresetIds.ToList() : new List<string>(),
                AllowedPresetIds = BrandPresetCatalog.All.Where(p => choices.Allows(p.Id)).Select(p => p.Id).ToList(),
                Revision = choices.Revision,
                UpdatedByUserId = choices.UpdatedByUserId,
                UpdatedUtc = choices.UpdatedUtc
            };
        }

        private static string DescribeChoices(MemberPaletteChoices choices)
        {
            return choices.Mode == MemberPaletteModeEnum.Selected ? "Selected [" + String.Join(",", choices.PresetIds) + "]" : choices.Mode.ToString();
        }

        private BrandingView Build(BrandPalette current, BrandPalette? previous)
        {
            BrandPalette seed = BrandPalette.FromDocument(_Property.Brand?.Colors);
            return new BrandingView
            {
                PropertyId = _Property.Id,
                Version = current.Version,
                Source = current.Source.ToString(),
                Note = current.Note,
                Light = current.Light.Clone(),
                Dark = current.Dark.Clone(),
                Keys = BrandColorSet.Keys.ToList(),
                Backgrounds = new BrandBackgroundsView(),
                Contrast = BrandContrast.Check(current.Light, current.Dark, current.Overrides),
                Overrides = current.Overrides.ToList(),
                Seed = new BrandPresetView { Light = seed.Light, Dark = seed.Dark },
                Previous = previous == null ? null : new BrandPresetView { Version = previous.Version, Source = previous.Source.ToString(), CreatedUtc = previous.CreatedUtc, Light = previous.Light.Clone(), Dark = previous.Dark.Clone() },
                Presets = BrandPresetCatalog.All.Select(p => p.Clone()).ToList(),
                MemberPresets = MemberPresetsOf(MemberChoices),
                SyncsFromFolder = SyncsFromFolder,
                CreatedByUserId = current.CreatedByUserId,
                CreatedUtc = current.CreatedUtc
            };
        }

        private static void Apply(Dictionary<string, string?>? patch, string scheme, BrandColorSet set, Dictionary<string, string> errors)
        {
            if (patch == null) return;
            if (patch.Count > BrandColorSet.Keys.Count * 2)
            {
                errors[scheme] = "At most " + BrandColorSet.Keys.Count + " keys.";
                return;
            }
            foreach (KeyValuePair<string, string?> entry in patch)
            {
                string path = scheme + "." + entry.Key;
                if (!BrandColorSet.IsKey(entry.Key))
                {
                    errors[path] = "Unknown brand color key; expected one of " + String.Join(", ", BrandColorSet.Keys) + ".";
                    continue;
                }
                string? normalized = BrandContrast.NormalizeHex(entry.Value);
                if (normalized == null)
                {
                    errors[path] = "Must be a hex color #RRGGBB (or #RGB), for example #1F5FBF.";
                    continue;
                }
                set.Set(entry.Key, normalized);
            }
        }

        private static string? Note(string? value, Dictionary<string, string> errors)
        {
            string? note = String.IsNullOrWhiteSpace(value) ? null : value.Trim();
            if (note != null && note.Length > _MaxNote) errors["note"] = "Must be at most " + _MaxNote + " characters.";
            return note;
        }

        private static Dictionary<string, string?> OverrideRequests(List<BrandContrastOverrideRequest>? overrides, Dictionary<string, string> errors)
        {
            Dictionary<string, string?> requested = new Dictionary<string, string?>(StringComparer.Ordinal);
            if (overrides == null) return requested;
            int limit = BrandContrast.Pairs.Count * BrandContrast.Schemes.Count;
            if (overrides.Count > limit)
            {
                errors["contrastOverrides"] = "At most " + limit + " entries.";
                return requested;
            }
            for (int i = 0; i < overrides.Count; i++)
            {
                BrandContrastOverrideRequest? item = overrides[i];
                string path = "contrastOverrides[" + i + "]";
                if (item == null)
                {
                    errors[path] = "Must be an object { scheme, pair, reason? }.";
                    continue;
                }
                string scheme = (item.Scheme ?? String.Empty).Trim().ToLowerInvariant();
                if (!BrandContrast.Schemes.Contains(scheme)) errors[path + ".scheme"] = "Must be light or dark.";
                if (BrandContrast.FindPair((item.Pair ?? String.Empty).Trim()) == null) errors[path + ".pair"] = "Unknown pair; expected one of " + String.Join(", ", BrandContrast.Pairs.Select(p => p.Id)) + ".";
                string? reason = String.IsNullOrWhiteSpace(item.Reason) ? null : item.Reason.Trim();
                if (reason != null && reason.Length > _MaxReason) errors[path + ".reason"] = "Must be at most " + _MaxReason + " characters.";
                if (errors.Keys.Any(k => k.StartsWith(path, StringComparison.Ordinal))) continue;
                requested[OverrideKey(scheme, item.Pair!.Trim())] = reason;
            }
            return requested;
        }

        private static string OverrideKey(string scheme, string pair)
        {
            return scheme + "|" + pair;
        }

        private static bool SameOverrides(List<BrandContrastOverride> a, List<BrandContrastOverride> b)
        {
            List<string> left = a.Select(o => OverrideKey(o.Scheme, o.Pair) + "|" + (o.Reason ?? String.Empty)).OrderBy(x => x, StringComparer.Ordinal).ToList();
            List<string> right = b.Select(o => OverrideKey(o.Scheme, o.Pair) + "|" + (o.Reason ?? String.Empty)).OrderBy(x => x, StringComparer.Ordinal).ToList();
            return left.SequenceEqual(right, StringComparer.Ordinal);
        }

        private async Task CreateAsync(BrandPalette next, CancellationToken token)
        {
            try
            {
                await _Database.BrandPalettes.CreateAsync(next, token).ConfigureAwait(false);
            }
            catch (DuplicateRecordException)
            {
                throw new ApiErrorException(409, "Conflict", "The palette changed in the meantime; reload it and try again.");
            }
        }

        private async Task AfterChangeAsync(BrandPalette next, DateTime now, string action, CancellationToken token)
        {
            await PruneAsync(token).ConfigureAwait(false);
            await TouchRegistryAsync(now, token).ConfigureAwait(false);
            TrellisKitTelemetry.Increment(TrellisKitTelemetry.PlatformActions, "action", action);
            Publish(next);
        }

        private async Task PruneAsync(CancellationToken token)
        {
            try
            {
                await _Database.BrandPalettes.PruneAsync(_Settings.Property.MaxPaletteVersions, token).ConfigureAwait(false);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                _Logger.Warn("[BrandPaletteService] [p:" + _Property.Id + "] pruning old palette versions failed: " + e.Message);
            }
        }

        private void Publish(BrandPalette palette)
        {
            Interlocked.Exchange(ref _Current, palette);
        }

        private void WarnFailing(string what, BrandPalette palette)
        {
            foreach (BrandContrastCheck check in BrandContrast.Check(palette.Light, palette.Dark, palette.Overrides).Where(c => !c.Passes))
                _Logger.Warn("[BrandPaletteService] [p:" + _Property.Id + "] " + what + " fails WCAG 2.1 AA: " + BrandContrast.Describe(check));
        }

        private static string FailingSuffix(BrandPalette palette)
        {
            List<string> failing = BrandContrast.Check(palette.Light, palette.Dark, palette.Overrides).Where(c => !c.Passes).Select(c => c.Scheme + " " + c.Pair + " " + c.Ratio.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) + ":1").ToList();
            return failing.Count == 0 ? String.Empty : "; warning: below WCAG 2.1 AA: " + String.Join(", ", failing);
        }

        private async Task TouchRegistryAsync(DateTime now, CancellationToken token)
        {
            try
            {
                PropertyRecord? record = await _Database.Registry.ReadAsync(_Property.Id, token).ConfigureAwait(false);
                if (record == null) return;
                record.UpdatedUtc = now;
                await _Database.Registry.UpdateAsync(record, token).ConfigureAwait(false);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                _Logger.Warn("[BrandPaletteService] [p:" + _Property.Id + "] registry touch failed (other instances apply the change on their next reload): " + e.Message);
            }
        }

        private static string Changes(BrandPalette before, BrandPalette after)
        {
            List<string> parts = new List<string>();
            foreach (string scheme in BrandContrast.Schemes)
            {
                foreach (string key in BrandColorSet.Keys)
                {
                    string a = before.SetOf(scheme).Get(key);
                    string b = after.SetOf(scheme).Get(key);
                    if (!String.Equals(a, b, StringComparison.OrdinalIgnoreCase)) parts.Add(scheme + "." + key + " " + (a.Length == 0 ? "unset" : a) + " -> " + b);
                }
            }
            string overrides = String.Join(",", after.Overrides.Select(o => o.Scheme + " " + o.Pair));
            if (overrides.Length > 0) parts.Add("overrides [" + overrides + "]");
            return parts.Count == 0 ? "no color change" : String.Join("; ", parts);
        }

        #endregion
    }
}
