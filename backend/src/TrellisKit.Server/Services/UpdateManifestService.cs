namespace TrellisKit.Server.Services
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Globalization;
    using System.Linq;
    using System.Security.Cryptography;
    using System.Text;
    using System.Text.Json;
    using System.Text.Json.Nodes;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core;
    using TrellisKit.Core.Database;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Exceptions;
    using TrellisKit.Core.Helpers;
    using TrellisKit.Core.Models;
    using TrellisKit.Core.Observability;
    using TrellisKit.Core.Requests;
    using TrellisKit.Core.Settings;

    /// <summary>
    /// The Expo Updates protocol v1 manifest endpoint (docs/LIVE_UPDATES_CONTRACT.md sections 3, 4, 9 and the client
    /// implementation notes): header parsing, update selection with deterministic rollout buckets keyed by the install
    /// id (X-TK-Install-Id), signed manifests and directives (a signed noUpdateAvailable when the app is current, and
    /// rollBackToEmbedded), the certificate_chain part for rotated keys, install adoption rows, client events, and
    /// asset delivery in Local mode.
    /// Thread safety: safe to call concurrently.
    /// </summary>
    public class UpdateManifestService
    {
        #region Public-Members

        /// <summary>
        /// updateeventcounts type of manifest responses that offered an update (the adoption checks series).
        /// </summary>
        public const string OfferedCount = "Offered";

        /// <summary>
        /// Path of Local mode asset delivery.
        /// </summary>
        public const string AssetPath = Constants.ApiPrefix + "/updates/assets";

        /// <summary>
        /// Candidates read per manifest request. Default 50. Minimum 5, maximum 500.
        /// </summary>
        public int CandidateLimit
        {
            get { return _CandidateLimit; }
            set { _CandidateLimit = Math.Clamp(value, 5, 500); }
        }

        #endregion

        #region Private-Members

        private static readonly JsonSerializerOptions _Compact = new JsonSerializerOptions { WriteIndented = false };
        private readonly DatabaseDriverBase _Database;
        private readonly AppSettings _Settings;
        private readonly UpdateAdminService _Admin;
        private readonly UpdateSigningService _Signing;
        private readonly UpdateObjectStore _Store;
        private readonly RateLimitService _Limits;
        private readonly AppLogger _Logger;
        private readonly string _PropertyId;
        private int _CandidateLimit = 50;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the service.
        /// </summary>
        /// <param name="database">Property-bound database driver.</param>
        /// <param name="settings">Running settings.</param>
        /// <param name="admin">Admin service (channels).</param>
        /// <param name="signing">Signing service.</param>
        /// <param name="store">Update storage.</param>
        /// <param name="limits">Rate limits.</param>
        /// <param name="logger">Logger.</param>
        /// <param name="propertyId">Property id.</param>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        public UpdateManifestService(DatabaseDriverBase database, AppSettings settings, UpdateAdminService admin, UpdateSigningService signing, UpdateObjectStore store, RateLimitService limits, AppLogger logger, string propertyId)
        {
            _Database = database ?? throw new ArgumentNullException(nameof(database));
            _Settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _Admin = admin ?? throw new ArgumentNullException(nameof(admin));
            _Signing = signing ?? throw new ArgumentNullException(nameof(signing));
            _Store = store ?? throw new ArgumentNullException(nameof(store));
            _Limits = limits ?? throw new ArgumentNullException(nameof(limits));
            _Logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _PropertyId = propertyId ?? throw new ArgumentNullException(nameof(propertyId));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Answer one manifest request.
        /// </summary>
        /// <param name="request">Protocol inputs.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Response to write as is.</returns>
        /// <exception cref="ArgumentNullException">The request is null.</exception>
        public async Task<UpdateManifestResult> HandleAsync(UpdateManifestRequest request, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            Stopwatch watch = Stopwatch.StartNew();
            UpdateManifestResult result;
            string platform = (request.Platform ?? String.Empty).Trim().ToLowerInvariant();
            string channel = String.IsNullOrWhiteSpace(request.Channel) ? LiveUpdateRules.ProductionChannel : request.Channel.Trim();
            using (Activity? span = TrellisKitTelemetry.StartSpan("updates.manifest"))
            {
                span?.SetTag("update.platform", platform);
                span?.SetTag("update.channel", channel);
                span?.SetTag("update.runtime_version", request.RuntimeVersion);
                try
                {
                    result = await SelectAsync(request, platform, channel, span, token).ConfigureAwait(false);
                }
                catch (ApiErrorException e)
                {
                    result = PlainError(e.StatusCode, e.Error, e.Message);
                    if (e.StatusCode == 429) result.Headers["Retry-After"] = (e.RetryAfterSeconds ?? 60).ToString(CultureInfo.InvariantCulture);
                }
            }
            TrellisKitTelemetry.Add(LiveUpdatesTelemetry.Checks, 1, "platform", LiveUpdateRules.IsPlatform(platform) ? platform : "unknown", "channel", channel.Length <= 32 ? channel : "other",
                "outcome", result.Outcome, "protocol", request.ProtocolVersion >= 1 ? "1" : "0", "trelliskit.property", _PropertyId);
            TrellisKitTelemetry.Record(LiveUpdatesTelemetry.CheckDuration, watch.Elapsed.TotalSeconds, "platform", LiveUpdateRules.IsPlatform(platform) ? platform : "unknown");
            if (result.Outcome == "error" && result.StatusCode >= 500) TrellisKitTelemetry.Add(LiveUpdatesTelemetry.Failures, 1, "platform", platform, "channel", channel, "stage", "check", "error_code", "Server");
            return result;
        }

        /// <summary>
        /// HMAC-SHA256 hex of an install id with a key derived from auth.tokenEncryptionKey (HKDF, info tk-updates-install).
        /// Raw install ids are never stored.
        /// </summary>
        /// <param name="installId">Install id.</param>
        /// <returns>Hex hash.</returns>
        public string InstallHash(string installId)
        {
            byte[] root;
            try
            {
                root = Convert.FromBase64String(_Settings.Auth.TokenEncryptionKey ?? String.Empty);
            }
            catch (FormatException)
            {
                root = Encoding.UTF8.GetBytes(_Settings.Auth.TokenEncryptionKey ?? String.Empty);
            }
            if (root.Length == 0) root = Encoding.UTF8.GetBytes("trelliskit-updates");
            byte[] key = HKDF.DeriveKey(HashAlgorithmName.SHA256, root, 32, Encoding.UTF8.GetBytes(_PropertyId), Encoding.UTF8.GetBytes("tk-updates-install"));
            return Convert.ToHexString(HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(installId))).ToLowerInvariant();
        }

        /// <summary>
        /// POST /api/v1/updates/events (section 9.2).
        /// </summary>
        /// <param name="installId">X-TK-Install-Id.</param>
        /// <param name="request">Body.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Counts of accepted and dropped events.</returns>
        /// <exception cref="ApiErrorException">400 ValidationFailed, 429 RateLimited.</exception>
        public async Task<Dictionary<string, int>> ReportEventsAsync(string? installId, UpdateEventsRequest request, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            string id = (installId ?? String.Empty).Trim();
            if (id.Length == 0 || id.Length > 64) throw ApiErrorException.Field("X-TK-Install-Id", "Required, 1 to 64 characters.");
            if (request.Events == null || request.Events.Count < 1 || request.Events.Count > 20) throw ApiErrorException.Field("events", "Send 1 to 20 events.");
            string hash = InstallHash(id);
            _Limits.Enforce(_PropertyId + ":updates:events:" + hash, _Settings.Updates.EventsPerInstallPerHour, RateLimitService.Hour);
            DateTime now = DateTime.UtcNow;
            List<UpdateEvent> events = new List<UpdateEvent>();
            Dictionary<string, string> errors = new Dictionary<string, string>();
            int dropped = 0;
            for (int i = 0; i < request.Events.Count; i++)
            {
                UpdateEventRequest item = request.Events[i];
                if (item == null || !Enum.TryParse(item.Type, false, out UpdateEventTypeEnum type))
                {
                    errors["events[" + i + "].type"] = "Unknown event type.";
                    continue;
                }
                UpdateErrorCodeEnum? code = null;
                if (!String.IsNullOrEmpty(item.ErrorCode))
                {
                    if (Enum.TryParse(item.ErrorCode, false, out UpdateErrorCodeEnum parsed)) code = parsed;
                    else code = UpdateErrorCodeEnum.Unknown;
                }
                DateTime occurred = (item.OccurredUtc ?? now).ToUniversalTime();
                if (occurred < now.AddDays(-7) || occurred > now.AddMinutes(5))
                {
                    dropped++;
                    continue;
                }
                string? uuid = String.IsNullOrWhiteSpace(item.UpdateId) ? null : item.UpdateId.Trim().ToLowerInvariant();
                if (uuid != null && uuid != "embedded" && !LiveUpdateRules.UuidPattern.IsMatch(uuid)) errors["events[" + i + "].updateId"] = "Must be an update UUID.";
                events.Add(new UpdateEvent
                {
                    Id = IdGenerator.Generate("uev_"),
                    InstallIdHash = hash,
                    UpdateUuid = uuid == "embedded" ? null : uuid,
                    Type = type,
                    ErrorCode = code,
                    LaunchDurationMs = item.LaunchDurationMs == null ? null : Math.Clamp(item.LaunchDurationMs.Value, 0, 600000),
                    OccurredUtc = occurred,
                    ReceivedUtc = now,
                    Message = IsFailure(type) ? Clip(item.Message, 500) : null,
                    StackTrace = IsFailure(type) ? Clip(item.StackTrace, 16384) : null
                });
            }
            if (errors.Count > 0) throw ApiErrorException.Validation(errors);
            await _Database.LiveUpdates.InsertEventsAsync(events, token).ConfigureAwait(false);
            List<string> launchFailures = events.Where(e => e.Type == UpdateEventTypeEnum.LaunchFailed && e.UpdateUuid != null).Select(e => e.UpdateUuid!).ToList();
            if (launchFailures.Count > 0) await _Database.LiveUpdates.AddInstallFailuresAsync(hash, launchFailures, now, token).ConfigureAwait(false);
            foreach (UpdateEvent e in events) CountEvent(e);
            return new Dictionary<string, int>(StringComparer.Ordinal) { { "accepted", events.Count }, { "dropped", dropped } };
        }

        /// <summary>
        /// Read a stored asset for Local mode delivery.
        /// </summary>
        /// <param name="propertyId">Property id from the path (must be this property).</param>
        /// <param name="sha256Hex">SHA-256 hex.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The asset record, or null when unknown.</returns>
        public async Task<UpdateAsset?> FindAssetAsync(string propertyId, string sha256Hex, CancellationToken token = default)
        {
            if (!String.Equals(propertyId, _PropertyId, StringComparison.Ordinal) || !LiveUpdateRules.Sha256HexPattern.IsMatch(sha256Hex ?? String.Empty)) return null;
            UpdateAsset? asset = await _Database.LiveUpdates.ReadAssetAsync(sha256Hex!, token).ConfigureAwait(false);
            return asset == null || asset.StoredUtc == null ? null : asset;
        }

        /// <summary>
        /// Read asset bytes.
        /// </summary>
        /// <param name="sha256Hex">SHA-256 hex.</param>
        /// <param name="offset">Offset.</param>
        /// <param name="length">Length.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Bytes.</returns>
        public async Task<byte[]> ReadAssetAsync(string sha256Hex, long offset, int length, CancellationToken token = default)
        {
            byte[] bytes = await _Store.ReadRangeAsync(UpdateObjectStore.AssetKey(_PropertyId, sha256Hex), offset, length, token).ConfigureAwait(false);
            TrellisKitTelemetry.Add(LiveUpdatesTelemetry.AssetBytes, bytes.Length, "direction", "served_local", "trelliskit.property", _PropertyId);
            return bytes;
        }

        /// <summary>
        /// Absolute asset URL.
        /// </summary>
        /// <param name="origin">Request origin for Local delivery.</param>
        /// <param name="sha256Hex">SHA-256 hex.</param>
        /// <returns>URL.</returns>
        public string AssetUrl(string origin, string sha256Hex)
        {
            string configured = (_Settings.Updates.PublicBaseUrl ?? String.Empty).Trim().TrimEnd('/');
            if (configured.Length > 0) return configured + "/assets/" + _PropertyId + "/" + sha256Hex;
            return (origin ?? String.Empty).TrimEnd('/') + AssetPath + "/" + _PropertyId + "/" + sha256Hex;
        }

        #endregion

        #region Private-Methods

        private static bool IsFailure(UpdateEventTypeEnum type)
        {
            return type == UpdateEventTypeEnum.LaunchFailed || type == UpdateEventTypeEnum.DownloadFailed || type == UpdateEventTypeEnum.CheckFailed;
        }

        private static string? Clip(string? value, int max)
        {
            if (String.IsNullOrWhiteSpace(value)) return null;
            return value.Length > max ? value.Substring(0, max) : value;
        }

        private async Task<UpdateManifestResult> SelectAsync(UpdateManifestRequest request, string platform, string channel, Activity? span, CancellationToken token)
        {
            if (String.IsNullOrWhiteSpace(request.Platform)) return PlainError(400, "MissingProtocolHeader", "expo-platform is required");
            if (String.IsNullOrWhiteSpace(request.RuntimeVersion)) return PlainError(400, "MissingProtocolHeader", "expo-runtime-version is required");
            if (!LiveUpdateRules.IsPlatform(platform)) return PlainError(400, "UnsupportedPlatform", "expo-platform must be ios or android");
            string runtime = request.RuntimeVersion.Trim();
            if (runtime.Length > 128) return PlainError(400, "MissingProtocolHeader", "expo-runtime-version is longer than 128 characters");
            if (String.IsNullOrWhiteSpace(request.Channel)) _Logger.Debug("[UpdateManifestService] [p:" + _PropertyId + "] manifest request without expo-channel-name; using production");
            if (!await _Admin.ChannelExistsAsync(channel, token).ConfigureAwait(false)) return PlainError(400, "UnknownChannel", "expo-channel-name " + channel + " is not a channel of this property");

            string? installId = InstallIdOf(request);
            string? hash = installId == null ? null : InstallHash(installId);
            if (hash != null) _Limits.Enforce(_PropertyId + ":updates:install:" + hash, _Settings.Updates.ChecksPerInstallPerHour, RateLimitService.Hour);
            if (!String.IsNullOrEmpty(request.SourceIp)) _Limits.Enforce(_PropertyId + ":updates:ip:" + request.SourceIp, _Settings.Updates.ChecksPerIpPerHour, RateLimitService.Hour);

            string? current = Uuid(request.CurrentUpdateId);
            string? embedded = Uuid(request.EmbeddedUpdateId);
            string? expectedKeyId = null;
            if (StructuredFields.ParseDictionary(request.ExpectSignature).TryGetValue("keyid", out string? keyid)) expectedKeyId = keyid;
            List<string> recentFailed = StructuredFields.ParseList(request.RecentFailedUpdateIds).Select(Uuid).Where(u => u != null).Select(u => u!).Take(50).ToList();
            DateTime now = DateTime.UtcNow;
            HashSet<string> failed = new HashSet<string>(recentFailed, StringComparer.Ordinal);
            if (hash != null)
            {
                if (recentFailed.Count > 0)
                {
                    await _Database.LiveUpdates.AddInstallFailuresAsync(hash, recentFailed, now, token).ConfigureAwait(false);
                    foreach (string uuid in recentFailed) TrellisKitTelemetry.Add(LiveUpdatesTelemetry.Failures, 1, "platform", platform, "channel", channel, "stage", "launch", "error_code", "Unknown", "trelliskit.property", _PropertyId);
                }
                failed.UnionWith(await _Database.LiveUpdates.ListInstallFailuresAsync(hash, token).ConfigureAwait(false));
            }

            UpdateRecord? selected = null;
            bool skippedByRollout = false;
            if (_Settings.Updates.Enabled)
            {
                foreach (UpdateRecord candidate in await _Database.LiveUpdates.ListCandidatesAsync(platform, runtime, channel, _CandidateLimit, token).ConfigureAwait(false))
                {
                    if (failed.Contains(candidate.Uuid)) continue;
                    bool runsIt = current != null && candidate.Uuid == current;
                    if (candidate.Status == UpdateGroupStatusEnum.Paused)
                    {
                        if (runsIt)
                        {
                            selected = candidate;
                            break;
                        }
                        continue;
                    }
                    if (!runsIt && !LiveUpdateRules.InRollout(candidate.GroupId, installId, candidate.RolloutBasisPoints))
                    {
                        skippedByRollout = true;
                        continue;
                    }
                    selected = candidate;
                    break;
                }
            }

            UpdateManifestResult result;
            if (selected != null && selected.Kind == UpdateGroupKindEnum.RollBackToEmbedded)
            {
                if (current != null && embedded != null && current == embedded) result = await DirectiveAsync(request, channel, expectedKeyId, "{\"type\":\"noUpdateAvailable\"}", "no_update", token).ConfigureAwait(false);
                else result = await DirectiveAsync(request, channel, expectedKeyId, "{\"type\":\"rollBackToEmbedded\",\"parameters\":{\"commitTime\":\"" + LiveUpdateRules.Iso(selected.CreatedAt) + "\"}}", "rollback_directive", token).ConfigureAwait(false);
            }
            else if (selected == null || (current != null && selected.Uuid == current))
            {
                result = await DirectiveAsync(request, channel, expectedKeyId, "{\"type\":\"noUpdateAvailable\"}", selected == null && skippedByRollout ? "not_in_rollout" : "no_update", token).ConfigureAwait(false);
            }
            else
            {
                result = await ManifestAsync(request, selected, channel, expectedKeyId, token).ConfigureAwait(false);
                span?.SetTag("update.selected_id", selected.Uuid);
                if (result.StatusCode == 200)
                    await _Database.LiveUpdates.IncrementCountAsync(selected.Uuid, now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), OfferedCount, token).ConfigureAwait(false);
            }

            if (hash != null)
            {
                await _Database.LiveUpdates.UpsertInstallAsync(new UpdateInstall
                {
                    Id = IdGenerator.Generate("uin_"),
                    InstallIdHash = hash,
                    Platform = platform,
                    Channel = channel,
                    RuntimeVersion = runtime,
                    CurrentUpdateUuid = current,
                    EmbeddedUpdateUuid = embedded,
                    KeyId = expectedKeyId != null && expectedKeyId.Length <= 64 ? expectedKeyId : null,
                    AppVersion = String.IsNullOrWhiteSpace(request.AppVersion) || request.AppVersion.Length > 32 ? null : request.AppVersion.Trim(),
                    FirstSeenUtc = now,
                    LastCheckUtc = now
                }, token).ConfigureAwait(false);
            }
            return result;
        }

        private async Task<UpdateManifestResult> ManifestAsync(UpdateManifestRequest request, UpdateRecord update, string channel, string? expectedKeyId, CancellationToken token)
        {
            UpdateGroup group = await _Database.LiveUpdates.ReadGroupAsync(update.GroupId, token).ConfigureAwait(false) ?? throw new ApiErrorException(500, "InternalError", "The selected update has no group.");
            List<UpdateAssetRef> refs = await _Database.LiveUpdates.ListAssetRefsAsync(update.Id, _Settings.Updates.MaxAssetsPerUpdate + 1, token).ConfigureAwait(false);
            UpdateAssetRef? launch = refs.FirstOrDefault(r => r.IsLaunchAsset);
            if (launch == null) throw new ApiErrorException(500, "InternalError", "The selected update has no launch asset.");
            JsonNode? expo = null;
            string? expoText = await _Database.LiveUpdates.ReadExpoClientAsync(update.Id, token).ConfigureAwait(false);
            if (!String.IsNullOrWhiteSpace(expoText))
            {
                try
                {
                    expo = JsonNode.Parse(expoText);
                }
                catch (JsonException)
                {
                    expo = null;
                }
            }
            JsonObject manifest = new JsonObject
            {
                ["id"] = update.Uuid,
                ["createdAt"] = LiveUpdateRules.Iso(update.CreatedAt),
                ["runtimeVersion"] = update.RuntimeVersion,
                ["launchAsset"] = AssetNode(launch, request.AssetOrigin),
                ["assets"] = new JsonArray(refs.Where(r => !r.IsLaunchAsset).Select(r => (JsonNode?)AssetNode(r, request.AssetOrigin)).ToArray()),
                ["metadata"] = new JsonObject
                {
                    ["channel"] = channel,
                    ["updateGroup"] = group.Id,
                    ["critical"] = group.Critical ? "true" : "false"
                },
                ["extra"] = new JsonObject
                {
                    ["expoClient"] = expo ?? new JsonObject(),
                    ["critical"] = group.Critical,
                    ["criticalMessage"] = group.Critical ? group.CriticalMessage : null,
                    ["trelliskit"] = new JsonObject
                    {
                        ["updateId"] = update.Id,
                        ["updateGroupId"] = group.Id,
                        ["message"] = group.Message,
                        ["gitCommit"] = group.GitCommit,
                        ["publishedUtc"] = group.PublishedUtc == null ? null : LiveUpdateRules.Iso(group.PublishedUtc.Value)
                    }
                }
            };
            byte[] body = Encoding.UTF8.GetBytes(manifest.ToJsonString(_Compact));
            UpdateSignature signature = await _Signing.SignAsync(body, expectedKeyId, token).ConfigureAwait(false);
            if (request.ProtocolVersion < 1) return JsonResponse(request, body, signature, channel, "update");
            List<UpdateManifestPart> parts = new List<UpdateManifestPart>
            {
                new UpdateManifestPart("manifest", "application/json; charset=utf-8", body, signature.HeaderValue),
                new UpdateManifestPart("extensions", "application/json", Encoding.UTF8.GetBytes("{\"assetRequestHeaders\":{}}"), null)
            };
            if (signature.CertificateChainPem != null) parts.Add(new UpdateManifestPart("certificate_chain", "application/x-pem-file", Encoding.UTF8.GetBytes(signature.CertificateChainPem), null));
            return Multipart(parts, channel, "update");
        }

        private async Task<UpdateManifestResult> DirectiveAsync(UpdateManifestRequest request, string channel, string? expectedKeyId, string directive, string outcome, CancellationToken token)
        {
            if (request.ProtocolVersion < 1)
            {
                UpdateManifestResult none = new UpdateManifestResult { StatusCode = 204, ContentType = "application/json", Outcome = outcome };
                Protocol(none, channel);
                return none;
            }
            byte[] body = Encoding.UTF8.GetBytes(directive);
            UpdateSignature signature = await _Signing.SignAsync(body, expectedKeyId, token).ConfigureAwait(false);
            List<UpdateManifestPart> parts = new List<UpdateManifestPart> { new UpdateManifestPart("directive", "application/json; charset=utf-8", body, signature.HeaderValue) };
            if (signature.CertificateChainPem != null) parts.Add(new UpdateManifestPart("certificate_chain", "application/x-pem-file", Encoding.UTF8.GetBytes(signature.CertificateChainPem), null));
            return Multipart(parts, channel, outcome);
        }

        private JsonObject AssetNode(UpdateAssetRef asset, string origin)
        {
            return new JsonObject
            {
                ["hash"] = asset.HashB64Url,
                ["key"] = asset.Key,
                ["contentType"] = asset.ContentType,
                ["fileExtension"] = asset.FileExtension,
                ["url"] = AssetUrl(origin, asset.Sha256Hex)
            };
        }

        private static UpdateManifestResult Multipart(List<UpdateManifestPart> parts, string channel, string outcome)
        {
            string boundary = "tk-" + Convert.ToHexString(RandomNumberGenerator.GetBytes(12)).ToLowerInvariant();
            List<byte> body = new List<byte>();
            foreach (UpdateManifestPart part in parts)
            {
                StringBuilder head = new StringBuilder();
                head.Append("--").Append(boundary).Append("\r\n");
                head.Append("content-type: ").Append(part.ContentType).Append("\r\n");
                head.Append("content-disposition: form-data; name=\"").Append(part.Name).Append("\"\r\n");
                if (part.Signature != null) head.Append("expo-signature: ").Append(part.Signature).Append("\r\n");
                head.Append("\r\n");
                body.AddRange(Encoding.UTF8.GetBytes(head.ToString()));
                body.AddRange(part.Body);
                body.AddRange(Encoding.ASCII.GetBytes("\r\n"));
            }
            body.AddRange(Encoding.ASCII.GetBytes("--" + boundary + "--\r\n"));
            UpdateManifestResult result = new UpdateManifestResult { StatusCode = 200, ContentType = "multipart/mixed; boundary=" + boundary, Body = body.ToArray(), Outcome = outcome };
            Protocol(result, channel);
            return result;
        }

        private static UpdateManifestResult JsonResponse(UpdateManifestRequest request, byte[] body, UpdateSignature signature, string channel, string outcome)
        {
            string accept = request.Accept ?? String.Empty;
            UpdateManifestResult result = new UpdateManifestResult
            {
                StatusCode = 200,
                ContentType = accept.Contains("application/expo+json", StringComparison.OrdinalIgnoreCase) || !accept.Contains("application/json", StringComparison.OrdinalIgnoreCase) ? "application/expo+json" : "application/json",
                Body = body,
                Outcome = outcome
            };
            result.Headers["expo-signature"] = signature.HeaderValue;
            Protocol(result, channel);
            return result;
        }

        private static void Protocol(UpdateManifestResult result, string channel)
        {
            result.Headers["expo-protocol-version"] = "1";
            result.Headers["expo-sfv-version"] = "0";
            result.Headers["expo-manifest-filters"] = "channel=" + StructuredFields.Quote(channel);
            result.Headers["cache-control"] = "private, max-age=0";
        }

        private static UpdateManifestResult PlainError(int status, string error, string message)
        {
            return new UpdateManifestResult
            {
                StatusCode = status,
                ContentType = "text/plain; charset=utf-8",
                Body = Encoding.UTF8.GetBytes(status.ToString(CultureInfo.InvariantCulture) + " " + error + ": " + message),
                Outcome = "error",
                Headers = new Dictionary<string, string>(StringComparer.Ordinal) { { "cache-control", "private, max-age=0" } }
            };
        }

        private static string? InstallIdOf(UpdateManifestRequest request)
        {
            string id = (request.InstallId ?? String.Empty).Trim();
            if (id.Length == 0 && StructuredFields.ParseDictionary(request.ExtraParams).TryGetValue("installid", out string? extra)) id = (extra ?? String.Empty).Trim();
            return id.Length == 0 || id.Length > 64 ? null : id;
        }

        private static string? Uuid(string? value)
        {
            string text = (value ?? String.Empty).Trim().ToLowerInvariant();
            return LiveUpdateRules.UuidPattern.IsMatch(text) ? text : null;
        }

        private void CountEvent(UpdateEvent e)
        {
            switch (e.Type)
            {
                case UpdateEventTypeEnum.Downloaded:
                    TrellisKitTelemetry.Add(LiveUpdatesTelemetry.Downloads, 1, "trelliskit.property", _PropertyId);
                    break;
                case UpdateEventTypeEnum.Applied:
                    TrellisKitTelemetry.Add(LiveUpdatesTelemetry.Applied, 1, "trelliskit.property", _PropertyId);
                    break;
                case UpdateEventTypeEnum.CheckFailed:
                case UpdateEventTypeEnum.DownloadFailed:
                case UpdateEventTypeEnum.LaunchFailed:
                    string stage = e.Type == UpdateEventTypeEnum.CheckFailed ? "check" : e.Type == UpdateEventTypeEnum.DownloadFailed ? "download" : "launch";
                    TrellisKitTelemetry.Add(LiveUpdatesTelemetry.Failures, 1, "stage", stage, "error_code", (e.ErrorCode ?? UpdateErrorCodeEnum.Unknown).ToString(), "trelliskit.property", _PropertyId);
                    break;
            }
        }

        #endregion
    }
}
