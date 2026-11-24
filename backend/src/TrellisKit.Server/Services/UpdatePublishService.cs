namespace TrellisKit.Server.Services
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.IO;
    using System.Linq;
    using System.Security.Cryptography;
    using System.Text;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core.Database;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Exceptions;
    using TrellisKit.Core.Helpers;
    using TrellisKit.Core.Models;
    using TrellisKit.Core.Observability;
    using TrellisKit.Core.Requests;
    using TrellisKit.Core.Responses;
    using TrellisKit.Core.Security;
    using TrellisKit.Core.Settings;
    using TrellisKit.Server.Serialization;

    /// <summary>
    /// Live update publishing (docs/LIVE_UPDATES_CONTRACT.md sections 7 and 10 with the client implementation notes):
    /// the streaming multipart upload from the publish tool, update groups, publish, rollout percentages and
    /// schedules with the automatic failure halt, pause and resume, critical marking, rollback, rollback to the
    /// embedded bundle, and promotion between channels (never from development).
    /// Thread safety: safe to call concurrently.
    /// </summary>
    public class UpdatePublishService
    {
        #region Public-Members

        /// <summary>
        /// Largest update descriptor part in bytes. Default 8388608. Minimum 65536, maximum 67108864.
        /// </summary>
        public int MaxDescriptorBytes
        {
            get { return _MaxDescriptorBytes; }
            set { _MaxDescriptorBytes = Math.Clamp(value, 65536, 67108864); }
        }

        /// <summary>
        /// Largest expoConfig in bytes. Default 262144.
        /// </summary>
        public int MaxExpoConfigBytes { get; } = 262144;

        /// <summary>
        /// Minutes within which a second platform without updateGroupId joins a group with the same channel, commit, and
        /// message. Default 10.
        /// </summary>
        public int JoinWindowMinutes { get; } = 10;

        #endregion

        #region Private-Members

        private readonly DatabaseDriverBase _Database;
        private readonly AppSettings _Settings;
        private readonly UpdateAdminService _Admin;
        private readonly UpdateSigningService _Signing;
        private readonly UpdateObjectStore _Store;
        private readonly MetadataService _Metadata;
        private readonly AuditService _Audit;
        private readonly AppLogger _Logger;
        private readonly string _PropertyId;
        private int _MaxDescriptorBytes = 8388608;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the service.
        /// </summary>
        /// <param name="database">Property-bound database driver.</param>
        /// <param name="settings">Running settings.</param>
        /// <param name="admin">Admin service (payloads, channels).</param>
        /// <param name="signing">Signing service.</param>
        /// <param name="store">Update storage.</param>
        /// <param name="metadata">Metadata service.</param>
        /// <param name="audit">Audit service.</param>
        /// <param name="logger">Logger.</param>
        /// <param name="propertyId">Property id.</param>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        public UpdatePublishService(DatabaseDriverBase database, AppSettings settings, UpdateAdminService admin, UpdateSigningService signing, UpdateObjectStore store, MetadataService metadata, AuditService audit, AppLogger logger, string propertyId)
        {
            _Database = database ?? throw new ArgumentNullException(nameof(database));
            _Settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _Admin = admin ?? throw new ArgumentNullException(nameof(admin));
            _Signing = signing ?? throw new ArgumentNullException(nameof(signing));
            _Store = store ?? throw new ArgumentNullException(nameof(store));
            _Metadata = metadata ?? throw new ArgumentNullException(nameof(metadata));
            _Audit = audit ?? throw new ArgumentNullException(nameof(audit));
            _Logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _PropertyId = propertyId ?? throw new ArgumentNullException(nameof(propertyId));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// POST /api/v1/admin/updates: one platform's update as multipart/form-data (an update JSON part, then one part per
        /// asset named by its key, then sourcemap-&lt;md5&gt; parts). Every part is streamed to a temporary file while its
        /// SHA-256 and MD5 are computed, verified against the descriptor, and stored content addressed.
        /// </summary>
        /// <param name="actor">Caller (metadata).</param>
        /// <param name="body">Request body stream.</param>
        /// <param name="contentType">Request Content-Type.</param>
        /// <param name="contentLength">Declared length, or 0 when chunked.</param>
        /// <param name="info">Request information.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The update group.</returns>
        /// <exception cref="ApiErrorException">400 ValidationFailed, UnknownChannel, UnsupportedPlatform, AssetMismatch; 403
        /// Forbidden; 409 AssetsMissing, RolloutConflict, Conflict; 413 TooLarge; 503 SigningUnavailable (kept as Draft).</exception>
        public async Task<UpdateGroupView> UploadAsync(ContentActor actor, Stream body, string? contentType, long contentLength, RequestInfo info, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            ArgumentNullException.ThrowIfNull(body);
            ArgumentNullException.ThrowIfNull(info);
            long maxUpload = (long)_Settings.Updates.MaxUploadMegabytes * 1024 * 1024;
            if (contentLength > maxUpload) throw TooLarge("The upload is larger than updates.maxUploadMegabytes (" + _Settings.Updates.MaxUploadMegabytes + ").");
            string? boundary = MultipartFormReader.BoundaryOf(contentType);
            if (contentType == null || !contentType.StartsWith("multipart/form-data", StringComparison.OrdinalIgnoreCase) || boundary == null)
                throw new ApiErrorException(400, "ValidationFailed", "Send multipart/form-data with an update part and one part per asset.");

            MultipartFormReader reader = new MultipartFormReader(body, boundary);
            UpdateDescriptor descriptor;
            List<string> stored = new List<string>();
            string tempRoot = Path.Combine(_Settings.Storage.Directory, "updates-upload", Guid.NewGuid().ToString("N"));
            try
            {
                MultipartSection? first = await reader.NextAsync(token).ConfigureAwait(false);
                if (first == null || first.Name != "update") throw new ApiErrorException(400, "ValidationFailed", "The first part must be the update descriptor named update.");
                MemoryStream json = new MemoryStream();
                await reader.ReadBodyAsync((b, o, c) => json.WriteAsync(b, o, c, token), _MaxDescriptorBytes, token).ConfigureAwait(false);
                try
                {
                    descriptor = ApiJson.Deserialize<UpdateDescriptor>(Encoding.UTF8.GetString(json.ToArray())) ?? throw new ApiErrorException(400, "InvalidJson", "The update part is empty.");
                }
                catch (JsonException)
                {
                    throw new ApiErrorException(400, "InvalidJson", "The update part is not valid JSON.");
                }
                Dictionary<string, UpdateDescriptorFile> files = await ValidateAsync(descriptor, info.Context, token).ConfigureAwait(false);
                await _Metadata.ValidateAsync(actor, descriptor.Labels, descriptor.Tags, true, token, TargetTypeEnum.UpdateGroup).ConfigureAwait(false);

                Directory.CreateDirectory(tempRoot);
                long maxAsset = (long)_Settings.Updates.MaxAssetMegabytes * 1024 * 1024;
                Dictionary<string, string> mismatches = new Dictionary<string, string>();
                HashSet<string> received = new HashSet<string>(StringComparer.Ordinal);
                long uploaded = 0;
                while (true)
                {
                    MultipartSection? section = await reader.NextAsync(token).ConfigureAwait(false);
                    if (section == null) break;
                    if (!files.TryGetValue(section.Name, out UpdateDescriptorFile? file))
                    {
                        await reader.ReadBodyAsync(null, maxUpload, token).ConfigureAwait(false);
                        throw ApiErrorException.Field(section.Name, "This part is not declared in the update descriptor.");
                    }
                    string temp = Path.Combine(tempRoot, received.Count.ToString(CultureInfo.InvariantCulture));
                    long size;
                    byte[] sha;
                    byte[] md5;
                    using (IncrementalHash shaHash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
                    using (IncrementalHash md5Hash = IncrementalHash.CreateHash(HashAlgorithmName.MD5))
                    {
                        await using (FileStream output = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true))
                        {
                            size = await reader.ReadBodyAsync(async (b, o, c) =>
                            {
                                shaHash.AppendData(b, o, c);
                                md5Hash.AppendData(b, o, c);
                                await output.WriteAsync(b.AsMemory(o, c), token).ConfigureAwait(false);
                            }, maxAsset, token).ConfigureAwait(false);
                        }
                        sha = shaHash.GetHashAndReset();
                        md5 = md5Hash.GetHashAndReset();
                    }
                    received.Add(section.Name);
                    string field = FieldOf(descriptor, file);
                    string shaHex = Convert.ToHexString(sha).ToLowerInvariant();
                    if (LiveUpdateRules.Base64Url(sha) != file.Hash) mismatches[field] = "SHA-256 does not match the declared hash.";
                    else if (Convert.ToHexString(md5).ToLowerInvariant() != file.Key) mismatches[field] = "MD5 does not match the declared key.";
                    else if (file.Size != null && file.Size.Value != size) mismatches[field] = "Size " + size + " does not match the declared size " + file.Size.Value + ".";
                    if (mismatches.Count > 0)
                    {
                        while (await reader.NextAsync(token).ConfigureAwait(false) != null) await reader.ReadBodyAsync(null, maxUpload, token).ConfigureAwait(false);
                        throw new ApiErrorException(400, "AssetMismatch", "An uploaded file does not match the descriptor.", mismatches);
                    }
                    bool isMap = descriptor.SourceMaps.Contains(file);
                    string key = isMap ? UpdateObjectStore.SourceMapKey(_PropertyId, shaHex) : UpdateObjectStore.AssetKey(_PropertyId, shaHex);
                    if (await _Store.HeadAsync(key, token).ConfigureAwait(false) == null)
                    {
                        await _Store.PutFileAsync(key, temp, isMap ? "application/json" : file.ContentType, token).ConfigureAwait(false);
                        uploaded += size;
                    }
                    if (!isMap)
                    {
                        await _Database.LiveUpdates.UpsertAssetAsync(new UpdateAsset { Sha256Hex = shaHex, HashB64Url = file.Hash, ContentType = file.ContentType, SizeBytes = size, StoredUtc = DateTime.UtcNow, LastReferencedUtc = DateTime.UtcNow }, token).ConfigureAwait(false);
                        file.Size = size;
                    }
                    else file.Size = size;
                    stored.Add(shaHex);
                    File.Delete(temp);
                }
                TrellisKitTelemetry.Add(LiveUpdatesTelemetry.AssetBytes, uploaded, "direction", "uploaded", "trelliskit.property", _PropertyId);
                await RequireAssetsAsync(descriptor, received, token).ConfigureAwait(false);
            }
            catch (InvalidDataException e)
            {
                throw new ApiErrorException(400, "ValidationFailed", "The multipart body is malformed: " + e.Message);
            }
            catch (PayloadTooLargeException e)
            {
                throw TooLarge(e.Message);
            }
            finally
            {
                try
                {
                    if (Directory.Exists(tempRoot)) Directory.Delete(tempRoot, true);
                }
                catch (IOException)
                {
                    // Temporary files are best-effort cleanup.
                }
            }
            return await CreateFromDescriptorAsync(actor, descriptor, info, token).ConfigureAwait(false);
        }

        /// <summary>
        /// HEAD /api/v1/admin/updates/assets/{sha256Hex}: whether the property stores an asset.
        /// </summary>
        /// <param name="sha256Hex">SHA-256 hex.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True when stored.</returns>
        public async Task<bool> HasAssetAsync(string sha256Hex, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(sha256Hex) || !LiveUpdateRules.Sha256HexPattern.IsMatch(sha256Hex)) return false;
            HashSet<string> stored = await _Database.LiveUpdates.StoredAssetsAsync(new[] { sha256Hex }, token).ConfigureAwait(false);
            return stored.Contains(sha256Hex);
        }

        /// <summary>
        /// POST /api/v1/admin/update-groups/{id}/publish.
        /// </summary>
        /// <param name="id">Group id.</param>
        /// <param name="request">Body.</param>
        /// <param name="info">Request information.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The group.</returns>
        /// <exception cref="ApiErrorException">403, 404, 409 UpdateNotDraft or RolloutConflict, 503 SigningUnavailable.</exception>
        public async Task<UpdateGroupView> PublishAsync(string id, UpdateRolloutRequest request, RequestInfo info, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            UpdateGroup group = await RequireGroupAsync(id, token).ConfigureAwait(false);
            RequireChannelAccess(info.Context, group.Channel);
            if (group.Status != UpdateGroupStatusEnum.Draft) throw new ApiErrorException(409, "UpdateNotDraft", "Only Draft groups can be published.");
            int bp = request.RolloutPercentage == null ? (group.RolloutBasisPoints > 0 ? group.RolloutBasisPoints : DefaultRollout(group.Channel)) : Percent(request.RolloutPercentage.Value, "rolloutPercentage", false);
            List<UpdateRecord> updates = await _Database.LiveUpdates.ListGroupUpdatesAsync(group.Id, token).ConfigureAwait(false);
            await RequireSigningAsync(token).ConfigureAwait(false);
            foreach (UpdateRecord update in updates) await CheckRolloutConflictAsync(update, group.Id, bp, request.Supersede, info, token).ConfigureAwait(false);
            DateTime now = DateTime.UtcNow;
            group.Status = UpdateGroupStatusEnum.Published;
            group.RolloutBasisPoints = bp;
            group.PublishedUtc ??= now;
            group.UpdatedUtc = now;
            await _Database.LiveUpdates.SaveGroupAsync(group, true, token).ConfigureAwait(false);
            await AfterRolloutAsync(group, info, token).ConfigureAwait(false);
            await AuditAsync(info, "updates.publish", group, "Published at " + LiveUpdateRules.ToPercent(bp) + "%", "publish", token).ConfigureAwait(false);
            return await _Admin.ViewAsync(group, true, token).ConfigureAwait(false);
        }

        /// <summary>
        /// POST /api/v1/admin/update-groups/{id}/rollout: a manual override of the rollout percentage.
        /// </summary>
        /// <param name="id">Group id.</param>
        /// <param name="request">Body.</param>
        /// <param name="info">Request information, or null for the rollout worker.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The group.</returns>
        /// <exception cref="ApiErrorException">400, 404, 409 UpdateNotPublished.</exception>
        public async Task<UpdateGroupView> RolloutAsync(string id, UpdateRolloutRequest request, RequestInfo? info, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            if (request.RolloutPercentage == null) throw ApiErrorException.Field("rolloutPercentage", "Required, 0.01 to 100.");
            int bp = Percent(request.RolloutPercentage.Value, "rolloutPercentage", false);
            UpdateGroup group = await RequireGroupAsync(id, token).ConfigureAwait(false);
            if (group.Status != UpdateGroupStatusEnum.Published && group.Status != UpdateGroupStatusEnum.Paused) throw new ApiErrorException(409, "UpdateNotPublished", "Only Published or Paused groups have a rollout.");
            if (request.Platform != null && !LiveUpdateRules.IsPlatform(request.Platform)) throw ApiErrorException.Field("platform", "Must be ios or android.");
            DateTime now = DateTime.UtcNow;
            if (request.Platform != null)
            {
                List<UpdateRecord> updates = await _Database.LiveUpdates.ListGroupUpdatesAsync(group.Id, token).ConfigureAwait(false);
                UpdateRecord update = updates.FirstOrDefault(u => u.Platform == request.Platform) ?? throw ApiErrorException.Field("platform", "The group has no " + request.Platform + " update.");
                await _Database.LiveUpdates.SetUpdateRolloutAsync(update.Id, bp, now, token).ConfigureAwait(false);
                group.UpdatedUtc = now;
                await _Database.LiveUpdates.SaveGroupAsync(group, false, token).ConfigureAwait(false);
            }
            else
            {
                group.RolloutBasisPoints = bp;
                group.UpdatedUtc = now;
                await _Database.LiveUpdates.SaveGroupAsync(group, true, token).ConfigureAwait(false);
            }
            await AfterRolloutAsync(group, info, token).ConfigureAwait(false);
            await AuditAsync(info, "updates.rollout", group, (request.Platform ?? "all") + " at " + LiveUpdateRules.ToPercent(bp) + "%", "rollout", token).ConfigureAwait(false);
            return await _Admin.ViewAsync(group, true, token).ConfigureAwait(false);
        }

        /// <summary>
        /// POST /api/v1/admin/update-groups/{id}/pause.
        /// </summary>
        /// <param name="id">Group id.</param>
        /// <param name="request">Body.</param>
        /// <param name="info">Request information.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The group.</returns>
        /// <exception cref="ApiErrorException">404, 409 UpdateNotPublished.</exception>
        public async Task<UpdateGroupView> PauseAsync(string id, UpdateRolloutRequest request, RequestInfo info, CancellationToken token = default)
        {
            UpdateGroup group = await RequireGroupAsync(id, token).ConfigureAwait(false);
            if (group.Status != UpdateGroupStatusEnum.Published) throw new ApiErrorException(409, "UpdateNotPublished", "Only Published groups can be paused.");
            DateTime now = DateTime.UtcNow;
            group.Status = UpdateGroupStatusEnum.Paused;
            group.PausedUtc = now;
            group.UpdatedUtc = now;
            await _Database.LiveUpdates.SaveGroupAsync(group, false, token).ConfigureAwait(false);
            await AuditAsync(info, "updates.pause", group, request?.Reason, "pause", token).ConfigureAwait(false);
            return await _Admin.ViewAsync(group, true, token).ConfigureAwait(false);
        }

        /// <summary>
        /// POST /api/v1/admin/update-groups/{id}/resume (also clears an automatic halt).
        /// </summary>
        /// <param name="id">Group id.</param>
        /// <param name="request">Body.</param>
        /// <param name="info">Request information.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The group.</returns>
        /// <exception cref="ApiErrorException">404, 409 UpdateNotPublished.</exception>
        public async Task<UpdateGroupView> ResumeAsync(string id, UpdateRolloutRequest request, RequestInfo info, CancellationToken token = default)
        {
            UpdateGroup group = await RequireGroupAsync(id, token).ConfigureAwait(false);
            if (group.Status != UpdateGroupStatusEnum.Paused) throw new ApiErrorException(409, "UpdateNotPublished", "Only Paused groups can be resumed.");
            DateTime now = DateTime.UtcNow;
            group.Status = UpdateGroupStatusEnum.Published;
            group.HaltedUtc = null;
            group.HaltReason = null;
            group.UpdatedUtc = now;
            await _Database.LiveUpdates.SaveGroupAsync(group, false, token).ConfigureAwait(false);
            await AuditAsync(info, "updates.resume", group, request?.Reason, "resume", token).ConfigureAwait(false);
            return await _Admin.ViewAsync(group, true, token).ConfigureAwait(false);
        }

        /// <summary>
        /// POST /api/v1/admin/update-groups/{id}/critical. Applies to installs that have not downloaded it yet.
        /// </summary>
        /// <param name="id">Group id.</param>
        /// <param name="request">Body.</param>
        /// <param name="info">Request information.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The group.</returns>
        /// <exception cref="ApiErrorException">400, 404.</exception>
        public async Task<UpdateGroupView> CriticalAsync(string id, UpdateCriticalRequest request, RequestInfo info, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            string? message = String.IsNullOrWhiteSpace(request.CriticalMessage) ? null : request.CriticalMessage.Trim();
            if (message != null && message.Length > 200) throw ApiErrorException.Field("criticalMessage", "At most 200 characters.");
            UpdateGroup group = await RequireGroupAsync(id, token).ConfigureAwait(false);
            group.Critical = request.Critical || message != null;
            group.CriticalMessage = group.Critical ? message : null;
            group.UpdatedUtc = DateTime.UtcNow;
            await _Database.LiveUpdates.SaveGroupAsync(group, false, token).ConfigureAwait(false);
            await AuditAsync(info, "updates.critical", group, group.Critical ? "critical" + (message == null ? String.Empty : ": " + message) : "not critical", "critical", token).ConfigureAwait(false);
            return await _Admin.ViewAsync(group, true, token).ConfigureAwait(false);
        }

        /// <summary>
        /// PUT /api/v1/admin/update-groups/{id}/rollout-plan: stages and the automatic halt (resolved decision 4).
        /// </summary>
        /// <param name="id">Group id.</param>
        /// <param name="request">Body.</param>
        /// <param name="info">Request information.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The group.</returns>
        /// <exception cref="ApiErrorException">400, 404, 409 (RolledBack or Superseded).</exception>
        public async Task<UpdateGroupView> SetRolloutPlanAsync(string id, UpdateRolloutPlanRequest request, RequestInfo info, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            UpdateGroup group = await RequireGroupAsync(id, token).ConfigureAwait(false);
            if (group.Status == UpdateGroupStatusEnum.RolledBack || group.Status == UpdateGroupStatusEnum.Superseded) throw new ApiErrorException(409, "UpdateNotPublished", "The group is no longer offered.");
            DateTime now = DateTime.UtcNow;
            DateTime start = group.PublishedUtc ?? now;
            List<UpdateRolloutStageRequest> stages = request.UseTemplate ? ParseTemplate(_Settings.Updates.RolloutPlanTemplate) : (request.Stages ?? new List<UpdateRolloutStageRequest>());
            if (stages.Count > 10) throw ApiErrorException.Field("stages", "At most 10 stages.");
            List<UpdateRolloutStage> plan = new List<UpdateRolloutStage>();
            for (int i = 0; i < stages.Count; i++)
            {
                UpdateRolloutStageRequest s = stages[i];
                int bp = Percent(s.Percentage, "stages[" + i + "].percentage", false);
                DateTime when;
                if (s.StartsUtc != null) when = s.StartsUtc.Value.ToUniversalTime();
                else if (s.HoursAfterPublish != null && s.HoursAfterPublish >= 0 && s.HoursAfterPublish <= 24 * 365) when = start.AddHours((double)s.HoursAfterPublish.Value);
                else throw ApiErrorException.Field("stages[" + i + "].startsUtc", "Set startsUtc or hoursAfterPublish (0 to 8760).");
                if (plan.Count > 0 && bp <= plan[plan.Count - 1].BasisPoints) throw ApiErrorException.Field("stages[" + i + "].percentage", "Percentages must increase.");
                if (plan.Count > 0 && when < plan[plan.Count - 1].StartsUtc) throw ApiErrorException.Field("stages[" + i + "].startsUtc", "Stages must be in time order.");
                plan.Add(new UpdateRolloutStage { Position = i, BasisPoints = bp, StartsUtc = when });
            }
            if (request.AutoHaltFailurePercent != null && (request.AutoHaltFailurePercent < 1 || request.AutoHaltFailurePercent > 100)) throw ApiErrorException.Field("autoHaltFailurePercent", "Must be 1 to 100.");
            if (request.AutoHaltMinInstalls != null && (request.AutoHaltMinInstalls < 1 || request.AutoHaltMinInstalls > 100000)) throw ApiErrorException.Field("autoHaltMinInstalls", "Must be 1 to 100000.");
            await _Database.LiveUpdates.ReplaceStagesAsync(group.Id, plan, token).ConfigureAwait(false);
            if (request.AutoHaltEnabled != null) group.AutoHaltEnabled = request.AutoHaltEnabled.Value;
            if (request.AutoHaltFailurePercent != null) group.AutoHaltFailurePercent = request.AutoHaltFailurePercent.Value;
            if (request.AutoHaltMinInstalls != null) group.AutoHaltMinInstalls = request.AutoHaltMinInstalls.Value;
            group.UpdatedUtc = now;
            await _Database.LiveUpdates.SaveGroupAsync(group, false, token).ConfigureAwait(false);
            await AuditAsync(info, "updates.rolloutPlan", group, String.Join(", ", plan.Select(p => LiveUpdateRules.ToPercent(p.BasisPoints).ToString(CultureInfo.InvariantCulture) + "% at " + p.StartsUtc.ToString("O", CultureInfo.InvariantCulture))), "rollout", token).ConfigureAwait(false);
            if (group.Status == UpdateGroupStatusEnum.Published) await ApplyDueStagesAsync(now, token).ConfigureAwait(false);
            return await _Admin.GetGroupAsync(group.Id, token).ConfigureAwait(false);
        }

        /// <summary>
        /// POST /api/v1/admin/update-groups/{id}/rollback: republish an earlier group's content with new ids.
        /// </summary>
        /// <param name="id">Group to roll back.</param>
        /// <param name="request">Body.</param>
        /// <param name="info">Request information.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The new group.</returns>
        /// <exception cref="ApiErrorException">400, 403, 404, 503 SigningUnavailable.</exception>
        public async Task<UpdateGroupView> RollbackAsync(string id, UpdateRollbackRequest request, RequestInfo info, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            RequireNotDeployKey(info.Context);
            UpdateGroup current = await RequireGroupAsync(id, token).ConfigureAwait(false);
            if (String.IsNullOrWhiteSpace(request.TargetUpdateGroupId)) throw ApiErrorException.Field("targetUpdateGroupId", "Required.");
            UpdateGroup target = await _Database.LiveUpdates.ReadGroupAsync(request.TargetUpdateGroupId, token).ConfigureAwait(false) ?? throw ApiErrorException.NotFound("Update group");
            if (target.Id == current.Id) throw ApiErrorException.Field("targetUpdateGroupId", "Choose an earlier group.");
            if (target.Channel != current.Channel) throw ApiErrorException.Field("targetUpdateGroupId", "The target must be on the same channel.");
            if (target.Kind == UpdateGroupKindEnum.RollBackToEmbedded || target.Status == UpdateGroupStatusEnum.Draft) throw ApiErrorException.Field("targetUpdateGroupId", "The target must be a published code update.");
            int bp = Percent(request.RolloutPercentage ?? 100, "rolloutPercentage", false);
            await RequireSigningAsync(token).ConfigureAwait(false);
            DateTime now = DateTime.UtcNow;
            UpdateGroup group = NewGroup(UpdateGroupKindEnum.Republish, current.Channel, Message(request.Message, "Roll back to " + target.Message), bp, info.Context, now);
            group.RepublishOf = target.Id;
            group.GitCommit = target.GitCommit;
            group.Critical = target.Critical;
            group.CriticalMessage = target.CriticalMessage;
            await _Database.LiveUpdates.CreateGroupAsync(group, token).ConfigureAwait(false);
            foreach (UpdateRecord source in await _Database.LiveUpdates.ListGroupUpdatesAsync(target.Id, token).ConfigureAwait(false))
            {
                await SupersedeTargetAsync(source.Platform, source.RuntimeVersion, group.Channel, group.Id, info, token).ConfigureAwait(false);
                await _Database.LiveUpdates.CopyUpdateAsync(source.Id, await CopyOfAsync(source, group, now, token).ConfigureAwait(false), token).ConfigureAwait(false);
            }
            current.Status = UpdateGroupStatusEnum.RolledBack;
            current.RolledBackUtc = now;
            current.UpdatedUtc = now;
            await _Database.LiveUpdates.SaveGroupAsync(current, false, token).ConfigureAwait(false);
            await AuditAsync(info, "updates.rollback", group, "Rolled back " + current.Id + " to " + target.Id, "rollback", token).ConfigureAwait(false);
            return await _Admin.ViewAsync(group, true, token).ConfigureAwait(false);
        }

        /// <summary>
        /// POST /api/v1/admin/update-groups/rollback-to-embedded: answer installs with the rollBackToEmbedded directive.
        /// </summary>
        /// <param name="request">Body.</param>
        /// <param name="info">Request information.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The new group.</returns>
        /// <exception cref="ApiErrorException">400, 403, 503 SigningUnavailable.</exception>
        public async Task<UpdateGroupView> RollbackToEmbeddedAsync(UpdateRollbackRequest request, RequestInfo info, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            RequireNotDeployKey(info.Context);
            Dictionary<string, string> errors = new Dictionary<string, string>();
            string platform = (request.Platform ?? "all").Trim();
            if (platform != "all" && !LiveUpdateRules.IsPlatform(platform)) errors["platform"] = "Must be ios, android, or all.";
            string runtime = (request.RuntimeVersion ?? String.Empty).Trim();
            if (runtime.Length < 1 || runtime.Length > 128) errors["runtimeVersion"] = "Required, 1 to 128 characters.";
            string channel = (request.Channel ?? String.Empty).Trim();
            if (!await _Admin.ChannelExistsAsync(channel, token).ConfigureAwait(false)) errors["channel"] = "Unknown channel.";
            if (errors.Count > 0) throw ApiErrorException.Validation(errors);
            int bp = Percent(request.RolloutPercentage ?? 100, "rolloutPercentage", false);
            await RequireSigningAsync(token).ConfigureAwait(false);
            DateTime now = DateTime.UtcNow;
            UpdateGroup group = NewGroup(UpdateGroupKindEnum.RollBackToEmbedded, channel, Message(request.Message, "Roll back to the built-in bundle"), bp, info.Context, now);
            await _Database.LiveUpdates.CreateGroupAsync(group, token).ConfigureAwait(false);
            foreach (string p in platform == "all" ? LiveUpdateRules.Platforms : new List<string> { platform })
            {
                await SupersedeTargetAsync(p, runtime, channel, group.Id, info, token).ConfigureAwait(false);
                UpdateRecord update = new UpdateRecord
                {
                    Id = IdGenerator.Generate("upd_"),
                    GroupId = group.Id,
                    Uuid = Guid.NewGuid().ToString(),
                    Platform = p,
                    RuntimeVersion = runtime,
                    Channel = channel,
                    Kind = UpdateGroupKindEnum.RollBackToEmbedded,
                    Status = group.Status,
                    RolloutBasisPoints = bp,
                    CreatedAt = await NextCreatedAtAsync(p, runtime, channel, now, token).ConfigureAwait(false),
                    CreatedUtc = now,
                    UpdatedUtc = now
                };
                await _Database.LiveUpdates.AddDirectiveUpdateAsync(update, token).ConfigureAwait(false);
            }
            await AuditAsync(info, "updates.rollbackToEmbedded", group, platform + " " + runtime + " on " + channel, "rollback_to_embedded", token).ConfigureAwait(false);
            return await _Admin.ViewAsync(group, true, token).ConfigureAwait(false);
        }

        /// <summary>
        /// POST /api/v1/admin/update-groups/{id}/promote: republish a tested group on another channel (decision 15).
        /// Development updates are never promoted (their published config has no API URL).
        /// </summary>
        /// <param name="id">Source group.</param>
        /// <param name="request">Body (channel, rolloutPercentage, message, supersede).</param>
        /// <param name="info">Request information.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The new group.</returns>
        /// <exception cref="ApiErrorException">400 PromotionNotAllowed or ValidationFailed, 403, 404, 409 RolloutConflict, 503.</exception>
        public async Task<UpdateGroupView> PromoteAsync(string id, UpdateRolloutRequest request, RequestInfo info, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            RequireNotDeployKey(info.Context);
            UpdateGroup source = await RequireGroupAsync(id, token).ConfigureAwait(false);
            if (source.Channel == LiveUpdateRules.DevelopmentChannel) throw new ApiErrorException(400, "PromotionNotAllowed", "Development updates are never promoted: their published config has no API URL.");
            if (source.Kind == UpdateGroupKindEnum.RollBackToEmbedded) throw new ApiErrorException(400, "PromotionNotAllowed", "A rollback to the built-in bundle cannot be promoted.");
            if (source.Status == UpdateGroupStatusEnum.Draft) throw new ApiErrorException(409, "UpdateNotPublished", "Publish the group before promoting it.");
            string channel = (request.Channel ?? String.Empty).Trim();
            if (channel == source.Channel) throw ApiErrorException.Field("channel", "Choose another channel.");
            if (channel == LiveUpdateRules.DevelopmentChannel) throw ApiErrorException.Field("channel", "Updates are not promoted to development.");
            if (!await _Admin.ChannelExistsAsync(channel, token).ConfigureAwait(false)) throw new ApiErrorException(400, "UnknownChannel", "Unknown channel " + channel + ".", new Dictionary<string, string> { { "channel", "Unknown channel." } });
            int bp = request.RolloutPercentage == null ? DefaultRollout(channel) : Percent(request.RolloutPercentage.Value, "rolloutPercentage", false);
            await RequireSigningAsync(token).ConfigureAwait(false);
            List<UpdateRecord> updates = await _Database.LiveUpdates.ListGroupUpdatesAsync(source.Id, token).ConfigureAwait(false);
            foreach (UpdateRecord update in updates)
            {
                UpdateRecord probe = new UpdateRecord { Platform = update.Platform, RuntimeVersion = update.RuntimeVersion, Channel = channel };
                await CheckRolloutConflictAsync(probe, String.Empty, bp, request.Supersede, info, token).ConfigureAwait(false);
            }
            DateTime now = DateTime.UtcNow;
            UpdateGroup group = NewGroup(UpdateGroupKindEnum.Publish, channel, Message(request.Message, source.Message), bp, info.Context, now);
            group.PromotedFrom = source.Id;
            group.GitCommit = source.GitCommit;
            group.GitDirty = source.GitDirty;
            group.Critical = source.Critical;
            group.CriticalMessage = source.CriticalMessage;
            await _Database.LiveUpdates.CreateGroupAsync(group, token).ConfigureAwait(false);
            await _Metadata.SnapshotAsync(TargetTypeEnum.UpdateGroup, source.Id, TargetTypeEnum.UpdateGroup, group.Id, token).ConfigureAwait(false);
            foreach (UpdateRecord update in updates)
                await _Database.LiveUpdates.CopyUpdateAsync(update.Id, await CopyOfAsync(update, group, now, token).ConfigureAwait(false), token).ConfigureAwait(false);
            await AfterRolloutAsync(group, info, token).ConfigureAwait(false);
            await AuditAsync(info, "updates.promote", group, "Promoted " + source.Id + " from " + source.Channel + " to " + channel, "publish", token).ConfigureAwait(false);
            return await _Admin.ViewAsync(group, true, token).ConfigureAwait(false);
        }

        /// <summary>
        /// DELETE /api/v1/admin/update-groups/{id}: Drafts only.
        /// </summary>
        /// <param name="id">Group id.</param>
        /// <param name="info">Request information.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        /// <exception cref="ApiErrorException">404, 409 UpdateNotDraft.</exception>
        public async Task DeleteAsync(string id, RequestInfo info, CancellationToken token = default)
        {
            UpdateGroup group = await RequireGroupAsync(id, token).ConfigureAwait(false);
            if (group.Status != UpdateGroupStatusEnum.Draft) throw new ApiErrorException(409, "UpdateNotDraft", "Published history is permanent; only Draft groups can be deleted.");
            await _Database.LiveUpdates.DeleteDraftGroupAsync(group.Id, token).ConfigureAwait(false);
            await AuditAsync(info, "updates.delete", group, group.Message, null, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Apply rollout plan stages whose start time passed (rollout worker).
        /// </summary>
        /// <param name="nowUtc">Now.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Stages applied.</returns>
        public async Task<int> ApplyDueStagesAsync(DateTime nowUtc, CancellationToken token = default)
        {
            List<UpdateDueStage> due = await _Database.LiveUpdates.ListDueStagesAsync(nowUtc, 500, token).ConfigureAwait(false);
            int applied = 0;
            foreach (IGrouping<string, UpdateDueStage> byGroup in due.GroupBy(d => d.GroupId, StringComparer.Ordinal))
            {
                UpdateDueStage last = byGroup.OrderBy(d => d.Position).Last();
                UpdateGroup? group = await _Database.LiveUpdates.ReadGroupAsync(byGroup.Key, token).ConfigureAwait(false);
                if (group == null) continue;
                group.RolloutBasisPoints = last.BasisPoints;
                group.UpdatedUtc = nowUtc;
                await _Database.LiveUpdates.SaveGroupAsync(group, true, token).ConfigureAwait(false);
                await _Database.LiveUpdates.MarkStageAppliedAsync(group.Id, last.Position, nowUtc, token).ConfigureAwait(false);
                await AfterRolloutAsync(group, null, token).ConfigureAwait(false);
                await AuditAsync(null, "updates.rollout", group, "Rollout plan stage " + (last.Position + 1) + ": " + LiveUpdateRules.ToPercent(last.BasisPoints) + "%", "rollout", token).ConfigureAwait(false);
                applied++;
            }
            return applied;
        }

        /// <summary>
        /// Pause Published groups whose install failure rate exceeds their threshold (resolved decision 4).
        /// </summary>
        /// <param name="nowUtc">Now.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Groups halted.</returns>
        public async Task<int> AutoHaltAsync(DateTime nowUtc, CancellationToken token = default)
        {
            int halted = 0;
            foreach (UpdateGroup group in await _Database.LiveUpdates.ListAutoHaltCandidatesAsync(200, token).ConfigureAwait(false))
            {
                UpdateHealthCounts counts = await _Database.LiveUpdates.CountHealthAsync(group.Id, nowUtc.AddDays(-30), token).ConfigureAwait(false);
                long total = counts.Failures + counts.Successes;
                if (total < Math.Max(1, group.AutoHaltMinInstalls)) continue;
                if (counts.Failures * 100 <= (long)group.AutoHaltFailurePercent * total) continue;
                decimal rate = Math.Round(counts.Failures * 100m / total, 2);
                group.Status = UpdateGroupStatusEnum.Paused;
                group.PausedUtc = nowUtc;
                group.HaltedUtc = nowUtc;
                group.HaltReason = "Launch failure rate " + rate.ToString(CultureInfo.InvariantCulture) + "% (" + counts.Failures + " of " + total + " installs) exceeded " + group.AutoHaltFailurePercent + "%.";
                group.UpdatedUtc = nowUtc;
                await _Database.LiveUpdates.SaveGroupAsync(group, false, token).ConfigureAwait(false);
                await AuditAsync(null, "updates.autoHalt", group, group.HaltReason, "pause", token).ConfigureAwait(false);
                TrellisKitTelemetry.Add(LiveUpdatesTelemetry.AutoHalts, 1, "channel", group.Channel, "trelliskit.property", _PropertyId);
                _Logger.Warn("[UpdatePublishService] [p:" + _PropertyId + "] halted " + group.Id + ": " + group.HaltReason);
                halted++;
            }
            return halted;
        }

        /// <summary>
        /// Parse a rollout plan template such as 5@0,25@24,100@72 (percent@hoursAfterPublish).
        /// </summary>
        /// <param name="template">Template.</param>
        /// <returns>Stages.</returns>
        /// <exception cref="ApiErrorException">400 ValidationFailed when malformed.</exception>
        public static List<UpdateRolloutStageRequest> ParseTemplate(string? template)
        {
            List<UpdateRolloutStageRequest> stages = new List<UpdateRolloutStageRequest>();
            foreach (string part in (template ?? String.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                string[] pieces = part.Split('@');
                if (pieces.Length != 2 || !Decimal.TryParse(pieces[0], NumberStyles.Number, CultureInfo.InvariantCulture, out decimal percent) ||
                    !Decimal.TryParse(pieces[1], NumberStyles.Number, CultureInfo.InvariantCulture, out decimal hours))
                    throw ApiErrorException.Field("updates.rolloutPlanTemplate", "Must be comma-separated percent@hours stages, for example 5@0,25@24,100@72.");
                stages.Add(new UpdateRolloutStageRequest { Percentage = percent, HoursAfterPublish = hours });
            }
            return stages;
        }

        #endregion

        #region Private-Methods

        private async Task<Dictionary<string, UpdateDescriptorFile>> ValidateAsync(UpdateDescriptor d, RequestContext? context, CancellationToken token)
        {
            Dictionary<string, string> errors = new Dictionary<string, string>();
            d.Platform = (d.Platform ?? String.Empty).Trim();
            d.Channel = (d.Channel ?? String.Empty).Trim();
            d.RuntimeVersion = (d.RuntimeVersion ?? String.Empty).Trim();
            d.Message = (d.Message ?? String.Empty).Trim();
            if (!LiveUpdateRules.IsPlatform(d.Platform)) throw new ApiErrorException(400, "UnsupportedPlatform", "platform must be ios or android.", new Dictionary<string, string> { { "platform", "Must be ios or android." } });
            if (!await _Admin.ChannelExistsAsync(d.Channel, token).ConfigureAwait(false)) throw new ApiErrorException(400, "UnknownChannel", "Unknown channel " + d.Channel + ".", new Dictionary<string, string> { { "channel", "Unknown channel." } });
            RequireChannelAccess(context, d.Channel);
            if (d.RuntimeVersion.Length < 1 || d.RuntimeVersion.Length > 128) errors["runtimeVersion"] = "Required, 1 to 128 characters.";
            if (d.Message.Length < 1 || d.Message.Length > 500) errors["message"] = "Required, 1 to 500 characters.";
            if (d.CriticalMessage != null)
            {
                d.CriticalMessage = d.CriticalMessage.Trim();
                if (d.CriticalMessage.Length == 0) d.CriticalMessage = null;
                else if (d.CriticalMessage.Length > 200) errors["criticalMessage"] = "At most 200 characters.";
                else d.Critical = true;
            }
            if (d.RolloutPercentage != null && LiveUpdateRules.ToBasisPoints(d.RolloutPercentage.Value) < 0) errors["rolloutPercentage"] = "Must be 0 to 100 with at most two decimals.";
            if (!String.IsNullOrEmpty(d.GitCommit) && !LiveUpdateRules.GitCommitPattern.IsMatch(d.GitCommit)) errors["gitCommit"] = "Must be 7 to 40 hex characters.";
            if (!String.IsNullOrEmpty(d.UpdateGroupId))
            {
                if (LiveUpdateRules.UuidPattern.IsMatch(d.UpdateGroupId)) d.UpdateGroupId = d.UpdateGroupId.ToLowerInvariant();
                else errors["updateGroupId"] = "Must be a UUID.";
            }
            else d.UpdateGroupId = null;
            if (d.ExpoConfig != null && d.ExpoConfig.Value.ValueKind != JsonValueKind.Object && d.ExpoConfig.Value.ValueKind != JsonValueKind.Null) errors["expoConfig"] = "Must be an object.";
            else if (d.ExpoConfig != null && Encoding.UTF8.GetByteCount(d.ExpoConfig.Value.GetRawText()) > MaxExpoConfigBytes) errors["expoConfig"] = "At most 256 KB.";
            if (d.LaunchAsset == null) errors["launchAsset"] = "Required.";
            if (d.Assets.Count > _Settings.Updates.MaxAssetsPerUpdate) errors["assets"] = "At most " + _Settings.Updates.MaxAssetsPerUpdate + " assets (updates.maxAssetsPerUpdate).";
            if (d.SourceMaps.Count > 10) errors["sourceMaps"] = "At most 10 source maps.";
            if (errors.Count > 0) throw ApiErrorException.Validation(errors);

            Dictionary<string, UpdateDescriptorFile> parts = new Dictionary<string, UpdateDescriptorFile>(StringComparer.Ordinal);
            HashSet<string> keys = new HashSet<string>(StringComparer.Ordinal);
            List<KeyValuePair<string, UpdateDescriptorFile>> all = new List<KeyValuePair<string, UpdateDescriptorFile>> { new KeyValuePair<string, UpdateDescriptorFile>("launchAsset", d.LaunchAsset!) };
            for (int i = 0; i < d.Assets.Count; i++) all.Add(new KeyValuePair<string, UpdateDescriptorFile>("assets[" + i + "]", d.Assets[i]));
            for (int i = 0; i < d.SourceMaps.Count; i++) all.Add(new KeyValuePair<string, UpdateDescriptorFile>("sourceMaps[" + i + "]", d.SourceMaps[i]));
            foreach (KeyValuePair<string, UpdateDescriptorFile> entry in all)
            {
                UpdateDescriptorFile f = entry.Value;
                if (f == null)
                {
                    errors[entry.Key] = "Required.";
                    continue;
                }
                bool isMap = entry.Key.StartsWith("sourceMaps", StringComparison.Ordinal);
                f.Key = (f.Key ?? String.Empty).Trim().ToLowerInvariant();
                f.ContentType = String.IsNullOrWhiteSpace(f.ContentType) ? "application/octet-stream" : f.ContentType.Trim();
                if (!LiveUpdateRules.Md5HexPattern.IsMatch(f.Key)) errors[entry.Key + ".key"] = "Must be the MD5 hex of the file.";
                if (LiveUpdateRules.HexOfBase64Url(f.Hash) == null) errors[entry.Key + ".hash"] = "Must be the SHA-256 base64url (no padding) of the file.";
                if (!isMap && (String.IsNullOrWhiteSpace(f.FileExtension) || f.FileExtension.Length > 20)) errors[entry.Key + ".fileExtension"] = "Required, for example .png.";
                if (isMap && !String.Equals(f.BundleKey, d.LaunchAsset?.Key, StringComparison.OrdinalIgnoreCase)) errors[entry.Key + ".bundleKey"] = "Must be the launch asset key.";
                if (!isMap && !keys.Add(f.Key)) errors[entry.Key + ".key"] = "Keys must be unique within the update.";
                string part = String.IsNullOrWhiteSpace(f.Part) ? (isMap ? "sourcemap-" + f.Key : f.Key) : f.Part.Trim();
                f.Part = part;
                if (!parts.TryAdd(part, f) && !isMap) errors[entry.Key + ".part"] = "Part names must be unique.";
            }
            if (d.LaunchAsset != null)
            {
                d.LaunchAsset.ContentType = "application/javascript";
                d.LaunchAsset.FileExtension = ".bundle";
            }
            if (errors.Count > 0) throw ApiErrorException.Validation(errors);
            return parts;
        }

        private async Task RequireAssetsAsync(UpdateDescriptor d, HashSet<string> received, CancellationToken token)
        {
            List<UpdateDescriptorFile> missing = new List<UpdateDescriptorFile>();
            List<UpdateDescriptorFile> declared = new List<UpdateDescriptorFile> { d.LaunchAsset! };
            declared.AddRange(d.Assets);
            List<UpdateDescriptorFile> notReceived = declared.Where(f => !received.Contains(f.Part!)).ToList();
            HashSet<string> stored = await _Database.LiveUpdates.StoredAssetsAsync(notReceived.Select(f => LiveUpdateRules.HexOfBase64Url(f.Hash)!), token).ConfigureAwait(false);
            foreach (UpdateDescriptorFile file in notReceived)
            {
                string hex = LiveUpdateRules.HexOfBase64Url(file.Hash)!;
                if (!stored.Contains(hex)) missing.Add(file);
                else
                {
                    UpdateAsset? asset = await _Database.LiveUpdates.ReadAssetAsync(hex, token).ConfigureAwait(false);
                    file.Size = asset?.SizeBytes ?? file.Size;
                }
            }
            foreach (UpdateDescriptorFile map in d.SourceMaps.Where(m => !received.Contains(m.Part!)))
            {
                string hex = LiveUpdateRules.HexOfBase64Url(map.Hash)!;
                if (await _Store.HeadAsync(UpdateObjectStore.SourceMapKey(_PropertyId, hex), token).ConfigureAwait(false) == null) missing.Add(map);
            }
            if (missing.Count == 0) return;
            Dictionary<string, string> fields = new Dictionary<string, string>();
            foreach (UpdateDescriptorFile file in missing.Take(100)) fields[file.Key] = "Neither uploaded nor stored.";
            throw new ApiErrorException(409, "AssetsMissing", missing.Count + " declared file(s) were neither uploaded nor already stored.", fields);
        }

        private async Task<UpdateGroupView> CreateFromDescriptorAsync(ContentActor actor, UpdateDescriptor d, RequestInfo info, CancellationToken token)
        {
            DateTime now = DateTime.UtcNow;
            int bp = d.RolloutPercentage == null ? DefaultRollout(d.Channel) : LiveUpdateRules.ToBasisPoints(d.RolloutPercentage.Value);
            UpdateGroup? group = null;
            if (d.UpdateGroupId != null) group = await _Database.LiveUpdates.ReadGroupByClientIdAsync(d.UpdateGroupId, token).ConfigureAwait(false);
            else group = await _Database.LiveUpdates.FindJoinableGroupAsync(d.Channel, d.GitCommit, d.Message, d.Platform, now.AddMinutes(-JoinWindowMinutes), token).ConfigureAwait(false);
            bool isNew = group == null;
            if (group != null)
            {
                if (group.Channel != d.Channel) throw new ApiErrorException(409, "RolloutConflict", "Update group " + group.Id + " is on channel " + group.Channel + ".");
                if (group.Kind != UpdateGroupKindEnum.Publish || (group.Status != UpdateGroupStatusEnum.Draft && group.Status != UpdateGroupStatusEnum.Published))
                    throw new ApiErrorException(409, "RolloutConflict", "Update group " + group.Id + " no longer accepts uploads.");
                if (group.Critical != d.Critical) throw new ApiErrorException(409, "RolloutConflict", "The critical flag differs from the group's first upload.");
                List<UpdateRecord> existing = await _Database.LiveUpdates.ListGroupUpdatesAsync(group.Id, token).ConfigureAwait(false);
                if (existing.Any(u => u.Platform == d.Platform)) throw new ApiErrorException(409, "Conflict", "Update group " + group.Id + " already has a " + d.Platform + " update.");
                bp = group.Status == UpdateGroupStatusEnum.Draft && group.RolloutBasisPoints == 0 ? 0 : group.RolloutBasisPoints;
            }
            bool publish = bp > 0;
            bool signingMissing = publish && await _Signing.ActiveAsync(token).ConfigureAwait(false) == null;
            UpdateRecord probe = new UpdateRecord { Platform = d.Platform, RuntimeVersion = d.RuntimeVersion, Channel = d.Channel };
            if (publish && !signingMissing) await CheckRolloutConflictAsync(probe, group?.Id ?? String.Empty, bp, d.Supersede, info, token).ConfigureAwait(false);

            if (group == null)
            {
                group = NewGroup(UpdateGroupKindEnum.Publish, d.Channel, d.Message, bp, info.Context, now);
                group.Critical = d.Critical;
                group.CriticalMessage = d.Critical ? d.CriticalMessage : null;
                group.GitCommit = String.IsNullOrEmpty(d.GitCommit) ? null : d.GitCommit.ToLowerInvariant();
                group.GitDirty = d.GitDirty;
                group.ClientGroupId = d.UpdateGroupId;
                if (!publish || signingMissing)
                {
                    group.Status = UpdateGroupStatusEnum.Draft;
                    group.PublishedUtc = null;
                }
                try
                {
                    await _Database.LiveUpdates.CreateGroupAsync(group, token).ConfigureAwait(false);
                }
                catch (DuplicateRecordException)
                {
                    group = await _Database.LiveUpdates.ReadGroupByClientIdAsync(d.UpdateGroupId!, token).ConfigureAwait(false) ?? throw new ApiErrorException(409, "Conflict", "The update group changed concurrently; retry.");
                    isNew = false;
                }
                if (isNew) await _Metadata.ApplyAsync(actor, TargetTypeEnum.UpdateGroup, group.Id, d.Labels, d.Tags, token).ConfigureAwait(false);
            }

            string launchHex = LiveUpdateRules.HexOfBase64Url(d.LaunchAsset!.Hash)!;
            List<UpdateAssetRef> refs = new List<UpdateAssetRef>
            {
                new UpdateAssetRef { Position = 0, Sha256Hex = launchHex, Key = d.LaunchAsset.Key, FileExtension = ".bundle", ContentType = "application/javascript", IsLaunchAsset = true }
            };
            for (int i = 0; i < d.Assets.Count; i++)
            {
                UpdateDescriptorFile a = d.Assets[i];
                refs.Add(new UpdateAssetRef { Position = i + 1, Sha256Hex = LiveUpdateRules.HexOfBase64Url(a.Hash)!, Key = a.Key, FileExtension = a.FileExtension!.StartsWith(".", StringComparison.Ordinal) ? a.FileExtension : "." + a.FileExtension, ContentType = a.ContentType });
            }
            List<UpdateSourceMap> maps = d.SourceMaps.Select((m, i) =>
            {
                string hex = LiveUpdateRules.HexOfBase64Url(m.Hash)!;
                return new UpdateSourceMap { Position = i, Sha256Hex = hex, Key = m.Key, BundleKey = (m.BundleKey ?? String.Empty).ToLowerInvariant(), SizeBytes = m.Size ?? 0, StorageKey = UpdateObjectStore.SourceMapKey(_PropertyId, hex) };
            }).ToList();
            DateTime createdAt = await NextCreatedAtAsync(d.Platform, d.RuntimeVersion, d.Channel, d.CreatedUtc ?? now, token).ConfigureAwait(false);
            UpdateRecord update = new UpdateRecord
            {
                Id = IdGenerator.Generate("upd_"),
                GroupId = group.Id,
                Uuid = Guid.NewGuid().ToString(),
                Platform = d.Platform,
                RuntimeVersion = d.RuntimeVersion,
                Channel = d.Channel,
                Kind = UpdateGroupKindEnum.Publish,
                Status = group.Status,
                RolloutBasisPoints = group.RolloutBasisPoints,
                CreatedAt = createdAt,
                LaunchAssetSha256 = launchHex,
                LaunchAssetSizeBytes = d.LaunchAsset.Size ?? 0,
                AssetCount = d.Assets.Count,
                TotalBytes = (d.LaunchAsset.Size ?? 0) + d.Assets.Sum(a => a.Size ?? 0),
                CreatedUtc = now,
                UpdatedUtc = now
            };
            string expo = d.ExpoConfig == null || d.ExpoConfig.Value.ValueKind != JsonValueKind.Object ? "{}" : d.ExpoConfig.Value.GetRawText();
            await _Database.LiveUpdates.AddUpdateAsync(update, expo, refs, maps, token).ConfigureAwait(false);
            await AuditAsync(info, isNew ? "updates.create" : "updates.upload", group, d.Platform + " " + d.RuntimeVersion + " on " + d.Channel + ": " + d.Message, "create", token).ConfigureAwait(false);
            if (group.Status == UpdateGroupStatusEnum.Published) await AfterRolloutAsync(group, info, token).ConfigureAwait(false);
            UpdateGroupView view = await _Admin.ViewAsync(group, false, token).ConfigureAwait(false);
            if (signingMissing) throw new ApiErrorException(503, "SigningUnavailable", "No active signing key: the upload is kept as Draft " + group.Id + "; publish it once a signing key is active.", new Dictionary<string, string> { { "updateGroupId", group.Id } });
            return view;
        }

        private async Task CheckRolloutConflictAsync(UpdateRecord target, string groupId, int bp, bool supersede, RequestInfo? info, CancellationToken token)
        {
            List<UpdateRecord> others = await _Database.LiveUpdates.ListPublishedOnTargetAsync(target.Platform, target.RuntimeVersion, target.Channel, groupId, token).ConfigureAwait(false);
            List<UpdateRecord> mid = others.Where(o => o.RolloutBasisPoints < 10000).ToList();
            if (mid.Count == 0) return;
            if (!supersede) throw new ApiErrorException(409, "RolloutConflict", "Update group " + mid[0].GroupId + " is mid-rollout (" + LiveUpdateRules.ToPercent(mid[0].RolloutBasisPoints) + "%) for " + target.Platform + " on " + target.Channel + "; send supersede: true to complete it.");
            foreach (string id in mid.Select(m => m.GroupId).Distinct(StringComparer.Ordinal)) await SupersedeGroupAsync(id, info, token).ConfigureAwait(false);
        }

        private async Task SupersedeTargetAsync(string platform, string runtimeVersion, string channel, string groupId, RequestInfo? info, CancellationToken token)
        {
            List<UpdateRecord> others = await _Database.LiveUpdates.ListPublishedOnTargetAsync(platform, runtimeVersion, channel, groupId, token).ConfigureAwait(false);
            foreach (string id in others.Where(o => o.RolloutBasisPoints < 10000).Select(m => m.GroupId).Distinct(StringComparer.Ordinal)) await SupersedeGroupAsync(id, info, token).ConfigureAwait(false);
        }

        private async Task SupersedeGroupAsync(string id, RequestInfo? info, CancellationToken token)
        {
            UpdateGroup? group = await _Database.LiveUpdates.ReadGroupAsync(id, token).ConfigureAwait(false);
            if (group == null || group.Status != UpdateGroupStatusEnum.Published) return;
            DateTime now = DateTime.UtcNow;
            group.Status = UpdateGroupStatusEnum.Superseded;
            group.SupersededUtc = now;
            group.UpdatedUtc = now;
            await _Database.LiveUpdates.SaveGroupAsync(group, false, token).ConfigureAwait(false);
            await AuditAsync(info, "updates.supersede", group, "Superseded", null, token).ConfigureAwait(false);
        }

        private async Task AfterRolloutAsync(UpdateGroup group, RequestInfo? info, CancellationToken token)
        {
            if (group.Status != UpdateGroupStatusEnum.Published) return;
            List<UpdateRecord> mine = await _Database.LiveUpdates.ListGroupUpdatesAsync(group.Id, token).ConfigureAwait(false);
            List<UpdateRecord> full = mine.Where(u => u.RolloutBasisPoints >= 10000).ToList();
            if (full.Count == 0) return;
            HashSet<string> covered = new HashSet<string>(full.Select(u => u.Platform + "|" + u.RuntimeVersion + "|" + u.Channel), StringComparer.Ordinal);
            HashSet<string> candidates = new HashSet<string>(StringComparer.Ordinal);
            foreach (UpdateRecord update in full)
            {
                foreach (UpdateRecord other in await _Database.LiveUpdates.ListPublishedOnTargetAsync(update.Platform, update.RuntimeVersion, update.Channel, group.Id, token).ConfigureAwait(false))
                    if (other.CreatedAt < update.CreatedAt) candidates.Add(other.GroupId);
            }
            foreach (string candidate in candidates)
            {
                List<UpdateRecord> theirs = await _Database.LiveUpdates.ListGroupUpdatesAsync(candidate, token).ConfigureAwait(false);
                if (theirs.All(u => covered.Contains(u.Platform + "|" + u.RuntimeVersion + "|" + u.Channel))) await SupersedeGroupAsync(candidate, info, token).ConfigureAwait(false);
            }
        }

        private async Task<UpdateRecord> CopyOfAsync(UpdateRecord source, UpdateGroup group, DateTime now, CancellationToken token)
        {
            return new UpdateRecord
            {
                Id = IdGenerator.Generate("upd_"),
                GroupId = group.Id,
                Uuid = Guid.NewGuid().ToString(),
                Platform = source.Platform,
                RuntimeVersion = source.RuntimeVersion,
                Channel = group.Channel,
                Kind = group.Kind,
                Status = group.Status,
                RolloutBasisPoints = group.RolloutBasisPoints,
                CreatedAt = await NextCreatedAtAsync(source.Platform, source.RuntimeVersion, group.Channel, now, token).ConfigureAwait(false),
                LaunchAssetSha256 = source.LaunchAssetSha256,
                LaunchAssetSizeBytes = source.LaunchAssetSizeBytes,
                AssetCount = source.AssetCount,
                TotalBytes = source.TotalBytes,
                CreatedUtc = now,
                UpdatedUtc = now
            };
        }

        private async Task<DateTime> NextCreatedAtAsync(string platform, string runtimeVersion, string channel, DateTime candidate, CancellationToken token)
        {
            DateTime value = LiveUpdateRules.Milliseconds(candidate);
            DateTime now = LiveUpdateRules.Milliseconds(DateTime.UtcNow);
            if (value > now.AddMinutes(5)) value = now;
            DateTime? max = await _Database.LiveUpdates.MaxCreatedAtAsync(platform, runtimeVersion, channel, token).ConfigureAwait(false);
            if (max != null)
            {
                DateTime previous = LiveUpdateRules.Milliseconds(max.Value);
                if (value <= previous) value = previous.AddMilliseconds(1);
            }
            return value;
        }

        private UpdateGroup NewGroup(UpdateGroupKindEnum kind, string channel, string message, int bp, RequestContext? context, DateTime now)
        {
            UpdateCreatorView creator = LiveUpdateRules.CreatorOf(context);
            return new UpdateGroup
            {
                Id = IdGenerator.Generate("upg_"),
                Kind = kind,
                Channel = channel,
                Message = message,
                Status = UpdateGroupStatusEnum.Published,
                RolloutBasisPoints = bp,
                CreatedByType = creator.Type,
                CreatedById = creator.Id,
                CreatedByName = creator.Name,
                AutoHaltEnabled = true,
                AutoHaltFailurePercent = _Settings.Updates.LaunchFailureAlarmPercent,
                AutoHaltMinInstalls = _Settings.Updates.AutoHaltMinInstalls,
                PublishedUtc = now,
                CreatedUtc = now,
                UpdatedUtc = now
            };
        }

        private async Task RequireSigningAsync(CancellationToken token)
        {
            if (await _Signing.ActiveAsync(token).ConfigureAwait(false) == null)
                throw new ApiErrorException(503, "SigningUnavailable", (await _Signing.ProblemAsync(token).ConfigureAwait(false)) ?? "No active signing key.");
        }

        private async Task<UpdateGroup> RequireGroupAsync(string id, CancellationToken token)
        {
            return await _Database.LiveUpdates.ReadGroupAsync(id, token).ConfigureAwait(false) ?? throw ApiErrorException.NotFound("Update group");
        }

        private static void RequireChannelAccess(RequestContext? context, string channel)
        {
            if (context == null || context.DeployKeyId == null) return;
            if (!context.DeployKeyChannels.Contains(channel)) throw ApiErrorException.Forbidden("This deploy key may not publish to channel " + channel + ".");
        }

        private static void RequireNotDeployKey(RequestContext? context)
        {
            if (context != null && context.DeployKeyId != null) throw ApiErrorException.Forbidden("Deploy keys may only create and publish updates.");
        }

        private static int DefaultRollout(string channel)
        {
            return channel == LiveUpdateRules.ProductionChannel ? 1000 : 10000;
        }

        private static int Percent(decimal value, string field, bool allowZero)
        {
            int bp = LiveUpdateRules.ToBasisPoints(value);
            if (bp < 0 || (!allowZero && bp == 0)) throw ApiErrorException.Field(field, "Must be 0.01 to 100 with at most two decimals.");
            return bp;
        }

        private static string Message(string? requested, string fallback)
        {
            string value = String.IsNullOrWhiteSpace(requested) ? fallback : requested.Trim();
            return value.Length > 500 ? value.Substring(0, 500) : value;
        }

        private static string FieldOf(UpdateDescriptor d, UpdateDescriptorFile file)
        {
            if (file == d.LaunchAsset) return "launchAsset";
            int asset = d.Assets.IndexOf(file);
            if (asset >= 0) return "assets[" + asset + "]";
            return "sourceMaps[" + d.SourceMaps.IndexOf(file) + "]";
        }

        private static ApiErrorException TooLarge(string message)
        {
            return new ApiErrorException(413, "TooLarge", message);
        }

        private async Task AuditAsync(RequestInfo? info, string action, UpdateGroup group, string? summary, string? metric, CancellationToken token)
        {
            if (info != null) await _Audit.WriteAsync(info, action, TargetTypeEnum.UpdateGroup.ToString(), group.Id, summary, token).ConfigureAwait(false);
            else await _Audit.WriteAsync(null, null, action, TargetTypeEnum.UpdateGroup.ToString(), group.Id, summary, token).ConfigureAwait(false);
            if (metric != null) TrellisKitTelemetry.Add(LiveUpdatesTelemetry.Publishes, 1, "action", metric, "credential", info == null ? "system" : LiveUpdateRules.CredentialLabel(info.Context), "trelliskit.property", _PropertyId);
        }

        #endregion
    }
}
