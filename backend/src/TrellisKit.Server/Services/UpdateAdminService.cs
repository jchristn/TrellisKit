namespace TrellisKit.Server.Services
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;
    using System.Security.Cryptography;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core;
    using TrellisKit.Core.Database;
    using TrellisKit.Core.Enumeration;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Exceptions;
    using TrellisKit.Core.Helpers;
    using TrellisKit.Core.Models;
    using TrellisKit.Core.Requests;
    using TrellisKit.Core.Responses;
    using TrellisKit.Core.Settings;

    /// <summary>
    /// Live update admin reads and registry management: update group payloads and enumerations, assets, adoption,
    /// channels, runtime versions, deploy keys, signing keys, and health (docs/LIVE_UPDATES_CONTRACT.md sections 6.3,
    /// 7.4, 7.5, 9.3, 10.1, 10.3.1).
    /// Thread safety: safe to call concurrently.
    /// </summary>
    public class UpdateAdminService
    {
        #region Public-Members

        /// <summary>
        /// Deploy key secret prefix.
        /// </summary>
        public const string DeployKeyPrefix = "tkdk_";

        #endregion

        #region Private-Members

        private const string _Base = Constants.ApiPrefix + "/admin";
        private readonly DatabaseDriverBase _Database;
        private readonly AppSettings _Settings;
        private readonly EnumerationService _Enumeration;
        private readonly MetadataService _Metadata;
        private readonly UpdateSigningService _Signing;
        private readonly UpdateObjectStore _Store;
        private readonly string _PropertyId;
        private bool _ChannelsSeeded = false;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the service.
        /// </summary>
        /// <param name="database">Property-bound database driver.</param>
        /// <param name="settings">Running settings.</param>
        /// <param name="enumeration">Enumeration service.</param>
        /// <param name="metadata">Metadata service.</param>
        /// <param name="signing">Signing service.</param>
        /// <param name="store">Update storage.</param>
        /// <param name="propertyId">Property id.</param>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        public UpdateAdminService(DatabaseDriverBase database, AppSettings settings, EnumerationService enumeration, MetadataService metadata, UpdateSigningService signing, UpdateObjectStore store, string propertyId)
        {
            _Database = database ?? throw new ArgumentNullException(nameof(database));
            _Settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _Enumeration = enumeration ?? throw new ArgumentNullException(nameof(enumeration));
            _Metadata = metadata ?? throw new ArgumentNullException(nameof(metadata));
            _Signing = signing ?? throw new ArgumentNullException(nameof(signing));
            _Store = store ?? throw new ArgumentNullException(nameof(store));
            _PropertyId = propertyId ?? throw new ArgumentNullException(nameof(propertyId));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Seed the built-in channels once per process.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        public async Task EnsureChannelsAsync(CancellationToken token = default)
        {
            if (_ChannelsSeeded) return;
            await _Database.LiveUpdates.EnsureChannelsAsync(LiveUpdateRules.BuiltInChannels, DateTime.UtcNow, token).ConfigureAwait(false);
            _ChannelsSeeded = true;
        }

        /// <summary>
        /// Whether a channel exists in the property.
        /// </summary>
        /// <param name="name">Channel name.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True when it exists.</returns>
        public async Task<bool> ChannelExistsAsync(string? name, CancellationToken token = default)
        {
            if (String.IsNullOrWhiteSpace(name)) return false;
            if (LiveUpdateRules.BuiltInChannels.Contains(name)) return true;
            await EnsureChannelsAsync(token).ConfigureAwait(false);
            return await _Database.LiveUpdates.ReadChannelAsync(name, token).ConfigureAwait(false) != null;
        }

        /// <summary>
        /// The UpdateGroup payload of a group.
        /// </summary>
        /// <param name="group">Group.</param>
        /// <param name="detail">Include adoption, the asset preview, source map counts, and the automatic halt counts.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>View.</returns>
        /// <exception cref="ArgumentNullException">The group is null.</exception>
        public async Task<UpdateGroupView> ViewAsync(UpdateGroup group, bool detail, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(group);
            List<UpdateRecord> updates = await _Database.LiveUpdates.ListGroupUpdatesAsync(group.Id, token).ConfigureAwait(false);
            List<UpdateRolloutStage> stages = await _Database.LiveUpdates.ListStagesAsync(group.Id, token).ConfigureAwait(false);
            ObjectMetadata metadata = await _Metadata.LoadOneAsync(TargetTypeEnum.UpdateGroup, group.Id, true, token).ConfigureAwait(false);
            UpdateGroupView view = new UpdateGroupView
            {
                Id = group.Id,
                Kind = group.Kind,
                Channel = group.Channel,
                Message = group.Message,
                Status = group.Status,
                RolloutPercentage = LiveUpdateRules.ToPercent(group.RolloutBasisPoints),
                Critical = group.Critical,
                CriticalMessage = group.CriticalMessage,
                GitCommit = group.GitCommit,
                GitDirty = group.GitDirty,
                RepublishOf = group.RepublishOf,
                PromotedFrom = group.PromotedFrom,
                UpdateGroupUuid = group.ClientGroupId,
                Platforms = updates.Select(u => u.Platform).Distinct(StringComparer.Ordinal).ToList(),
                Updates = updates.Take(2).Select(LiveUpdateRules.ViewOf).ToList(),
                CreatedBy = new UpdateCreatorView { Type = group.CreatedByType, Id = group.CreatedById, Name = group.CreatedByName },
                PublishedUtc = group.PublishedUtc,
                PausedUtc = group.PausedUtc,
                RolledBackUtc = group.RolledBackUtc,
                SupersededUtc = group.SupersededUtc,
                RolloutPlan = stages.Select(s => new UpdateRolloutStageView { Position = s.Position, Percentage = LiveUpdateRules.ToPercent(s.BasisPoints), StartsUtc = s.StartsUtc, AppliedUtc = s.AppliedUtc }).ToList(),
                AutoHalt = new UpdateAutoHaltView
                {
                    Enabled = group.AutoHaltEnabled,
                    FailurePercent = group.AutoHaltFailurePercent,
                    MinInstalls = group.AutoHaltMinInstalls,
                    Halted = group.HaltedUtc != null,
                    HaltedUtc = group.HaltedUtc,
                    HaltReason = group.HaltReason
                },
                Labels = metadata.Labels,
                Tags = metadata.Tags,
                CreatedUtc = group.CreatedUtc,
                UpdatedUtc = group.UpdatedUtc
            };
            if (!detail) return view;
            DateTime active = DateTime.UtcNow.AddDays(-30);
            UpdateHealthCounts health = await _Database.LiveUpdates.CountHealthAsync(group.Id, active, token).ConfigureAwait(false);
            view.AutoHalt.Failures = health.Failures;
            view.AutoHalt.Successes = health.Successes;
            long total = health.Failures + health.Successes;
            view.AutoHalt.FailureRatePercent = total == 0 ? null : Math.Round(health.Failures * 100m / total, 2);
            long activeInstalls = 0;
            foreach (string platform in view.Platforms)
                activeInstalls += await _Database.LiveUpdates.CountInstallsAsync(platform, group.Channel, null, active, token).ConfigureAwait(false);
            view.Adoption = new UpdateAdoptionSummaryView { ActiveInstalls = activeInstalls, OnThisUpdate = health.Successes };
            foreach (UpdateView u in view.Updates) u.SourceMapCount = await _Database.LiveUpdates.CountSourceMapsAsync(u.Id, token).ConfigureAwait(false);
            UpdateRecord? first = updates.FirstOrDefault(u => u.LaunchAssetSha256 != null);
            if (first != null)
            {
                EnumerationRequest request = _Enumeration.Prepare(new EnumerationQuery { MaxResults = 20 }, LiveUpdateCollections.UpdateAssets, true, first.Id);
                EnumerationResult<UpdateAssetRef> page = await _Database.LiveUpdates.EnumerateAssetRefsAsync(request, first.Id, token).ConfigureAwait(false);
                view.Assets = new UpdateAssetPreview { Objects = page.Objects, TotalRecords = page.TotalRecords ?? page.Objects.Count, Enumerate = _Base + "/updates/" + first.Id + "/assets/enumerate" };
            }
            else view.Assets = new UpdateAssetPreview();
            return view;
        }

        /// <summary>
        /// POST /api/v1/admin/update-groups/enumerate.
        /// </summary>
        /// <param name="query">Query.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Page of UpdateGroup payloads.</returns>
        /// <exception cref="ApiErrorException">400 ValidationFailed.</exception>
        public async Task<EnumerationResult<UpdateGroupView>> EnumerateGroupsAsync(EnumerationQuery? query, CancellationToken token = default)
        {
            EnumerationRequest request = _Enumeration.Prepare(query, LiveUpdateCollections.UpdateGroups, true);
            Dictionary<string, string> errors = new Dictionary<string, string>();
            UpdateGroupFilter filter = new UpdateGroupFilter
            {
                Platform = request.Query.FilterString("platform"),
                Channel = request.Query.FilterString("channel"),
                RuntimeVersion = request.Query.FilterString("runtimeVersion"),
                Status = request.Query.FilterEnum<UpdateGroupStatusEnum>("status", errors),
                Kind = request.Query.FilterEnum<UpdateGroupKindEnum>("kind", errors),
                Critical = request.Query.FilterBool("critical", errors),
                GitCommit = request.Query.FilterString("gitCommit")
            };
            if (filter.Platform != null && !LiveUpdateRules.IsPlatform(filter.Platform)) errors["filters.platform"] = "Must be ios or android.";
            if (errors.Count > 0) throw ApiErrorException.Validation(errors);
            EnumerationResult<UpdateGroup> page = await _Database.LiveUpdates.EnumerateGroupsAsync(request, filter, token).ConfigureAwait(false);
            List<UpdateGroupView> views = new List<UpdateGroupView>();
            foreach (UpdateGroup group in page.Objects) views.Add(await ViewAsync(group, false, token).ConfigureAwait(false));
            return page.With(views);
        }

        /// <summary>
        /// GET /api/v1/admin/update-groups/{id}.
        /// </summary>
        /// <param name="id">Group id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Detail payload.</returns>
        /// <exception cref="ApiErrorException">404 NotFound.</exception>
        public async Task<UpdateGroupView> GetGroupAsync(string id, CancellationToken token = default)
        {
            UpdateGroup group = await _Database.LiveUpdates.ReadGroupAsync(id, token).ConfigureAwait(false) ?? throw ApiErrorException.NotFound("Update group");
            return await ViewAsync(group, true, token).ConfigureAwait(false);
        }

        /// <summary>
        /// POST /api/v1/admin/updates/{id}/assets/enumerate.
        /// </summary>
        /// <param name="updateId">Update id.</param>
        /// <param name="query">Query.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Page of assets.</returns>
        /// <exception cref="ApiErrorException">404 NotFound.</exception>
        public async Task<EnumerationResult<UpdateAssetRef>> EnumerateAssetsAsync(string updateId, EnumerationQuery? query, CancellationToken token = default)
        {
            UpdateRecord update = await _Database.LiveUpdates.ReadUpdateAsync(updateId, token).ConfigureAwait(false) ?? throw ApiErrorException.NotFound("Update");
            EnumerationRequest request = _Enumeration.Prepare(query, LiveUpdateCollections.UpdateAssets, true, update.Id);
            return await _Database.LiveUpdates.EnumerateAssetRefsAsync(request, update.Id, token).ConfigureAwait(false);
        }

        /// <summary>Enumerate adoption day buckets with scalar platform context.</summary>
        /// <param name="id">Group id.</param>
        /// <param name="query">Enumeration query with optional days filter (1 to 90, default 14).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Day page.</returns>
        public async Task<CatalogEnumerationResponse<UpdateAdoptionDay>> AdoptionAsync(string id, EnumerationQuery? query, CancellationToken token = default)
        {
            EnumerationRequest request = _Enumeration.Prepare(query, LiveUpdateCollections.AdoptionDays, true, id);
            int days = 14;
            string? rawDays = request.Query.FilterString("days");
            if (rawDays != null && (!Int32.TryParse(rawDays, NumberStyles.Integer, CultureInfo.InvariantCulture, out days) || days < 1 || days > 90))
                throw ApiErrorException.Field("filters.days", "Must be 1 to 90.");
            UpdateGroup group = await _Database.LiveUpdates.ReadGroupAsync(id, token).ConfigureAwait(false) ?? throw ApiErrorException.NotFound("Update group");
            List<UpdateRecord> updates = await _Database.LiveUpdates.ListGroupUpdatesAsync(group.Id, token).ConfigureAwait(false);
            DateTime now = DateTime.UtcNow;
            DateTime active = now.AddDays(-30);
            string since = now.AddDays(1 - days).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            UpdateAdoptionView view = new UpdateAdoptionView { Days = days };
            foreach (UpdateRecord update in updates)
            {
                List<UpdateCountRow> counts = await _Database.LiveUpdates.EventCountsAsync(new List<string> { update.Uuid }, since, token).ConfigureAwait(false);
                long activeInstalls = await _Database.LiveUpdates.CountInstallsAsync(update.Platform, group.Channel, null, active, token).ConfigureAwait(false);
                long onUpdate = await _Database.LiveUpdates.CountInstallsAsync(update.Platform, null, update.Uuid, active, token).ConfigureAwait(false);
                view.ByPlatform[update.Platform] = new UpdatePlatformAdoption
                {
                    ActiveInstalls = activeInstalls,
                    OnThisUpdate = onUpdate,
                    PercentOnThisUpdate = activeInstalls == 0 ? 0 : Math.Round(onUpdate * 100m / activeInstalls, 2),
                    Downloaded = Sum(counts, UpdateEventTypeEnum.Downloaded.ToString()),
                    Applied = Sum(counts, UpdateEventTypeEnum.Applied.ToString()),
                    Failed = Sum(counts, UpdateEventTypeEnum.DownloadFailed.ToString()) + Sum(counts, UpdateEventTypeEnum.CheckFailed.ToString()),
                    LaunchFailed = Sum(counts, UpdateEventTypeEnum.LaunchFailed.ToString())
                };
            }
            List<UpdateCountRow> all = await _Database.LiveUpdates.EventCountsAsync(updates.Select(u => u.Uuid).ToList(), since, token).ConfigureAwait(false);
            for (int i = days - 1; i >= 0; i--)
            {
                string day = now.AddDays(-i).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                List<UpdateCountRow> rows = all.Where(r => r.Key == day).ToList();
                view.ByDay.Add(new UpdateAdoptionDay
                {
                    Date = day,
                    Checks = Sum(rows, UpdateManifestService.OfferedCount),
                    Downloads = Sum(rows, UpdateEventTypeEnum.Downloaded.ToString()),
                    Applied = Sum(rows, UpdateEventTypeEnum.Applied.ToString()),
                    Failures = Sum(rows, UpdateEventTypeEnum.DownloadFailed.ToString()) + Sum(rows, UpdateEventTypeEnum.LaunchFailed.ToString()) + Sum(rows, UpdateEventTypeEnum.CheckFailed.ToString())
                });
            }
            EnumerationResult<UpdateAdoptionDay> page = CatalogEnumeration.Page(request, view.ByDay, d => d.Date,
                created: d => DateTime.Parse(d.Date, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal), search: d => d.Date);
            return CatalogEnumerationResponse<UpdateAdoptionDay>.From(page, new Dictionary<string, object?> { { "days", days }, { "byPlatform", view.ByPlatform } });
        }

        /// <summary>Enumerate install counts by binary version for a group's platform updates.</summary>
        /// <param name="id">Group id.</param>
        /// <param name="query">Enumeration query.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Version count page.</returns>
        public async Task<EnumerationResult<UpdateCountRow>> EnumerateAdoptionBinariesAsync(string id, EnumerationQuery? query, CancellationToken token = default)
        {
            EnumerationRequest request = _Enumeration.Prepare(query, LiveUpdateCollections.AdoptionBinaries, true, id);
            UpdateGroup group = await _Database.LiveUpdates.ReadGroupAsync(id, token).ConfigureAwait(false) ?? throw ApiErrorException.NotFound("Update group");
            List<UpdateRecord> updates = await _Database.LiveUpdates.ListGroupUpdatesAsync(group.Id, token).ConfigureAwait(false);
            return await _Database.LiveUpdates.EnumerateInstallCountsAsync(request, "appversion", updates.Select(u => u.Uuid).ToList(), DateTime.UtcNow.AddDays(-30), token).ConfigureAwait(false);
        }

        /// <summary>
        /// POST /api/v1/admin/updates/channels/enumerate.
        /// </summary>
        /// <param name="query">Query.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Page of channels.</returns>
        /// <exception cref="ApiErrorException">400 ValidationFailed.</exception>
        public async Task<EnumerationResult<UpdateChannelView>> EnumerateChannelsAsync(EnumerationQuery? query, CancellationToken token = default)
        {
            await EnsureChannelsAsync(token).ConfigureAwait(false);
            EnumerationRequest request = _Enumeration.Prepare(query, LiveUpdateCollections.Channels, true);
            Dictionary<string, string> errors = new Dictionary<string, string>();
            bool? builtIn = request.Query.FilterBool("builtIn", errors);
            if (errors.Count > 0) throw ApiErrorException.Validation(errors);
            EnumerationResult<UpdateChannel> page = await _Database.LiveUpdates.EnumerateChannelsAsync(request, builtIn, token).ConfigureAwait(false);
            List<UpdateChannelView> views = new List<UpdateChannelView>();
            foreach (UpdateChannel channel in page.Objects) views.Add(await ChannelViewAsync(channel, token).ConfigureAwait(false));
            return page.With(views);
        }

        /// <summary>
        /// POST /api/v1/admin/updates/channels. Audited updates.channel.create.
        /// </summary>
        /// <param name="request">Body.</param>
        /// <param name="info">Request information.</param>
        /// <param name="audit">Audit service.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Channel.</returns>
        /// <exception cref="ApiErrorException">400 ValidationFailed, 409 Conflict.</exception>
        public async Task<UpdateChannelView> CreateChannelAsync(UpdateChannelRequest request, RequestInfo info, AuditService audit, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            ArgumentNullException.ThrowIfNull(audit);
            await EnsureChannelsAsync(token).ConfigureAwait(false);
            string name = (request.Name ?? String.Empty).Trim();
            if (!LiveUpdateRules.ChannelPattern.IsMatch(name)) throw ApiErrorException.Field("name", "Must match ^[a-z][a-z0-9-]{1,31}$.");
            List<UpdateChannel> existing = await _Database.LiveUpdates.ListChannelsAsync(_Settings.Updates.MaxChannels + 10, token).ConfigureAwait(false);
            if (existing.Any(c => c.Name == name)) throw new ApiErrorException(409, "Conflict", "Channel " + name + " exists.");
            if (existing.Count(c => !c.BuiltIn) >= _Settings.Updates.MaxChannels) throw ApiErrorException.Field("name", "At most " + _Settings.Updates.MaxChannels + " custom channels (updates.maxChannels).");
            UpdateChannel channel = new UpdateChannel { Name = name, BuiltIn = false, CreatedUtc = DateTime.UtcNow };
            try
            {
                await _Database.LiveUpdates.CreateChannelAsync(channel, token).ConfigureAwait(false);
            }
            catch (DuplicateRecordException)
            {
                throw new ApiErrorException(409, "Conflict", "Channel " + name + " exists.");
            }
            await audit.WriteAsync(info, "updates.channel.create", "UpdateChannel", name, null, token).ConfigureAwait(false);
            return await ChannelViewAsync(channel, token).ConfigureAwait(false);
        }

        /// <summary>
        /// DELETE /api/v1/admin/updates/channels/{name}: custom channels without live groups. Audited.
        /// </summary>
        /// <param name="name">Channel.</param>
        /// <param name="info">Request information.</param>
        /// <param name="audit">Audit service.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        /// <exception cref="ApiErrorException">404 NotFound, 409 Conflict.</exception>
        public async Task DeleteChannelAsync(string name, RequestInfo info, AuditService audit, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(audit);
            UpdateChannel channel = await _Database.LiveUpdates.ReadChannelAsync(name, token).ConfigureAwait(false) ?? throw ApiErrorException.NotFound("Channel");
            if (channel.BuiltIn) throw new ApiErrorException(409, "Conflict", "Built-in channels cannot be deleted.");
            if (await _Database.LiveUpdates.CountLiveGroupsInChannelAsync(name, token).ConfigureAwait(false) > 0) throw new ApiErrorException(409, "Conflict", "The channel has published updates; roll them back or wait until they are superseded.");
            await _Database.LiveUpdates.DeleteChannelAsync(name, token).ConfigureAwait(false);
            await audit.WriteAsync(info, "updates.channel.delete", "UpdateChannel", name, null, token).ConfigureAwait(false);
        }

        /// <summary>
        /// POST /api/v1/admin/updates/runtime-versions/enumerate (section 7.5).
        /// </summary>
        /// <param name="query">Query.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Page.</returns>
        /// <exception cref="ApiErrorException">400 ValidationFailed.</exception>
        public async Task<EnumerationResult<UpdateRuntimeVersionView>> EnumerateRuntimeVersionsAsync(EnumerationQuery? query, CancellationToken token = default)
        {
            EnumerationRequest request = _Enumeration.Prepare(query, LiveUpdateCollections.RuntimeVersions, true);
            string? platform = request.Query.FilterString("platform");
            string? channel = request.Query.FilterString("channel");
            if (platform != null && !LiveUpdateRules.IsPlatform(platform)) throw ApiErrorException.Field("filters.platform", "Must be ios or android.");
            DateTime active = DateTime.UtcNow.AddDays(-30);
            EnumerationResult<UpdateRuntimeVersionRow> page = await _Database.LiveUpdates.EnumerateRuntimeVersionsAsync(request, platform, channel, active, token).ConfigureAwait(false);
            List<UpdateRuntimeVersionView> views = new List<UpdateRuntimeVersionView>();
            foreach (UpdateRuntimeVersionRow row in page.Objects)
            {
                List<UpdateRecord> latest = await _Database.LiveUpdates.ListCandidatesAsync(row.Platform, row.RuntimeVersion, channel ?? LiveUpdateRules.ProductionChannel, 1, token).ConfigureAwait(false);
                views.Add(new UpdateRuntimeVersionView
                {
                    Platform = row.Platform,
                    RuntimeVersion = row.RuntimeVersion,
                    FirstSeenUtc = row.FirstSeenUtc,
                    LastSeenUtc = row.LastSeenUtc,
                    ActiveInstalls30d = row.ActiveInstalls30d,
                    LatestUpdate = latest.Count == 0 ? null : LiveUpdateRules.ViewOf(latest[0]),
                    BinaryVersions = new UpdateCountPreview
                    {
                        Objects = await _Database.LiveUpdates.CountInstallsByAsync("appversion", row.Platform, row.RuntimeVersion, null, active, 10, token).ConfigureAwait(false),
                        TotalRecords = await _Database.LiveUpdates.CountDistinctInstallValuesAsync("appversion", row.Platform, row.RuntimeVersion, null, active, token).ConfigureAwait(false)
                    }
                });
            }
            return page.With(views);
        }

        /// <summary>
        /// Enumerate installs by embedded certificate key id (rotation drain).
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Counts.</returns>
        /// <param name="query">Enumeration query.</param>
        public Task<EnumerationResult<UpdateCountRow>> InstallsByKeyIdAsync(EnumerationQuery? query, CancellationToken token = default)
        {
            EnumerationRequest request = _Enumeration.Prepare(query, LiveUpdateCollections.SigningKeyInstalls, true);
            return _Database.LiveUpdates.EnumerateInstallCountsAsync(request, "keyid", null, DateTime.UtcNow.AddDays(-30), token);
        }

        /// <summary>
        /// POST /api/v1/admin/updates/deploy-keys: returns the secret once. Audited updates.deployKey.create.
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="request">Body.</param>
        /// <param name="info">Request information.</param>
        /// <param name="audit">Audit service.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Deploy key with its secret.</returns>
        /// <exception cref="ApiErrorException">400 ValidationFailed, 403 Forbidden.</exception>
        public async Task<UpdateDeployKeyView> CreateDeployKeyAsync(ContentActor actor, UpdateDeployKeyRequest request, RequestInfo info, AuditService audit, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            ArgumentNullException.ThrowIfNull(request);
            ArgumentNullException.ThrowIfNull(audit);
            if (info.Context != null && info.Context.DeployKeyId != null) throw ApiErrorException.Forbidden("Deploy keys cannot manage deploy keys.");
            Dictionary<string, string> errors = new Dictionary<string, string>();
            string name = (request.Name ?? String.Empty).Trim();
            if (name.Length < 1 || name.Length > 100) errors["name"] = "Must be 1 to 100 characters.";
            List<string> channels = (request.Channels ?? new List<string>()).Select(c => (c ?? String.Empty).Trim()).Distinct(StringComparer.Ordinal).ToList();
            if (channels.Count == 0) errors["channels"] = "At least one channel is required.";
            else if (channels.Contains(LiveUpdateRules.ProductionChannel)) errors["channels"] = "Deploy keys never publish to production; admins publish or promote to production.";
            else
            {
                foreach (string channel in channels)
                    if (!await ChannelExistsAsync(channel, token).ConfigureAwait(false)) errors["channels"] = "Unknown channel " + channel + ".";
            }
            if (request.ExpiresUtc != null && request.ExpiresUtc.Value <= DateTime.UtcNow) errors["expiresUtc"] = "Must be in the future.";
            if (errors.Count > 0) throw ApiErrorException.Validation(errors);
            await _Metadata.ValidateAsync(actor, request.Labels, request.Tags, true, token, TargetTypeEnum.UpdateDeployKey).ConfigureAwait(false);
            string secret = DeployKeyPrefix + Base64UrlSecret();
            UpdateDeployKey key = new UpdateDeployKey
            {
                Id = IdGenerator.Generate("udk_"),
                Name = name,
                SecretHash = Hash(secret),
                Channels = channels,
                ExpiresUtc = request.ExpiresUtc,
                CreatedByUserId = info.Context?.UserId,
                CreatedUtc = DateTime.UtcNow
            };
            await _Database.LiveUpdates.CreateDeployKeyAsync(key, token).ConfigureAwait(false);
            ObjectMetadata metadata = await _Metadata.ApplyAsync(actor, TargetTypeEnum.UpdateDeployKey, key.Id, request.Labels, request.Tags, token).ConfigureAwait(false);
            await audit.WriteAsync(info, "updates.deployKey.create", "UpdateDeployKey", key.Id, name + " (" + String.Join(", ", channels) + ")", token).ConfigureAwait(false);
            UpdateDeployKeyView view = DeployKeyView(key, metadata);
            view.Secret = secret;
            return view;
        }

        /// <summary>
        /// POST /api/v1/admin/updates/deploy-keys/enumerate (filter active).
        /// </summary>
        /// <param name="query">Query.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Page.</returns>
        /// <exception cref="ApiErrorException">400 ValidationFailed.</exception>
        public async Task<EnumerationResult<UpdateDeployKeyView>> EnumerateDeployKeysAsync(EnumerationQuery? query, CancellationToken token = default)
        {
            EnumerationRequest request = _Enumeration.Prepare(query, LiveUpdateCollections.DeployKeys, true);
            Dictionary<string, string> errors = new Dictionary<string, string>();
            bool? active = request.Query.FilterBool("active", errors);
            if (errors.Count > 0) throw ApiErrorException.Validation(errors);
            EnumerationResult<UpdateDeployKey> page = await _Database.LiveUpdates.EnumerateDeployKeysAsync(request, active, DateTime.UtcNow, token).ConfigureAwait(false);
            Dictionary<string, ObjectMetadata> metadata = await _Metadata.LoadAsync(TargetTypeEnum.UpdateDeployKey, page.Objects.Select(k => k.Id), true, token).ConfigureAwait(false);
            return page.With(page.Objects.Select(k => DeployKeyView(k, metadata.TryGetValue(k.Id, out ObjectMetadata? m) ? m : new ObjectMetadata())).ToList());
        }

        /// <summary>
        /// POST /api/v1/admin/updates/deploy-keys/{id}/revoke. Audited.
        /// </summary>
        /// <param name="id">Key id.</param>
        /// <param name="info">Request information.</param>
        /// <param name="audit">Audit service.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Key.</returns>
        /// <exception cref="ApiErrorException">403 Forbidden, 404 NotFound.</exception>
        public async Task<UpdateDeployKeyView> RevokeDeployKeyAsync(string id, RequestInfo info, AuditService audit, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(audit);
            if (info.Context != null && info.Context.DeployKeyId != null) throw ApiErrorException.Forbidden("Deploy keys cannot manage deploy keys.");
            UpdateDeployKey key = await _Database.LiveUpdates.ReadDeployKeyAsync(id, token).ConfigureAwait(false) ?? throw ApiErrorException.NotFound("Deploy key");
            await _Database.LiveUpdates.RevokeDeployKeyAsync(key.Id, DateTime.UtcNow, token).ConfigureAwait(false);
            await audit.WriteAsync(info, "updates.deployKey.revoke", "UpdateDeployKey", key.Id, key.Name, token).ConfigureAwait(false);
            UpdateDeployKey revoked = (await _Database.LiveUpdates.ReadDeployKeyAsync(id, token).ConfigureAwait(false))!;
            return DeployKeyView(revoked, await _Metadata.LoadOneAsync(TargetTypeEnum.UpdateDeployKey, id, true, token).ConfigureAwait(false));
        }

        /// <summary>
        /// Find an active deploy key by its presented secret and record the use.
        /// </summary>
        /// <param name="secret">tkdk_ secret.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Key, or null when unknown, revoked, or expired.</returns>
        public async Task<UpdateDeployKey?> AuthenticateDeployKeyAsync(string secret, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(secret) || !secret.StartsWith(DeployKeyPrefix, StringComparison.Ordinal)) return null;
            UpdateDeployKey? key = await _Database.LiveUpdates.ReadDeployKeyBySecretAsync(Hash(secret), token).ConfigureAwait(false);
            DateTime now = DateTime.UtcNow;
            if (key == null || key.RevokedUtc != null || (key.ExpiresUtc != null && key.ExpiresUtc <= now)) return null;
            await _Database.LiveUpdates.TouchDeployKeyAsync(key.Id, now, token).ConfigureAwait(false);
            return key;
        }

        /// <summary>
        /// POST /api/v1/admin/updates/signing-keys/enumerate.
        /// </summary>
        /// <param name="query">Query.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Page.</returns>
        /// <exception cref="ApiErrorException">400 ValidationFailed.</exception>
        public async Task<EnumerationResult<UpdateSigningKeyView>> EnumerateSigningKeysAsync(EnumerationQuery? query, CancellationToken token = default)
        {
            await _Signing.LoadAsync(token).ConfigureAwait(false);
            EnumerationRequest request = _Enumeration.Prepare(query, LiveUpdateCollections.SigningKeys, true);
            Dictionary<string, string> errors = new Dictionary<string, string>();
            UpdateSigningKeyStatusEnum? status = request.Query.FilterEnum<UpdateSigningKeyStatusEnum>("status", errors);
            if (errors.Count > 0) throw ApiErrorException.Validation(errors);
            EnumerationResult<UpdateSigningKey> page = await _Database.LiveUpdates.EnumerateSigningKeysAsync(request, status, token).ConfigureAwait(false);
            List<UpdateSigningMaterial> ring = await _Signing.LoadAsync(token).ConfigureAwait(false);
            Dictionary<string, ObjectMetadata> metadata = await _Metadata.LoadAsync(TargetTypeEnum.UpdateSigningKey, page.Objects.Select(k => k.Id), true, token).ConfigureAwait(false);
            return page.With(page.Objects.Select(k => SigningKeyView(k, ring.FirstOrDefault(m => m.Record.Id == k.Id), metadata.TryGetValue(k.Id, out ObjectMetadata? m) ? m : new ObjectMetadata())).ToList());
        }

        /// <summary>
        /// The payload of one signing key.
        /// </summary>
        /// <param name="id">Record id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>View.</returns>
        /// <exception cref="ApiErrorException">404 NotFound.</exception>
        public async Task<UpdateSigningKeyView> SigningKeyViewAsync(string id, CancellationToken token = default)
        {
            List<UpdateSigningMaterial> ring = await _Signing.LoadAsync(token, true).ConfigureAwait(false);
            UpdateSigningMaterial material = ring.FirstOrDefault(m => m.Record.Id == id) ?? throw ApiErrorException.NotFound("Signing key");
            return SigningKeyView(material.Record, material, await _Metadata.LoadOneAsync(TargetTypeEnum.UpdateSigningKey, id, true, token).ConfigureAwait(false));
        }

        /// <summary>
        /// GET /api/v1/admin/updates/health.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Health.</returns>
        public async Task<UpdateHealthView> HealthAsync(CancellationToken token = default)
        {
            await EnsureChannelsAsync(token).ConfigureAwait(false);
            string? problem = await _Signing.ProblemAsync(token).ConfigureAwait(false);
            UpdateSigningMaterial? active = await _Signing.ActiveAsync(token).ConfigureAwait(false);
            string storage = "Ok";
            try
            {
                await _Store.HeadAsync(UpdateObjectStore.AssetKey(_PropertyId, new string('0', 64)), token).ConfigureAwait(false);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                storage = "Error";
            }
            UpdateHealthView view = new UpdateHealthView
            {
                Enabled = _Settings.Updates.Enabled,
                Signing = problem == null ? "Active" : "Missing",
                ActiveKeyId = active?.Record.KeyId,
                SigningProblem = problem,
                Storage = storage,
                StorageMode = _Store.Mode.ToString(),
                RolloutPlanTemplate = _Settings.Updates.RolloutPlanTemplate
            };
            return view;
        }

        /// <summary>
        /// SHA-256 hex of a secret.
        /// </summary>
        /// <param name="secret">Secret.</param>
        /// <returns>Hex.</returns>
        public static string Hash(string secret)
        {
            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(secret ?? String.Empty))).ToLowerInvariant();
        }

        #endregion

        #region Private-Methods

        private async Task<UpdateChannelView> ChannelViewAsync(UpdateChannel channel, CancellationToken token)
        {
            UpdateChannelView view = new UpdateChannelView
            {
                Name = channel.Name,
                BuiltIn = channel.BuiltIn,
                Promotable = channel.Name != LiveUpdateRules.DevelopmentChannel,
                CreatedUtc = channel.CreatedUtc,
                ActiveInstalls30d = await _Database.LiveUpdates.CountInstallsAsync(null, channel.Name, null, DateTime.UtcNow.AddDays(-30), token).ConfigureAwait(false)
            };
            foreach (string platform in LiveUpdateRules.Platforms)
            {
                UpdateRecord? update = await _Database.LiveUpdates.ReadLatestAsync(channel.Name, platform, token).ConfigureAwait(false);
                view.LatestByPlatform[platform] = update == null ? null : LiveUpdateRules.ViewOf(update);
            }
            return view;
        }

        private static UpdateDeployKeyView DeployKeyView(UpdateDeployKey key, ObjectMetadata metadata)
        {
            DateTime now = DateTime.UtcNow;
            return new UpdateDeployKeyView
            {
                Id = key.Id,
                Name = key.Name,
                Channels = key.Channels,
                Active = key.RevokedUtc == null && (key.ExpiresUtc == null || key.ExpiresUtc > now),
                ExpiresUtc = key.ExpiresUtc,
                LastUsedUtc = key.LastUsedUtc,
                RevokedUtc = key.RevokedUtc,
                Labels = metadata.Labels,
                Tags = metadata.Tags,
                CreatedUtc = key.CreatedUtc
            };
        }

        private static UpdateSigningKeyView SigningKeyView(UpdateSigningKey key, UpdateSigningMaterial? material, ObjectMetadata metadata)
        {
            return new UpdateSigningKeyView
            {
                Id = key.Id,
                KeyId = key.KeyId,
                Algorithm = key.Algorithm,
                KeyBits = key.KeyBits,
                Source = key.Source,
                PrivateKeyAvailable = material != null && material.PrivateKey != null,
                CertificateSha256 = key.CertificateSha256,
                CertificateNotAfterUtc = key.CertificateNotAfterUtc,
                CertificateSubject = material?.Certificate?.Subject ?? String.Empty,
                IssuedByKeyId = material?.IssuedByKeyId,
                IsCertificateAuthority = key.IsCertificateAuthority,
                Status = key.Status,
                LastUsedUtc = key.LastUsedUtc,
                Labels = metadata.Labels,
                Tags = metadata.Tags,
                CreatedUtc = key.CreatedUtc,
                RetiredUtc = key.RetiredUtc
            };
        }

        private static long Sum(List<UpdateCountRow> rows, string type)
        {
            return rows.Where(r => r.Type == type).Sum(r => r.Count);
        }

        private static string Base64UrlSecret()
        {
            return LiveUpdateRules.Base64Url(RandomNumberGenerator.GetBytes(32));
        }

        #endregion
    }
}
