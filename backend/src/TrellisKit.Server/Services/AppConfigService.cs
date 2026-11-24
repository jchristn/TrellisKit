namespace TrellisKit.Server.Services
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Globalization;
    using System.Linq;
    using System.Text.Json;
    using System.Text.RegularExpressions;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core;
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
    using TrellisKit.Core.Security;
    using TrellisKit.Core.Settings;

    /// <summary>
    /// Server-side feature flags, live kill switches, content versions, and the app content manifest
    /// (docs/LIVE_UPDATES_CONTRACT.md sections 11, 12, 13, 15, and 16). Flags are drafts until a content publish; kill
    /// switches change live, are audited, and are enforced on the API. Published content and kill switches are cached
    /// per property and re-read when appcontentstate changes (appConfig.propagationSeconds and
    /// appConfig.killSwitchPropagationSeconds), so every task converges without a message bus.
    /// Remote strings, banners, and remote assets are drafted in <see cref="AppContentService"/> and publish, restore,
    /// and discard together with flags here.
    /// Thread safety: safe to call concurrently.
    /// </summary>
    public class AppConfigService
    {
        #region Private-Members

        private const string _Base = Constants.ApiPrefix + "/admin/app";
        private static readonly Regex _FlagKey = new Regex("^[a-z][a-zA-Z0-9]*(\\.[a-z][a-zA-Z0-9]*){0,3}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
        private static readonly HashSet<string> _Clients = new HashSet<string>(StringComparer.Ordinal) { "mobile", "web", "server" };
        private readonly DatabaseDriverBase _Database;
        private readonly AppSettings _Settings;
        private readonly PropertyConfiguration _Property;
        private readonly EnumerationService _Enumeration;
        private readonly MetadataService _Metadata;
        private readonly AuditService _Audit;
        private readonly TokenService _Tokens;
        private readonly RateLimitService _Limits;
        private readonly string _PropertyId;
        private readonly SemaphoreSlim _Lock = new SemaphoreSlim(1, 1);
        private List<FeatureFlag> _Published = new List<FeatureFlag>();
        private List<FeatureFlag> _Switches = new List<FeatureFlag>();
        private long _CachedVersion = -1;
        private long _CachedRevision = -1;
        private DateTime _CheckedUtc = DateTime.MinValue;
        private bool _Seeded = false;

        #endregion

        #region Public-Members

        /// <summary>
        /// Remote strings, banners, and assets (publishing, restore, discard, and the manifest parts), or null.
        /// </summary>
        public AppContentService? Content { get; set; } = null;

        /// <summary>
        /// Install id hasher (the HMAC the manifest endpoint stores), so runtimeVersion targeting reads the install row, or null.
        /// </summary>
        public Func<string, string>? InstallHash { get; set; } = null;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the service.
        /// </summary>
        /// <param name="database">Property-bound database driver.</param>
        /// <param name="settings">Running settings.</param>
        /// <param name="property">Property (binary version policy).</param>
        /// <param name="enumeration">Enumeration service.</param>
        /// <param name="metadata">Metadata service.</param>
        /// <param name="audit">Audit service.</param>
        /// <param name="tokens">Token service (signed-in manifest callers).</param>
        /// <param name="limits">Rate limits.</param>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        public AppConfigService(DatabaseDriverBase database, AppSettings settings, PropertyConfiguration property, EnumerationService enumeration, MetadataService metadata, AuditService audit, TokenService tokens, RateLimitService limits)
        {
            _Database = database ?? throw new ArgumentNullException(nameof(database));
            _Settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _Property = property ?? throw new ArgumentNullException(nameof(property));
            _Enumeration = enumeration ?? throw new ArgumentNullException(nameof(enumeration));
            _Metadata = metadata ?? throw new ArgumentNullException(nameof(metadata));
            _Audit = audit ?? throw new ArgumentNullException(nameof(audit));
            _Tokens = tokens ?? throw new ArgumentNullException(nameof(tokens));
            _Limits = limits ?? throw new ArgumentNullException(nameof(limits));
            _PropertyId = property.Id;
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// POST /api/v1/admin/app/flags/enumerate.
        /// </summary>
        /// <param name="query">Query.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Page of flags (the draft when one is pending).</returns>
        /// <exception cref="ApiErrorException">400 ValidationFailed.</exception>
        public async Task<EnumerationResult<FeatureFlagView>> EnumerateFlagsAsync(EnumerationQuery? query, CancellationToken token = default)
        {
            EnumerationRequest request = _Enumeration.Prepare(query, LiveUpdateCollections.Flags, true);
            Dictionary<string, string> errors = new Dictionary<string, string>();
            FeatureFlagFilter filter = new FeatureFlagFilter
            {
                Kind = request.Query.FilterEnum<FeatureFlagKindEnum>("kind", errors),
                State = request.Query.FilterEnum<FeatureFlagStateEnum>("state", errors),
                HasDraftChanges = request.Query.FilterBool("hasDraftChanges", errors),
                Client = request.Query.FilterString("client")
            };
            if (filter.Kind == FeatureFlagKindEnum.KillSwitch) errors["filters.kind"] = "Kill switches are enumerated at /api/v1/admin/app/kill-switches/enumerate.";
            if (errors.Count > 0) throw ApiErrorException.Validation(errors);
            EnumerationResult<FeatureFlag> page = await _Database.AppConfig.EnumerateFlagsAsync(request, filter, token).ConfigureAwait(false);
            Dictionary<string, ObjectMetadata> metadata = await _Metadata.LoadAsync(TargetTypeEnum.FeatureFlag, page.Objects.Select(f => f.Id), true, token).ConfigureAwait(false);
            return page.With(page.Objects.Select(f => View(f, metadata.TryGetValue(f.Id, out ObjectMetadata? m) ? m : new ObjectMetadata(), f.IsDraft)).ToList());
        }

        /// <summary>
        /// POST /api/v1/admin/app/flags: create a draft flag. Audited appConfig.flag.create.
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="request">Body.</param>
        /// <param name="present">Fields present in the body, or null for every field.</param>
        /// <param name="info">Request information.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The flag.</returns>
        /// <exception cref="ApiErrorException">400 ValidationFailed or InvalidTargetingRule, 409 FlagKeyInUse.</exception>
        public async Task<FeatureFlagView> CreateFlagAsync(ContentActor actor, FeatureFlagRequest request, HashSet<string>? present, RequestInfo info, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            ArgumentNullException.ThrowIfNull(request);
            if (await _Database.AppConfig.CountFlagsAsync(token).ConfigureAwait(false) >= _Settings.AppConfig.MaxFlags) throw ApiErrorException.Field("key", "At most " + _Settings.AppConfig.MaxFlags + " flags (appConfig.maxFlags).");
            DateTime now = DateTime.UtcNow;
            FeatureFlag flag = new FeatureFlag { Id = IdGenerator.Generate("ffl_"), Kind = request.Kind ?? FeatureFlagKindEnum.Boolean, CreatedUtc = now, State = FeatureFlagStateEnum.Draft };
            Apply(flag, request, present ?? AllFields(), null);
            await ValidateFlagAsync(flag, null, true, token).ConfigureAwait(false);
            await _Metadata.ValidateAsync(actor, request.Labels, request.Tags, true, token, TargetTypeEnum.FeatureFlag).ConfigureAwait(false);
            flag.RevisionId = IdGenerator.Generate("ffv_");
            flag.EditedByUserId = info.Context?.UserId;
            flag.UpdatedUtc = now;
            await SaveDraftAsync(flag, token).ConfigureAwait(false);
            ObjectMetadata metadata = await _Metadata.ApplyAsync(actor, TargetTypeEnum.FeatureFlag, flag.Id, request.Labels, request.Tags, token).ConfigureAwait(false);
            await _Audit.WriteAsync(info, "appConfig.flag.create", TargetTypeEnum.FeatureFlag.ToString(), flag.Id, flag.Key, token).ConfigureAwait(false);
            return View(flag, metadata, true);
        }

        /// <summary>
        /// GET /api/v1/admin/app/flags/{id}.
        /// </summary>
        /// <param name="id">Flag id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Draft view, live definition, and history preview.</returns>
        /// <exception cref="ApiErrorException">404 NotFound.</exception>
        public async Task<FeatureFlagDetailView> GetFlagAsync(string id, CancellationToken token = default)
        {
            FeatureFlag? draft = await _Database.AppConfig.ReadDraftAsync(id, token).ConfigureAwait(false);
            FeatureFlag? current = await _Database.AppConfig.ReadCurrentAsync(id, token).ConfigureAwait(false);
            FeatureFlag head = draft ?? current ?? throw ApiErrorException.NotFound("Feature flag");
            if (head.Kind == FeatureFlagKindEnum.KillSwitch) throw ApiErrorException.NotFound("Feature flag");
            ObjectMetadata metadata = await _Metadata.LoadOneAsync(TargetTypeEnum.FeatureFlag, id, true, token).ConfigureAwait(false);
            EnumerationRequest request = _Enumeration.Prepare(new EnumerationQuery { MaxResults = 10 }, LiveUpdateCollections.FlagHistory, true, id);
            EnumerationResult<FeatureFlag> history = await _Database.AppConfig.EnumerateHistoryAsync(request, id, token).ConfigureAwait(false);
            return new FeatureFlagDetailView
            {
                Flag = View(head, metadata, draft != null),
                Published = current == null ? null : View(current, metadata, draft != null),
                History = new FlagHistoryPreview
                {
                    Objects = history.Objects.Select(h => View(h, metadata, false)).ToList(),
                    TotalRecords = history.TotalRecords ?? history.Objects.Count,
                    Enumerate = _Base + "/flags/" + id + "/history/enumerate"
                }
            };
        }

        /// <summary>
        /// POST /api/v1/admin/app/flags/{id}/history/enumerate.
        /// </summary>
        /// <param name="id">Flag id.</param>
        /// <param name="query">Query.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Published revisions, newest first.</returns>
        /// <exception cref="ApiErrorException">404 NotFound.</exception>
        public async Task<EnumerationResult<FeatureFlagView>> EnumerateHistoryAsync(string id, EnumerationQuery? query, CancellationToken token = default)
        {
            await RequireFlagAsync(id, token).ConfigureAwait(false);
            EnumerationRequest request = _Enumeration.Prepare(query, LiveUpdateCollections.FlagHistory, true, id);
            EnumerationResult<FeatureFlag> page = await _Database.AppConfig.EnumerateHistoryAsync(request, id, token).ConfigureAwait(false);
            ObjectMetadata metadata = await _Metadata.LoadOneAsync(TargetTypeEnum.FeatureFlag, id, true, token).ConfigureAwait(false);
            return page.With(page.Objects.Select(f => View(f, metadata, false)).ToList());
        }

        /// <summary>
        /// PUT /api/v1/admin/app/flags/{id}: replace the draft (absent fields keep their values; rules replace the whole
        /// list). If-Match must equal the flag's ETag.
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="id">Flag id.</param>
        /// <param name="request">Body.</param>
        /// <param name="present">Fields present in the body.</param>
        /// <param name="ifMatch">If-Match header.</param>
        /// <param name="info">Request information.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The draft.</returns>
        /// <exception cref="ApiErrorException">400, 404, 409 FlagKeyInUse, 412 PreconditionFailed.</exception>
        public async Task<FeatureFlagView> UpdateFlagAsync(ContentActor actor, string id, FeatureFlagRequest request, HashSet<string> present, string? ifMatch, RequestInfo info, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            ArgumentNullException.ThrowIfNull(present);
            FeatureFlag head = await RequireFlagAsync(id, token).ConfigureAwait(false);
            if (String.IsNullOrWhiteSpace(ifMatch)) throw new ApiErrorException(412, "PreconditionFailed", "Send If-Match with the flag's ETag.");
            if (Unquote(ifMatch) != head.RevisionId) throw new ApiErrorException(412, "PreconditionFailed", "The flag changed since it was read; reload it.");
            bool published = await _Database.AppConfig.HasPublishedRevisionAsync(id, token).ConfigureAwait(false);
            FeatureFlag draft = Copy(head);
            Apply(draft, request, present, head);
            if (published && present.Contains("key") && !String.Equals(draft.Key, head.Key, StringComparison.Ordinal)) throw ApiErrorException.Field("key", "The key cannot change after the first publish.");
            await ValidateFlagAsync(draft, id, present.Contains("key"), token).ConfigureAwait(false);
            await _Metadata.ValidateAsync(actor, request.Labels, request.Tags, true, token, TargetTypeEnum.FeatureFlag).ConfigureAwait(false);
            draft.RevisionId = IdGenerator.Generate("ffv_");
            draft.EditedByUserId = info.Context?.UserId;
            draft.UpdatedUtc = DateTime.UtcNow;
            await SaveDraftAsync(draft, token).ConfigureAwait(false);
            ObjectMetadata metadata = await _Metadata.ApplyAsync(actor, TargetTypeEnum.FeatureFlag, id, present.Contains("labels") ? request.Labels : null, present.Contains("tags") ? request.Tags : null, token).ConfigureAwait(false);
            await _Audit.WriteAsync(info, "appConfig.flag.update", TargetTypeEnum.FeatureFlag.ToString(), id, draft.Key, token).ConfigureAwait(false);
            return View(draft, metadata, true);
        }

        /// <summary>
        /// POST /api/v1/admin/app/flags/{id}/archive or /restore: a draft change of the state.
        /// </summary>
        /// <param name="id">Flag id.</param>
        /// <param name="archive">Archive (true) or restore (false).</param>
        /// <param name="info">Request information.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The draft.</returns>
        /// <exception cref="ApiErrorException">404, 409 Conflict (never published).</exception>
        public async Task<FeatureFlagView> ArchiveFlagAsync(string id, bool archive, RequestInfo info, CancellationToken token = default)
        {
            FeatureFlag head = await RequireFlagAsync(id, token).ConfigureAwait(false);
            bool published = await _Database.AppConfig.HasPublishedRevisionAsync(id, token).ConfigureAwait(false);
            if (!published) throw new ApiErrorException(409, "Conflict", "Delete a flag that was never published instead of archiving it.");
            FeatureFlag draft = Copy(head);
            draft.State = archive ? FeatureFlagStateEnum.Archived : FeatureFlagStateEnum.Published;
            draft.RevisionId = IdGenerator.Generate("ffv_");
            draft.EditedByUserId = info.Context?.UserId;
            draft.UpdatedUtc = DateTime.UtcNow;
            await SaveDraftAsync(draft, token).ConfigureAwait(false);
            await _Audit.WriteAsync(info, archive ? "appConfig.flag.archive" : "appConfig.flag.restore", TargetTypeEnum.FeatureFlag.ToString(), id, draft.Key, token).ConfigureAwait(false);
            return View(draft, await _Metadata.LoadOneAsync(TargetTypeEnum.FeatureFlag, id, true, token).ConfigureAwait(false), true);
        }

        /// <summary>
        /// DELETE /api/v1/admin/app/flags/{id}: only flags that were never published.
        /// </summary>
        /// <param name="id">Flag id.</param>
        /// <param name="info">Request information.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        /// <exception cref="ApiErrorException">404, 409 Conflict.</exception>
        public async Task DeleteFlagAsync(string id, RequestInfo info, CancellationToken token = default)
        {
            FeatureFlag head = await RequireFlagAsync(id, token).ConfigureAwait(false);
            if (await _Database.AppConfig.HasPublishedRevisionAsync(id, token).ConfigureAwait(false)) throw new ApiErrorException(409, "Conflict", "Published flags are archived, not deleted.");
            await _Database.AppConfig.DeleteDraftAsync(id, token).ConfigureAwait(false);
            await _Audit.WriteAsync(info, "appConfig.flag.delete", TargetTypeEnum.FeatureFlag.ToString(), id, head.Key, token).ConfigureAwait(false);
        }

        /// <summary>
        /// POST /api/v1/admin/app/flags/{id}/evaluate: the Test as preview.
        /// </summary>
        /// <param name="id">Flag id.</param>
        /// <param name="request">Body.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Evaluation.</returns>
        /// <exception cref="ApiErrorException">404 NotFound.</exception>
        public async Task<FlagEvaluationView> EvaluateFlagAsync(string id, FlagEvaluateRequest request, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            FeatureFlag? flag = request.UseDraft ? await _Database.AppConfig.ReadDraftAsync(id, token).ConfigureAwait(false) : null;
            flag ??= await _Database.AppConfig.ReadCurrentAsync(id, token).ConfigureAwait(false);
            if (flag == null && !request.UseDraft) flag = await _Database.AppConfig.ReadDraftAsync(id, token).ConfigureAwait(false);
            if (flag == null || flag.Kind == FeatureFlagKindEnum.KillSwitch) throw ApiErrorException.NotFound("Feature flag");
            FlagEvaluationSubject s = request.As ?? new FlagEvaluationSubject();
            FlagEvaluationContext context = new FlagEvaluationContext
            {
                Platform = s.Platform,
                Client = s.Client,
                AppVersion = s.AppVersion,
                UpdateId = s.UpdateId,
                RuntimeVersion = s.RuntimeVersion,
                Channel = s.Channel,
                SignedIn = s.SignedIn || !String.IsNullOrEmpty(s.UserId),
                Country = s.Country,
                SubjectId = String.IsNullOrEmpty(s.UserId) ? s.InstallId : s.UserId,
                NowUtc = DateTime.UtcNow
            };
            if (!String.IsNullOrEmpty(s.UserId)) await AddMemberAsync(context, s.UserId, null, token).ConfigureAwait(false);
            if (String.IsNullOrEmpty(context.RuntimeVersion) && !String.IsNullOrEmpty(s.InstallId)) await AddInstallAsync(context, s.InstallId, token).ConfigureAwait(false);
            FlagEvaluationResult result = FeatureFlagEvaluator.Evaluate(flag, context);
            CountEvaluation("preview", result.Reason);
            return new FlagEvaluationView { Value = Typed(result.Value, flag.ValueType), MatchedRuleId = result.MatchedRuleId, Reason = result.Reason, Bucket = result.Bucket };
        }

        /// <summary>
        /// GET /api/v1/admin/app/drafts: pending drafts (at most 100) and the current version.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Drafts.</returns>
        public async Task<ContentDraftsView> GetDraftsAsync(CancellationToken token = default)
        {
            AppContentState state = await _Database.AppConfig.ReadStateAsync(token).ConfigureAwait(false);
            EnumerationRequest request = _Enumeration.Prepare(new EnumerationQuery { MaxResults = 100 }, LiveUpdateCollections.Changes, true, "drafts");
            EnumerationResult<FeatureFlag> page = await _Database.AppConfig.EnumerateDraftsAsync(request, token).ConfigureAwait(false);
            Dictionary<string, ObjectMetadata> metadata = await _Metadata.LoadAsync(TargetTypeEnum.FeatureFlag, page.Objects.Select(f => f.Id), true, token).ConfigureAwait(false);
            ContentDraftCounts content = Content == null ? new ContentDraftCounts() : await Content.CountDraftsAsync(token).ConfigureAwait(false);
            return new ContentDraftsView
            {
                Counts = new Dictionary<string, long>(StringComparer.Ordinal) { { "flags", page.TotalRecords ?? page.Objects.Count }, { "strings", content.Strings }, { "banners", content.Banners }, { "assets", content.Assets } },
                ContentVersion = state.ContentVersion,
                Objects = page.Objects.Select(f => View(f, metadata.TryGetValue(f.Id, out ObjectMetadata? m) ? m : new ObjectMetadata(), true)).ToList(),
                TotalRecords = page.TotalRecords ?? page.Objects.Count,
                Enumerate = _Base + "/drafts/enumerate"
            };
        }

        /// <summary>
        /// POST /api/v1/admin/app/drafts/enumerate.
        /// </summary>
        /// <param name="query">Query.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Page of drafts.</returns>
        public async Task<EnumerationResult<FeatureFlagView>> EnumerateDraftsAsync(EnumerationQuery? query, CancellationToken token = default)
        {
            EnumerationRequest request = _Enumeration.Prepare(query, LiveUpdateCollections.Changes, true, "drafts");
            EnumerationResult<FeatureFlag> page = await _Database.AppConfig.EnumerateDraftsAsync(request, token).ConfigureAwait(false);
            Dictionary<string, ObjectMetadata> metadata = await _Metadata.LoadAsync(TargetTypeEnum.FeatureFlag, page.Objects.Select(f => f.Id), true, token).ConfigureAwait(false);
            AppContentState state = await _Database.AppConfig.ReadStateAsync(token).ConfigureAwait(false);
            ContentDraftCounts content = Content == null ? new ContentDraftCounts() : await Content.CountDraftsAsync(token).ConfigureAwait(false);
            long flags = await _Database.AppConfig.CountDraftsAsync(token).ConfigureAwait(false);
            return CatalogEnumerationResponse<FeatureFlagView>.From(page.With(page.Objects.Select(f => View(f, metadata.TryGetValue(f.Id, out ObjectMetadata? m) ? m : new ObjectMetadata(), true)).ToList()),
                new Dictionary<string, object?> { ["contentVersion"] = state.ContentVersion, ["counts"] = new Dictionary<string, long> { ["flags"] = flags, ["strings"] = content.Strings, ["banners"] = content.Banners, ["assets"] = content.Assets } });
        }

        /// <summary>
        /// POST /api/v1/admin/app/drafts/discard. Audited appConfig.discard.
        /// </summary>
        /// <param name="request">Body.</param>
        /// <param name="info">Request information.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Drafts discarded.</returns>
        public async Task<int> DiscardDraftsAsync(DraftDiscardRequest request, RequestInfo info, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            List<string> ids = request.ObjectIds ?? (await _Database.AppConfig.ListDraftsAsync(_Settings.AppConfig.MaxFlags + 100, token).ConfigureAwait(false)).Select(f => f.Id).ToList();
            if (ids.Count > 1000) throw ApiErrorException.Field("objectIds", "At most 1000 ids.");
            int discarded = 0;
            foreach (string id in ids.Where(i => i.StartsWith("ffl_", StringComparison.Ordinal)).Distinct(StringComparer.Ordinal))
                if (await _Database.AppConfig.DeleteDraftAsync(id, token).ConfigureAwait(false)) discarded++;
            if (Content != null) discarded += await Content.DiscardAsync(request.ObjectIds, token).ConfigureAwait(false);
            await _Audit.WriteAsync(info, "appConfig.discard", TargetTypeEnum.ContentVersion.ToString(), null, discarded + " drafts", token).ConfigureAwait(false);
            TrellisKitTelemetry.Add(LiveUpdatesTelemetry.ContentPublishes, 1, "action", "discard", "trelliskit.property", _PropertyId);
            return discarded;
        }

        /// <summary>
        /// POST /api/v1/admin/app/publish: publish every pending draft as the next content version. Audited
        /// appConfig.publish.
        /// </summary>
        /// <param name="request">Body (message, expectedVersion).</param>
        /// <param name="info">Request information.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The version.</returns>
        /// <exception cref="ApiErrorException">400 ValidationFailed, 409 ContentVersionConflict.</exception>
        public async Task<ContentVersionView> PublishAsync(ContentPublishRequest request, RequestInfo info, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            string message = (request.Message ?? String.Empty).Trim();
            if (message.Length < 1 || message.Length > 500) throw ApiErrorException.Field("message", "Required, 1 to 500 characters.");
            if (request.ExpectedVersion == null) throw ApiErrorException.Field("expectedVersion", "Required: the content version the drafts are based on.");
            List<FeatureFlag> drafts = await _Database.AppConfig.ListDraftsAsync(_Settings.AppConfig.MaxFlags + 100, token).ConfigureAwait(false);
            ContentDraftCounts content = Content == null ? new ContentDraftCounts() : await Content.CountDraftsAsync(token).ConfigureAwait(false);
            if (drafts.Count + content.Strings + content.Banners + content.Assets == 0) throw ApiErrorException.Field("drafts", "Nothing to publish.");
            if (Content != null) await Content.ValidatePublishAsync(token).ConfigureAwait(false);
            ContentVersion version = await PublishDraftsAsync(message, request.ExpectedVersion.Value, drafts.Count, content, ContentVersionKindEnum.Publish, null, info, token).ConfigureAwait(false);
            await _Audit.WriteAsync(info, "appConfig.publish", TargetTypeEnum.ContentVersion.ToString(), version.Id, "Version " + version.Version + ": " + message, token).ConfigureAwait(false);
            TrellisKitTelemetry.Add(LiveUpdatesTelemetry.ContentPublishes, 1, "action", "publish", "trelliskit.property", _PropertyId);
            return await VersionViewAsync(version, false, token).ConfigureAwait(false);
        }

        /// <summary>
        /// POST /api/v1/admin/app/versions/enumerate.
        /// </summary>
        /// <param name="query">Query.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Page of versions.</returns>
        public async Task<EnumerationResult<ContentVersionView>> EnumerateVersionsAsync(EnumerationQuery? query, CancellationToken token = default)
        {
            EnumerationRequest request = _Enumeration.Prepare(query, LiveUpdateCollections.Versions, true);
            EnumerationResult<ContentVersion> page = await _Database.AppConfig.EnumerateVersionsAsync(request, token).ConfigureAwait(false);
            List<ContentVersionView> views = new List<ContentVersionView>();
            foreach (ContentVersion v in page.Objects) views.Add(await VersionViewAsync(v, false, token).ConfigureAwait(false));
            return page.With(views);
        }

        /// <summary>
        /// GET /api/v1/admin/app/versions/{version}.
        /// </summary>
        /// <param name="version">Version number text.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Version with a preview of its changes.</returns>
        /// <exception cref="ApiErrorException">404 NotFound.</exception>
        public async Task<ContentVersionView> GetVersionAsync(string version, CancellationToken token = default)
        {
            ContentVersion v = await RequireVersionAsync(version, token).ConfigureAwait(false);
            return await VersionViewAsync(v, true, token).ConfigureAwait(false);
        }

        /// <summary>
        /// POST /api/v1/admin/app/versions/{version}/changes/enumerate.
        /// </summary>
        /// <param name="version">Version number text.</param>
        /// <param name="query">Query.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Flags as published in that version.</returns>
        /// <exception cref="ApiErrorException">404 NotFound.</exception>
        public async Task<EnumerationResult<FeatureFlagView>> EnumerateChangesAsync(string version, EnumerationQuery? query, CancellationToken token = default)
        {
            ContentVersion v = await RequireVersionAsync(version, token).ConfigureAwait(false);
            EnumerationRequest request = _Enumeration.Prepare(query, LiveUpdateCollections.Changes, true, "v" + v.Version);
            EnumerationResult<FeatureFlag> page = await _Database.AppConfig.EnumerateChangesAsync(request, v.Version, token).ConfigureAwait(false);
            Dictionary<string, ObjectMetadata> metadata = await _Metadata.LoadAsync(TargetTypeEnum.FeatureFlag, page.Objects.Select(f => f.Id), true, token).ConfigureAwait(false);
            return page.With(page.Objects.Select(f => View(f, metadata.TryGetValue(f.Id, out ObjectMetadata? m) ? m : new ObjectMetadata(), false)).ToList());
        }

        /// <summary>
        /// POST /api/v1/admin/app/versions/{version}/restore: publish a new version whose flags equal their state at
        /// that version (kill switches untouched). Audited appConfig.restore.
        /// </summary>
        /// <param name="version">Version number text.</param>
        /// <param name="request">Body (message).</param>
        /// <param name="info">Request information.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The new version.</returns>
        /// <exception cref="ApiErrorException">400, 404, 409 ContentVersionConflict.</exception>
        public async Task<ContentVersionView> RestoreVersionAsync(string version, ContentPublishRequest request, RequestInfo info, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            ContentVersion target = await RequireVersionAsync(version, token).ConfigureAwait(false);
            string message = String.IsNullOrWhiteSpace(request.Message) ? "Restore version " + target.Version : request.Message.Trim();
            if (message.Length > 500) throw ApiErrorException.Field("message", "At most 500 characters.");
            AppContentState state = await _Database.AppConfig.ReadStateAsync(token).ConfigureAwait(false);
            int limit = _Settings.AppConfig.MaxFlags + 100;
            List<FeatureFlag> then = await _Database.AppConfig.ListAtVersionAsync(target.Version, limit, token).ConfigureAwait(false);
            List<FeatureFlag> now = await _Database.AppConfig.ListCurrentAsync(false, limit, token).ConfigureAwait(false);
            DateTime utc = DateTime.UtcNow;
            int changes = 0;
            foreach (FeatureFlag current in now)
            {
                FeatureFlag? old = then.FirstOrDefault(t => t.Id == current.Id);
                if (old != null && old.RevisionId == current.RevisionId) continue;
                FeatureFlag draft = Copy(old ?? current);
                if (old == null) draft.State = FeatureFlagStateEnum.Archived;
                else if (draft.State == FeatureFlagStateEnum.Draft) draft.State = FeatureFlagStateEnum.Published;
                draft.Key = current.Key;
                draft.RevisionId = IdGenerator.Generate("ffv_");
                draft.EditedByUserId = info.Context?.UserId;
                draft.UpdatedUtc = utc;
                await _Database.AppConfig.SaveDraftAsync(draft, token).ConfigureAwait(false);
                changes++;
            }
            ContentDraftCounts content = Content == null ? new ContentDraftCounts() : await Content.RestoreDraftsAsync(target.Version, info.Context?.UserId, token).ConfigureAwait(false);
            if (changes + content.Strings + content.Banners + content.Assets == 0) throw ApiErrorException.Field("version", "The current content already equals version " + target.Version + ".");
            ContentVersion restored = await PublishDraftsAsync(message, state.ContentVersion, changes, content, ContentVersionKindEnum.Restore, target.Version, info, token).ConfigureAwait(false);
            await _Audit.WriteAsync(info, "appConfig.restore", TargetTypeEnum.ContentVersion.ToString(), restored.Id, "Restored version " + target.Version + " as " + restored.Version, token).ConfigureAwait(false);
            TrellisKitTelemetry.Add(LiveUpdatesTelemetry.ContentPublishes, 1, "action", "restore", "trelliskit.property", _PropertyId);
            return await VersionViewAsync(restored, false, token).ConfigureAwait(false);
        }

        /// <summary>
        /// POST /api/v1/admin/app/kill-switches/enumerate (filter killed). Every engine feature key has a switch.
        /// </summary>
        /// <param name="query">Query.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Page of kill switches.</returns>
        /// <exception cref="ApiErrorException">400 ValidationFailed.</exception>
        public async Task<EnumerationResult<KillSwitchView>> EnumerateKillSwitchesAsync(EnumerationQuery? query, CancellationToken token = default)
        {
            await EnsureKillSwitchesAsync(token).ConfigureAwait(false);
            EnumerationRequest request = _Enumeration.Prepare(query, LiveUpdateCollections.KillSwitches, true);
            Dictionary<string, string> errors = new Dictionary<string, string>();
            bool? killed = request.Query.FilterBool("killed", errors);
            if (errors.Count > 0) throw ApiErrorException.Validation(errors);
            EnumerationResult<FeatureFlag> page = await _Database.AppConfig.EnumerateKillSwitchesAsync(request, killed, token).ConfigureAwait(false);
            List<KillSwitchState> states = await _Database.AppConfig.ListKillSwitchStatesAsync(token).ConfigureAwait(false);
            Dictionary<string, ObjectMetadata> metadata = await _Metadata.LoadAsync(TargetTypeEnum.FeatureFlag, page.Objects.Select(f => f.Id), true, token).ConfigureAwait(false);
            return page.With(page.Objects.Select(f => KillSwitchViewOf(f, states.FirstOrDefault(s => s.FeatureKey == f.Key), metadata.TryGetValue(f.Id, out ObjectMetadata? m) ? m : new ObjectMetadata())).ToList());
        }

        /// <summary>
        /// PUT /api/v1/admin/app/kill-switches/{featureKey}: change a kill switch live. Audited appConfig.killSwitch with
        /// the reason.
        /// </summary>
        /// <param name="featureKey">Engine feature key.</param>
        /// <param name="request">Body.</param>
        /// <param name="info">Request information.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The switch.</returns>
        /// <exception cref="ApiErrorException">400 ValidationFailed or InvalidTargetingRule, 404 NotFound.</exception>
        public async Task<KillSwitchView> PutKillSwitchAsync(string featureKey, KillSwitchRequest request, RequestInfo info, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            if (!KillSwitchCatalog.IsFeature(featureKey)) throw ApiErrorException.NotFound("Kill switch");
            await EnsureKillSwitchesAsync(token).ConfigureAwait(false);
            List<FeatureFlag> switches = await _Database.AppConfig.ListCurrentAsync(true, 500, token).ConfigureAwait(false);
            FeatureFlag live = switches.FirstOrDefault(s => s.Key == featureKey) ?? throw ApiErrorException.NotFound("Kill switch");
            Dictionary<string, string> errors = new Dictionary<string, string>();
            string reason = (request.Reason ?? String.Empty).Trim();
            if (reason.Length < 1 || reason.Length > 500) errors["reason"] = "Required, 1 to 500 characters.";
            string? message = String.IsNullOrWhiteSpace(request.Message) ? null : request.Message.Trim();
            if (message != null && message.Length > 200) errors["message"] = "At most 200 characters.";
            if (errors.Count > 0) throw ApiErrorException.Validation(errors);
            List<FeatureFlagRule> rules = new List<FeatureFlagRule>();
            if (request.Killed)
            {
                if (request.Rules != null && request.Rules.Count > 0) rules = BuildRules(request.Rules, live, FeatureFlagValueTypeEnum.Boolean, true, errors);
                else rules.Add(new FeatureFlagRule { Id = live.Rules.FirstOrDefault(r => r.Conditions.Count == 0)?.Id ?? IdGenerator.Generate("ffr_"), Position = 1, Description = "Everyone", Value = "true", RolloutBasisPoints = 10000 });
                foreach (FeatureFlagRule rule in rules) rule.Value = "true";
            }
            if (errors.Count > 0) throw new ApiErrorException(400, "InvalidTargetingRule", "A targeting rule is invalid.", errors);
            Dictionary<string, string> ruleErrors = FeatureFlagEvaluator.Validate(rules, _Settings.AppConfig.MaxRulesPerFlag, _Settings.AppConfig.MaxConditionsPerRule, _Settings.AppConfig.MaxValuesPerCondition);
            if (ruleErrors.Count == 0) ruleErrors = await ProfileFieldErrorsAsync(_Database, rules, token).ConfigureAwait(false);
            if (ruleErrors.Count > 0) throw new ApiErrorException(400, "InvalidTargetingRule", "A targeting rule is invalid.", ruleErrors);
            bool wasKilled = live.Rules.Count > 0;
            live.Rules = rules;
            live.Message = message;
            if (request.BlocksServer != null) live.BlocksServer = request.BlocksServer.Value;
            live.EditedByUserId = info.Context?.UserId;
            live.UpdatedUtc = DateTime.UtcNow;
            KillSwitchState state = new KillSwitchState
            {
                FeatureKey = featureKey,
                FlagObjectId = live.Id,
                LastChangedByUserId = info.Context?.UserId,
                LastChangedByName = info.Context?.PrincipalName ?? "admin",
                LastChangedReason = reason,
                LastChangedUtc = live.UpdatedUtc
            };
            await _Database.AppConfig.SaveKillSwitchAsync(live, state, token).ConfigureAwait(false);
            _CheckedUtc = DateTime.MinValue;
            string action = !request.Killed ? "unkill" : rules.Any(r => r.Conditions.Count > 0) ? "target" : "kill";
            await _Audit.WriteAsync(info, "appConfig.killSwitch", TargetTypeEnum.FeatureFlag.ToString(), live.Id, featureKey + " " + action + (wasKilled == request.Killed ? " (rules changed)" : String.Empty) + ": " + reason, token).ConfigureAwait(false);
            TrellisKitTelemetry.Add(LiveUpdatesTelemetry.KillSwitchChanges, 1, "feature", featureKey, "action", action, "trelliskit.property", _PropertyId);
            List<KillSwitchState> states = await _Database.AppConfig.ListKillSwitchStatesAsync(token).ConfigureAwait(false);
            return KillSwitchViewOf(live, states.FirstOrDefault(s => s.FeatureKey == featureKey), await _Metadata.LoadOneAsync(TargetTypeEnum.FeatureFlag, live.Id, true, token).ConfigureAwait(false));
        }

        /// <summary>
        /// GET /api/v1/app/manifest: the content manifest resolved for one caller.
        /// </summary>
        /// <param name="inputs">Caller inputs.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Manifest.</returns>
        /// <exception cref="ApiErrorException">429 RateLimited.</exception>
        public async Task<AppManifestView> ManifestAsync(AppManifestInputs inputs, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(inputs);
            Stopwatch watch = Stopwatch.StartNew();
            string client = inputs.Client == "mobile" ? "mobile" : "web";
            if (!String.IsNullOrEmpty(inputs.ClientId)) _Limits.Enforce(_PropertyId + ":app:client:" + inputs.ClientId, _Settings.AppConfig.ManifestPerClientPerHour, RateLimitService.Hour);
            if (!String.IsNullOrEmpty(inputs.SourceIp)) _Limits.Enforce(_PropertyId + ":app:ip:" + inputs.SourceIp, _Settings.AppConfig.ManifestPerIpPerHour, RateLimitService.Hour);
            await RefreshAsync(token).ConfigureAwait(false);
            FlagEvaluationContext context = await CallerContextAsync(inputs, token).ConfigureAwait(false);
            AppManifestView view = new AppManifestView
            {
                Since = inputs.Since,
                CheckIntervalMinutes = _Settings.AppConfig.CheckIntervalMinutes,
                UpdatesCheckIntervalMinutes = _Property.Updates?.CheckIntervalMinutes ?? _Settings.Updates.CheckIntervalMinutes,
                Property = inputs.Since == null ? inputs.Property : null
            };
            AppContentState state = await _Database.AppConfig.ReadStateAsync(token).ConfigureAwait(false);
            view.ContentVersion = state.ContentVersion;
            if (state.ContentVersion > 0) view.PublishedUtc = (await _Database.AppConfig.ReadVersionAsync(state.ContentVersion, token).ConfigureAwait(false))?.PublishedUtc;
            view.EvaluatedFor["platform"] = context.Platform;
            view.EvaluatedFor["appVersion"] = context.AppVersion;
            view.EvaluatedFor["updateId"] = context.UpdateId;
            view.EvaluatedFor["signedIn"] = context.SignedIn;
            view.EvaluatedFor["membership"] = context.SignedIn ? context.Membership : null;
            foreach (FeatureFlag flag in _Published.Where(f => f.State != FeatureFlagStateEnum.Archived && f.Clients.Contains(client)).Take(_Settings.AppConfig.MaxFlags))
            {
                FlagEvaluationResult result = FeatureFlagEvaluator.Evaluate(flag, context);
                view.Flags[flag.Key] = Typed(result.Value, flag.ValueType);
                CountEvaluation("manifest", result.Reason);
            }
            foreach (FeatureFlag sw in _Switches)
            {
                FlagEvaluationResult result = FeatureFlagEvaluator.Evaluate(sw, context);
                bool killed = result.Value == "true";
                view.KillSwitches[sw.Key] = new AppKillSwitchState { Killed = killed, Message = killed ? sw.Message : null };
            }
            if (Content != null) await Content.FillManifestAsync(view, context, client, inputs.Since, inputs.Origin, token).ConfigureAwait(false);
            else
            {
                view.Strings = new Dictionary<string, object>(StringComparer.Ordinal) { { "upserts", new Dictionary<string, string>() }, { "deletes", new List<string>() } };
                view.Banners = new Dictionary<string, object>(StringComparer.Ordinal) { { "objects", new List<object>() }, { "deletes", new List<string>() } };
                view.Assets = new Dictionary<string, object>(StringComparer.Ordinal) { { "upserts", new List<object>() }, { "deletes", new List<string>() } };
            }
            if (client == "mobile") view.Binary = BinaryPolicy(context.Platform, context.AppVersion);
            TrellisKitTelemetry.Add(LiveUpdatesTelemetry.AppManifestRequests, 1, "client", client, "outcome", view.FullRefresh ? "full_refresh" : inputs.Since == null ? "full" : "delta", "trelliskit.property", _PropertyId);
            TrellisKitTelemetry.Record(LiveUpdatesTelemetry.AppManifestDuration, watch.Elapsed.TotalSeconds, "client", client);
            return view;
        }

        /// <summary>
        /// The targeting attributes of an app manifest caller (headers, the member behind the access token, and the install
        /// row of X-TK-Install-Id), shared by the manifest and the content re-enumeration routes.
        /// </summary>
        /// <param name="inputs">Caller inputs.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Evaluation context.</returns>
        /// <exception cref="ArgumentNullException">The inputs are null.</exception>
        public async Task<FlagEvaluationContext> CallerContextAsync(AppManifestInputs inputs, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(inputs);
            string client = inputs.Client == "mobile" ? "mobile" : "web";
            FlagEvaluationContext context = new FlagEvaluationContext
            {
                Platform = inputs.Platform ?? (client == "web" ? "web" : null),
                Client = client,
                AppVersion = inputs.AppVersion,
                UpdateId = inputs.UpdateId,
                Channel = inputs.Channel,
                Country = inputs.Country,
                SubjectId = inputs.ClientId,
                NowUtc = DateTime.UtcNow
            };
            if (!String.IsNullOrEmpty(inputs.AccessToken))
            {
                SessionPrincipal? principal = await _Tokens.ValidateAccessTokenAsync(inputs.AccessToken, token).ConfigureAwait(false);
                if (principal != null && principal.PrincipalKind == PrincipalKindEnum.Member) await AddMemberAsync(context, principal.UserId, principal, token).ConfigureAwait(false);
            }
            if (client == "mobile" && !String.IsNullOrEmpty(inputs.ClientId)) await AddInstallAsync(context, inputs.ClientId, token).ConfigureAwait(false);
            return context;
        }

        /// <summary>
        /// Server-side enforcement for one API request: kill switches of the route's features (section 13.2) and, when
        /// mobile.enforceMinimumOnApi is on, the minimum binary version (section 15.1).
        /// </summary>
        /// <param name="method">HTTP method.</param>
        /// <param name="path">Path without query.</param>
        /// <param name="context">Authenticated caller, or null for anonymous routes.</param>
        /// <param name="header">Request header reader.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The refusal, or null when the request may proceed.</returns>
        /// <exception cref="ArgumentNullException">The header reader is null.</exception>
        public async Task<AppGateDecision?> GateAsync(string method, string? path, RequestContext? context, Func<string, string?> header, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(header);
            string p = path ?? String.Empty;
            string? client = header("X-Client");
            string? version = header("X-Client-Version");
            if (_Settings.AppConfig.EnforceMinimumOnApi && String.Equals(client, "mobile", StringComparison.OrdinalIgnoreCase) && !Exempt(p))
            {
                string? platform = header("X-Client-Platform") ?? PlatformOfApp(header("X-TK-App"));
                string? minimum = VersionFor(_Property.Mobile.MinimumBinaryVersion, platform);
                int? cmp = minimum == null ? null : SemverRange.CompareVersions(version, minimum);
                if (cmp != null && cmp < 0) return new AppGateDecision { StatusCode = 426, Error = "ClientUpdateRequired", Message = "Update the app to version " + minimum + " or later." };
            }
            List<string> features = KillSwitchCatalog.FeaturesFor(method, p);
            if (features.Count == 0) return null;
            await RefreshAsync(token).ConfigureAwait(false);
            List<FeatureFlag> relevant = _Switches.Where(s => s.BlocksServer && s.Rules.Count > 0 && features.Contains(s.Key)).ToList();
            if (relevant.Count == 0) return null;
            FlagEvaluationContext x = new FlagEvaluationContext
            {
                Platform = header("X-Client-Platform") ?? PlatformOfApp(header("X-TK-App")) ?? (String.Equals(client, "web", StringComparison.OrdinalIgnoreCase) ? "web" : null),
                Client = client?.ToLowerInvariant(),
                AppVersion = version,
                UpdateId = header("X-Client-Update-Id"),
                Country = header("CloudFront-Viewer-Country"),
                SignedIn = context != null && !String.IsNullOrEmpty(context.UserId),
                UserId = context?.UserId,
                SubjectId = context?.UserId ?? header("X-TK-Install-Id") ?? header("X-TK-Visitor-Id"),
                Membership = context == null || String.IsNullOrEmpty(context.UserId) ? null : context.HasMembership ? "Active" : "Inactive",
                IsSystemAdmin = context != null && context.IsAdmin,
                NowUtc = DateTime.UtcNow
            };
            if (context != null && !String.IsNullOrEmpty(context.UserId))
            {
                x.Roles.Add("User");
                if (context.IsAdmin) x.Roles.Add("SystemAdmin");
                if (context.IsModerator) x.Roles.Add("Moderator");
                if (context.IsReleaseManager) x.Roles.Add("ReleaseManager");
                if (relevant.Any(sw => sw.Rules.Any(r => r.Conditions.Any(c => c.Attribute == "profileField")))) await AddProfileFieldsAsync(x, context.UserId, token).ConfigureAwait(false);
            }
            if (relevant.Any(sw => sw.Rules.Any(r => r.Conditions.Any(c => c.Attribute == "runtimeVersion" || c.Attribute == "channel"))))
            {
                string? installId = header("X-TK-Install-Id");
                x.Channel = header("X-Client-Channel");
                if (!String.IsNullOrEmpty(installId)) await AddInstallAsync(x, installId, token).ConfigureAwait(false);
            }
            foreach (string feature in features)
            {
                FeatureFlag? sw = relevant.FirstOrDefault(s => s.Key == feature);
                if (sw == null) continue;
                FlagEvaluationResult result = FeatureFlagEvaluator.Evaluate(sw, x);
                if (result.Value != "true") continue;
                if (x.IsSystemAdmin && !result.RuleTargetsAdmins) continue;
                TrellisKitTelemetry.Add(LiveUpdatesTelemetry.KillSwitchBlocked, 1, "feature", feature, "client", x.Client ?? "unknown", "trelliskit.property", _PropertyId);
                return new AppGateDecision { StatusCode = 503, Error = "FeatureDisabled", Message = sw.Message ?? "This feature is temporarily unavailable.", Feature = feature, RetryAfterSeconds = 60 };
            }
            return null;
        }

        /// <summary>
        /// Whether a kill switch is on (it has any rule, targeted or for everyone). Background work of a feature without
        /// routes, such as the broadcast send worker, pauses while its switch is on (docs/LIVE_UPDATES_CONTRACT.md section 13).
        /// </summary>
        /// <param name="featureKey">Engine feature key.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True when the switch is on.</returns>
        public async Task<bool> IsKillSwitchOnAsync(string featureKey, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(featureKey)) return false;
            await RefreshAsync(token).ConfigureAwait(false);
            return _Switches.Any(s => s.Key == featureKey && s.Rules.Count > 0);
        }

        /// <summary>
        /// Evaluate a server flag (clients include server) for backend services; null when the key is unknown.
        /// </summary>
        /// <param name="key">Flag key.</param>
        /// <param name="context">Caller attributes.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Typed value, or null.</returns>
        public async Task<object?> GetServerFlagAsync(string key, FlagEvaluationContext context, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(context);
            await RefreshAsync(token).ConfigureAwait(false);
            FeatureFlag? flag = _Published.FirstOrDefault(f => f.Key == key && f.State != FeatureFlagStateEnum.Archived && f.Clients.Contains("server"));
            if (flag == null) return null;
            FlagEvaluationResult result = FeatureFlagEvaluator.Evaluate(flag, context);
            CountEvaluation("server", result.Reason);
            return Typed(result.Value, flag.ValueType);
        }

        /// <summary>
        /// The binary version policy for a platform and app version (section 15.2).
        /// </summary>
        /// <param name="platform">ios or android.</param>
        /// <param name="appVersion">App version.</param>
        /// <returns>Policy.</returns>
        public AppBinaryPolicyView BinaryPolicy(string? platform, string? appVersion)
        {
            string? minimum = VersionFor(_Property.Mobile.MinimumBinaryVersion, platform);
            string? latest = VersionFor(_Property.Mobile.LatestBinaryVersion, platform);
            string? store = null;
            if (platform != null && _Property.Mobile.StoreUrls != null && _Property.Mobile.StoreUrls.TryGetValue(platform, out string? url)) store = url;
            if (store == null && platform == "ios" && !String.IsNullOrWhiteSpace(_Property.Mobile.AppStoreId)) store = "https://apps.apple.com/app/id" + _Property.Mobile.AppStoreId.Trim();
            return new AppBinaryPolicyView
            {
                MinimumVersion = minimum,
                LatestVersion = latest,
                UpdateRequired = minimum != null && (SemverRange.CompareVersions(appVersion, minimum) ?? 0) < 0,
                UpdateRecommended = latest != null && (SemverRange.CompareVersions(appVersion, latest) ?? 0) < 0,
                StoreUrl = store
            };
        }

        #endregion

        #region Private-Methods

        private async Task EnsureKillSwitchesAsync(CancellationToken token)
        {
            if (_Seeded) return;
            DateTime now = DateTime.UtcNow;
            List<FeatureFlag> seeds = KillSwitchCatalog.FeatureKeys.Select(key => new FeatureFlag
            {
                RevisionId = IdGenerator.Generate("ffv_"),
                Id = IdGenerator.Generate("ffl_"),
                Key = key,
                Name = key,
                Kind = FeatureFlagKindEnum.KillSwitch,
                ValueType = FeatureFlagValueTypeEnum.Boolean,
                DefaultValue = "false",
                OffValue = "false",
                Enabled = true,
                BlocksServer = true,
                State = FeatureFlagStateEnum.Published,
                Clients = new List<string> { "mobile", "web", "server" },
                CreatedUtc = now,
                UpdatedUtc = now
            }).ToList();
            await _Database.AppConfig.EnsureKillSwitchesAsync(seeds, token).ConfigureAwait(false);
            _Seeded = true;
        }

        private async Task RefreshAsync(CancellationToken token)
        {
            int seconds = Math.Min(_Settings.AppConfig.PropagationSeconds, _Settings.AppConfig.KillSwitchPropagationSeconds);
            if ((DateTime.UtcNow - _CheckedUtc).TotalSeconds < seconds) return;
            await _Lock.WaitAsync(token).ConfigureAwait(false);
            try
            {
                if ((DateTime.UtcNow - _CheckedUtc).TotalSeconds < seconds) return;
                await EnsureKillSwitchesAsync(token).ConfigureAwait(false);
                AppContentState state = await _Database.AppConfig.ReadStateAsync(token).ConfigureAwait(false);
                if (state.ContentVersion != _CachedVersion)
                {
                    _Published = await _Database.AppConfig.ListCurrentAsync(false, _Settings.AppConfig.MaxFlags + 100, token).ConfigureAwait(false);
                    _CachedVersion = state.ContentVersion;
                }
                if (state.KillSwitchRevision != _CachedRevision || _Switches.Count == 0)
                {
                    _Switches = await _Database.AppConfig.ListCurrentAsync(true, 500, token).ConfigureAwait(false);
                    _CachedRevision = state.KillSwitchRevision;
                }
                _CheckedUtc = DateTime.UtcNow;
            }
            finally
            {
                _Lock.Release();
            }
        }

        private async Task<ContentVersion> PublishDraftsAsync(string message, long expected, int changes, ContentDraftCounts content, ContentVersionKindEnum kind, long? restoredFrom, RequestInfo info, CancellationToken token)
        {
            ContentVersion version = new ContentVersion
            {
                Id = IdGenerator.Generate("cve_"),
                Message = message,
                Kind = kind,
                RestoredFromVersion = restoredFrom,
                PublishedByUserId = info.Context?.UserId,
                PublishedByName = info.Context?.PrincipalName ?? "admin",
                FlagChanges = changes,
                StringChanges = (int)content.Strings,
                BannerChanges = (int)content.Banners,
                AssetChanges = (int)content.Assets,
                PublishedUtc = DateTime.UtcNow
            };
            if (!await _Database.AppConfig.PublishAsync(expected, version, token).ConfigureAwait(false))
            {
                AppContentState state = await _Database.AppConfig.ReadStateAsync(token).ConfigureAwait(false);
                throw new ApiErrorException(409, "ContentVersionConflict", "Content version " + state.ContentVersion + " was published since version " + expected + "; review the new changes and publish again.",
                    new Dictionary<string, string> { { "expectedVersion", state.ContentVersion.ToString(CultureInfo.InvariantCulture) } });
            }
            _CheckedUtc = DateTime.MinValue;
            Content?.Invalidate();
            long retain = version.Version - _Settings.AppConfig.ChangeLogRetentionVersions;
            if (retain > 0) await _Database.AppConfig.PruneChangesAsync(retain, token).ConfigureAwait(false);
            return version;
        }

        private async Task<ContentVersionView> VersionViewAsync(ContentVersion v, bool detail, CancellationToken token)
        {
            ObjectMetadata metadata = await _Metadata.LoadOneAsync(TargetTypeEnum.ContentVersion, v.Id, true, token).ConfigureAwait(false);
            ContentVersionView view = new ContentVersionView
            {
                Id = v.Id,
                Version = v.Version,
                Message = v.Message,
                Kind = v.Kind,
                RestoredFromVersion = v.RestoredFromVersion,
                PublishedBy = v.PublishedByName,
                PublishedUtc = v.PublishedUtc,
                CreatedUtc = v.PublishedUtc,
                Labels = metadata.Labels,
                Tags = metadata.Tags
            };
            view.ChangeCounts["flags"] = v.FlagChanges;
            view.ChangeCounts["strings"] = v.StringChanges;
            view.ChangeCounts["banners"] = v.BannerChanges;
            view.ChangeCounts["assets"] = v.AssetChanges;
            if (detail)
            {
                EnumerationRequest request = _Enumeration.Prepare(new EnumerationQuery { MaxResults = 50 }, LiveUpdateCollections.Changes, true, "v" + v.Version);
                EnumerationResult<FeatureFlag> page = await _Database.AppConfig.EnumerateChangesAsync(request, v.Version, token).ConfigureAwait(false);
                view.Changes = new FlagHistoryPreview
                {
                    Objects = page.Objects.Select(f => View(f, new ObjectMetadata(), false)).ToList(),
                    TotalRecords = page.TotalRecords ?? page.Objects.Count,
                    Enumerate = _Base + "/versions/" + v.Version + "/changes/enumerate"
                };
            }
            return view;
        }

        private async Task<ContentVersion> RequireVersionAsync(string version, CancellationToken token)
        {
            if (!Int64.TryParse(version, NumberStyles.None, CultureInfo.InvariantCulture, out long number)) throw ApiErrorException.NotFound("Content version");
            return await _Database.AppConfig.ReadVersionAsync(number, token).ConfigureAwait(false) ?? throw ApiErrorException.NotFound("Content version");
        }

        private async Task<FeatureFlag> RequireFlagAsync(string id, CancellationToken token)
        {
            FeatureFlag? flag = await _Database.AppConfig.ReadDraftAsync(id, token).ConfigureAwait(false) ?? await _Database.AppConfig.ReadCurrentAsync(id, token).ConfigureAwait(false);
            if (flag == null || flag.Kind == FeatureFlagKindEnum.KillSwitch) throw ApiErrorException.NotFound("Feature flag");
            return flag;
        }

        private async Task SaveDraftAsync(FeatureFlag flag, CancellationToken token)
        {
            try
            {
                await _Database.AppConfig.SaveDraftAsync(flag, token).ConfigureAwait(false);
            }
            catch (DuplicateRecordException)
            {
                throw new ApiErrorException(409, "FlagKeyInUse", "The flag key " + flag.Key + " is in use.");
            }
        }

        private async Task ValidateFlagAsync(FeatureFlag flag, string? objectId, bool checkKey, CancellationToken token)
        {
            Dictionary<string, string> errors = new Dictionary<string, string>();
            if (flag.Kind == FeatureFlagKindEnum.KillSwitch) errors["kind"] = "Kill switches are managed at /api/v1/admin/app/kill-switches.";
            if (flag.Key.Length < 3 || flag.Key.Length > 64 || !_FlagKey.IsMatch(flag.Key)) errors["key"] = "Use 3 to 64 characters: start with a lowercase letter, then letters and digits only (no spaces, dashes, or underscores), with up to 3 more dot-separated parts that also start with a lowercase letter, for example community.newComposer.";
            else if (KillSwitchCatalog.IsFeature(flag.Key)) errors["key"] = "Engine feature keys are reserved for kill switches.";
            if (flag.Name.Length < 1 || flag.Name.Length > 100) errors["name"] = "Required, 1 to 100 characters.";
            if (flag.Description != null && flag.Description.Length > 500) errors["description"] = "At most 500 characters.";
            if (flag.Clients.Count == 0 || flag.Clients.Any(c => !_Clients.Contains(c))) errors["clients"] = "One or more of mobile, web, server.";
            if (flag.Kind == FeatureFlagKindEnum.Variant)
            {
                if (flag.Variants.Count < 2 || flag.Variants.Count > 20 || flag.Variants.Distinct(StringComparer.Ordinal).Count() != flag.Variants.Count || flag.Variants.Any(v => String.IsNullOrEmpty(v) || v.Length > 100))
                    errors["variants"] = "2 to 20 distinct values of 1 to 100 characters.";
                else
                {
                    if (!flag.Variants.Contains(flag.DefaultValue)) errors["defaultValue"] = "Must be one of the variants.";
                    if (!flag.Variants.Contains(flag.OffValue)) errors["offValue"] = "Must be one of the variants.";
                    for (int i = 0; i < flag.Rules.Count; i++) if (!flag.Variants.Contains(flag.Rules[i].Value)) errors["rules[" + i + "].value"] = "Must be one of the variants.";
                }
            }
            if (errors.Count > 0) throw ApiErrorException.Validation(errors);
            Dictionary<string, string> ruleErrors = FeatureFlagEvaluator.Validate(flag.Rules, _Settings.AppConfig.MaxRulesPerFlag, _Settings.AppConfig.MaxConditionsPerRule, _Settings.AppConfig.MaxValuesPerCondition);
            if (ruleErrors.Count == 0) ruleErrors = await ProfileFieldErrorsAsync(_Database, flag.Rules, token).ConfigureAwait(false);
            if (ruleErrors.Count > 0) throw new ApiErrorException(400, "InvalidTargetingRule", "A targeting rule is invalid.", ruleErrors);
            if (checkKey && await _Database.AppConfig.KeyInUseAsync(flag.Key, objectId, token).ConfigureAwait(false)) throw new ApiErrorException(409, "FlagKeyInUse", "The flag key " + flag.Key + " is in use.");
        }

        private void Apply(FeatureFlag flag, FeatureFlagRequest r, HashSet<string> present, FeatureFlag? previous)
        {
            Dictionary<string, string> errors = new Dictionary<string, string>();
            if (present.Contains("key")) flag.Key = (r.Key ?? String.Empty).Trim();
            if (present.Contains("name")) flag.Name = (r.Name ?? String.Empty).Trim();
            if (present.Contains("description")) flag.Description = String.IsNullOrWhiteSpace(r.Description) ? null : r.Description.Trim();
            if (present.Contains("kind") && r.Kind != null) flag.Kind = r.Kind.Value;
            if (flag.Kind == FeatureFlagKindEnum.Boolean) flag.ValueType = FeatureFlagValueTypeEnum.Boolean;
            else if (flag.Kind == FeatureFlagKindEnum.Variant) flag.ValueType = FeatureFlagValueTypeEnum.String;
            else if (present.Contains("valueType") && r.ValueType != null) flag.ValueType = r.ValueType.Value;
            if (present.Contains("variants") && r.Variants != null) flag.Variants = (r.Variants ?? new List<string>()).Select(v => (v ?? String.Empty).Trim()).ToList();
            if (present.Contains("enabled") && r.Enabled != null) flag.Enabled = r.Enabled.Value;
            if (present.Contains("clients") && r.Clients != null) flag.Clients = (r.Clients ?? new List<string>()).Select(c => (c ?? String.Empty).Trim().ToLowerInvariant()).Distinct(StringComparer.Ordinal).ToList();
            if (previous == null && flag.Clients.Count == 0) flag.Clients = new List<string> { "mobile", "web" };
            string fallback = flag.ValueType == FeatureFlagValueTypeEnum.Boolean ? "false" : flag.ValueType == FeatureFlagValueTypeEnum.String ? (flag.Variants.FirstOrDefault() ?? String.Empty) : "0";
            if (present.Contains("defaultValue")) flag.DefaultValue = Text(r.DefaultValue, flag.ValueType, "defaultValue", errors) ?? fallback;
            else if (previous == null || !Valid(flag.DefaultValue, flag.ValueType)) flag.DefaultValue = fallback;
            if (present.Contains("offValue")) flag.OffValue = Text(r.OffValue, flag.ValueType, "offValue", errors) ?? fallback;
            else if (previous == null || !Valid(flag.OffValue, flag.ValueType)) flag.OffValue = fallback;
            if (present.Contains("rules")) flag.Rules = BuildRules(r.Rules ?? new List<FlagRuleRequest>(), previous, flag.ValueType, false, errors);
            if (errors.Count > 0) throw new ApiErrorException(400, errors.Keys.Any(k => k.StartsWith("rules", StringComparison.Ordinal)) ? "InvalidTargetingRule" : "ValidationFailed", "One or more fields are invalid.", errors);
        }

        internal static List<FeatureFlagRule> BuildRules(List<FlagRuleRequest> requests, FeatureFlag? previous, FeatureFlagValueTypeEnum type, bool killSwitch, Dictionary<string, string> errors)
        {
            List<FeatureFlagRule> rules = new List<FeatureFlagRule>();
            HashSet<string> known = new HashSet<string>(previous?.Rules.Select(x => x.Id) ?? Enumerable.Empty<string>(), StringComparer.Ordinal);
            HashSet<string> used = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < requests.Count; i++)
            {
                FlagRuleRequest r = requests[i] ?? new FlagRuleRequest();
                string id = !String.IsNullOrEmpty(r.Id) && known.Contains(r.Id) && !used.Contains(r.Id) ? r.Id : IdGenerator.Generate("ffr_");
                used.Add(id);
                int bp = r.RolloutPercentage == null ? 10000 : LiveUpdateRules.ToBasisPoints(r.RolloutPercentage.Value);
                if (bp <= 0) errors["rules[" + i + "].rolloutPercentage"] = "Must be 0.01 to 100.";
                string value = killSwitch ? "true" : Text(r.Value, type, "rules[" + i + "].value", errors) ?? String.Empty;
                rules.Add(new FeatureFlagRule
                {
                    Id = id,
                    Position = i + 1,
                    Description = String.IsNullOrWhiteSpace(r.Description) ? null : r.Description.Trim(),
                    Value = value,
                    RolloutBasisPoints = Math.Max(bp, 1),
                    Conditions = (r.Conditions ?? new List<FlagConditionRequest>()).Select((c, j) => new FeatureFlagCondition
                    {
                        Position = j,
                        Attribute = (c?.Attribute ?? String.Empty).Trim(),
                        Operator = (c?.Operator ?? String.Empty).Trim(),
                        Argument = String.IsNullOrWhiteSpace(c?.Argument) ? null : c!.Argument!.Trim(),
                        Values = (c?.Values ?? new List<string>()).Select(v => v ?? String.Empty).ToList()
                    }).ToList()
                });
            }
            return rules;
        }

        private static string? Text(JsonElement? value, FeatureFlagValueTypeEnum type, string field, Dictionary<string, string> errors)
        {
            if (value == null || value.Value.ValueKind == JsonValueKind.Null || value.Value.ValueKind == JsonValueKind.Undefined)
            {
                errors[field] = "Required.";
                return null;
            }
            JsonElement v = value.Value;
            switch (type)
            {
                case FeatureFlagValueTypeEnum.Boolean:
                    if (v.ValueKind == JsonValueKind.True) return "true";
                    if (v.ValueKind == JsonValueKind.False) return "false";
                    errors[field] = "Must be true or false.";
                    return null;
                case FeatureFlagValueTypeEnum.Integer:
                    if (v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out long l)) return l.ToString(CultureInfo.InvariantCulture);
                    errors[field] = "Must be a whole number.";
                    return null;
                case FeatureFlagValueTypeEnum.Decimal:
                    if (v.ValueKind == JsonValueKind.Number && v.TryGetDecimal(out decimal d)) return d.ToString(CultureInfo.InvariantCulture);
                    errors[field] = "Must be a number.";
                    return null;
                default:
                    if (v.ValueKind == JsonValueKind.String && (v.GetString() ?? String.Empty).Length <= 500) return v.GetString() ?? String.Empty;
                    errors[field] = "Must be text of at most 500 characters.";
                    return null;
            }
        }

        private static bool Valid(string text, FeatureFlagValueTypeEnum type)
        {
            switch (type)
            {
                case FeatureFlagValueTypeEnum.Boolean: return text == "true" || text == "false";
                case FeatureFlagValueTypeEnum.Integer: return Int64.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out long _);
                case FeatureFlagValueTypeEnum.Decimal: return Decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal _);
                default: return text != null;
            }
        }

        private static object? Typed(string text, FeatureFlagValueTypeEnum type)
        {
            switch (type)
            {
                case FeatureFlagValueTypeEnum.Boolean: return text == "true";
                case FeatureFlagValueTypeEnum.Integer: return Int64.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out long l) ? l : 0L;
                case FeatureFlagValueTypeEnum.Decimal: return Decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal d) ? d : 0m;
                default: return text;
            }
        }

        private async Task AddMemberAsync(FlagEvaluationContext context, string userId, SessionPrincipal? principal, CancellationToken token)
        {
            context.SignedIn = true;
            context.UserId = userId;
            context.SubjectId = userId;
            User? user = await _Database.Users.ReadAsync(userId, token).ConfigureAwait(false);
            if (user == null) return;
            context.Roles.Add("User");
            if (principal != null)
            {
                if (principal.IsSystemAdmin) context.Roles.Add("SystemAdmin");
                if (principal.IsModerator) context.Roles.Add("Moderator");
                if (principal.IsReleaseManager) context.Roles.Add("ReleaseManager");
                context.Membership = principal.HasMembership ? "Active" : "Inactive";
                context.IsSystemAdmin = principal.IsSystemAdmin;
            }
            else context.Membership = "None";
            ObjectMetadata metadata = await _Metadata.LoadOneAsync(TargetTypeEnum.User, userId, true, token).ConfigureAwait(false);
            context.UserLabels = metadata.Labels;
            context.UserTags = metadata.Tags;
            await AddProfileFieldsAsync(context, userId, token).ConfigureAwait(false);
        }

        private async Task AddProfileFieldsAsync(FlagEvaluationContext context, string userId, CancellationToken token)
        {
            List<ProfileField> fields = (await _Database.ProfileFields.ReadAllAsync(token).ConfigureAwait(false)).Where(f => f.Filterable).ToList();
            if (fields.Count == 0) return;
            ProfileValueSet set = await _Database.ProfileValues.ReadForUserAsync(userId, token).ConfigureAwait(false);
            foreach (ProfileField field in fields)
            {
                if (!set.Values.TryGetValue(field.Id, out ProfileValue? value) || value == null) continue;
                List<string> values = value.OptionKeys.Count > 0 ? new List<string>(value.OptionKeys) : new List<string>();
                if (values.Count == 0)
                {
                    string? text = value.ValueText ?? value.ValueDate ?? (value.ValueBool == null ? null : value.ValueBool.Value ? "true" : "false") ??
                        value.ValueNumber?.ToString(CultureInfo.InvariantCulture) ?? value.ValueLevel?.ToString(CultureInfo.InvariantCulture);
                    if (!String.IsNullOrEmpty(text)) values.Add(text);
                }
                if (values.Count > 0) context.ProfileFields[field.Key] = values;
            }
        }

        private async Task AddInstallAsync(FlagEvaluationContext context, string installId, CancellationToken token)
        {
            if (InstallHash == null || String.IsNullOrWhiteSpace(installId) || installId.Length > 64) return;
            UpdateInstall? install = await _Database.LiveUpdates.ReadInstallAsync(InstallHash(installId.Trim()), token).ConfigureAwait(false);
            if (install == null) return;
            if (String.IsNullOrEmpty(context.RuntimeVersion)) context.RuntimeVersion = install.RuntimeVersion;
            if (String.IsNullOrEmpty(context.Channel)) context.Channel = install.Channel;
        }

        /// <summary>
        /// Field errors for profileField conditions that name an unknown or non-filterable profile field.
        /// </summary>
        /// <param name="database">Property-bound database driver.</param>
        /// <param name="rules">Rules.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Field errors (empty when valid).</returns>
        internal static async Task<Dictionary<string, string>> ProfileFieldErrorsAsync(DatabaseDriverBase database, List<FeatureFlagRule> rules, CancellationToken token)
        {
            Dictionary<string, string> errors = new Dictionary<string, string>();
            if (!rules.Any(r => r.Conditions.Any(c => c.Attribute == "profileField"))) return errors;
            HashSet<string> filterable = new HashSet<string>((await database.ProfileFields.ReadAllAsync(token).ConfigureAwait(false)).Where(f => f.Filterable).Select(f => f.Key), StringComparer.OrdinalIgnoreCase);
            for (int r = 0; r < rules.Count; r++)
            {
                for (int c = 0; c < rules[r].Conditions.Count; c++)
                {
                    FeatureFlagCondition condition = rules[r].Conditions[c];
                    if (condition.Attribute != "profileField") continue;
                    string? key = FeatureFlagEvaluator.ProfileFieldKey(condition);
                    if (key == null || !filterable.Contains(key)) errors["rules[" + r + "].conditions[" + c + "].values"] = "Use the key of a filterable profile field.";
                }
            }
            return errors;
        }

        private static string? VersionFor(JsonElement? value, string? platform)
        {
            if (value == null) return null;
            JsonElement v = value.Value;
            if (v.ValueKind == JsonValueKind.String) return String.IsNullOrWhiteSpace(v.GetString()) ? null : v.GetString()!.Trim();
            if (v.ValueKind == JsonValueKind.Object && platform != null && v.TryGetProperty(platform, out JsonElement p) && p.ValueKind == JsonValueKind.String) return p.GetString();
            return null;
        }

        private string? PlatformOfApp(string? appId)
        {
            if (String.IsNullOrEmpty(appId)) return null;
            if (String.Equals(appId, _Property.Mobile.BundleId, StringComparison.OrdinalIgnoreCase) && !String.Equals(appId, _Property.Mobile.AndroidPackage, StringComparison.OrdinalIgnoreCase)) return "ios";
            if (String.Equals(appId, _Property.Mobile.AndroidPackage, StringComparison.OrdinalIgnoreCase) && !String.Equals(appId, _Property.Mobile.BundleId, StringComparison.OrdinalIgnoreCase)) return "android";
            return null;
        }

        private static bool Exempt(string path)
        {
            string[] exempt = { "/auth", "/me", "/membership", "/property", "/updates", "/app", "/health", "/admin", "/platform", "/settings" };
            return exempt.Any(e => path.StartsWith(Constants.ApiPrefix + e, StringComparison.OrdinalIgnoreCase));
        }

        private static string Unquote(string value)
        {
            string v = value.Trim();
            if (v.StartsWith("W/", StringComparison.Ordinal)) v = v.Substring(2);
            return v.Trim('"');
        }

        private static HashSet<string> AllFields()
        {
            return new HashSet<string>(new[] { "key", "name", "description", "kind", "valueType", "variants", "defaultValue", "offValue", "enabled", "clients", "rules" }, StringComparer.Ordinal);
        }

        private static FeatureFlag Copy(FeatureFlag f)
        {
            return new FeatureFlag
            {
                RevisionId = f.RevisionId,
                Id = f.Id,
                Key = f.Key,
                Name = f.Name,
                Description = f.Description,
                Kind = f.Kind,
                ValueType = f.ValueType,
                Variants = new List<string>(f.Variants),
                DefaultValue = f.DefaultValue,
                OffValue = f.OffValue,
                Enabled = f.Enabled,
                Clients = new List<string>(f.Clients),
                Rules = f.Rules.Select(r => new FeatureFlagRule
                {
                    Id = r.Id,
                    Position = r.Position,
                    Description = r.Description,
                    Value = r.Value,
                    RolloutBasisPoints = r.RolloutBasisPoints,
                    Conditions = r.Conditions.Select(c => new FeatureFlagCondition { Id = c.Id, Position = c.Position, Attribute = c.Attribute, Operator = c.Operator, Argument = c.Argument, Values = new List<string>(c.Values) }).ToList()
                }).ToList(),
                Message = f.Message,
                BlocksServer = f.BlocksServer,
                State = f.State,
                CreatedUtc = f.CreatedUtc,
                UpdatedUtc = f.UpdatedUtc
            };
        }

        private static FeatureFlagView View(FeatureFlag f, ObjectMetadata metadata, bool hasDraft)
        {
            return new FeatureFlagView
            {
                Id = f.Id,
                Key = f.Key,
                Name = f.Name,
                Description = f.Description,
                Kind = f.Kind,
                ValueType = f.ValueType,
                Variants = f.Variants,
                DefaultValue = Typed(f.DefaultValue, f.ValueType),
                OffValue = Typed(f.OffValue, f.ValueType),
                Enabled = f.Enabled,
                Clients = f.Clients,
                Rules = f.Rules.Select(r => RuleView(r, f.ValueType)).ToList(),
                Message = f.Message,
                BlocksServer = f.BlocksServer,
                State = f.State,
                HasDraftChanges = hasDraft,
                PublishedInVersion = f.IsDraft ? null : f.ValidFromVersion,
                ETag = "\"" + f.RevisionId + "\"",
                Labels = metadata.Labels,
                Tags = metadata.Tags,
                CreatedUtc = f.CreatedUtc,
                UpdatedUtc = f.UpdatedUtc
            };
        }

        internal static FlagRuleView RuleView(FeatureFlagRule r, FeatureFlagValueTypeEnum type)
        {
            return new FlagRuleView
            {
                Id = r.Id,
                Position = r.Position,
                Description = r.Description,
                Value = Typed(r.Value, type),
                RolloutPercentage = LiveUpdateRules.ToPercent(r.RolloutBasisPoints),
                Conditions = r.Conditions.Select(c => new FlagConditionView { Attribute = c.Attribute, Operator = c.Operator, Argument = c.Argument, Values = c.Values }).ToList()
            };
        }

        private static KillSwitchView KillSwitchViewOf(FeatureFlag f, KillSwitchState? state, ObjectMetadata metadata)
        {
            return new KillSwitchView
            {
                Id = f.Id,
                FeatureKey = f.Key,
                Killed = f.Rules.Any(r => r.Value == "true"),
                KilledForEveryone = f.Rules.Any(r => r.Value == "true" && r.Conditions.Count == 0 && r.RolloutBasisPoints >= 10000),
                Message = f.Message,
                BlocksServer = f.BlocksServer,
                RuleCount = f.Rules.Count,
                Rules = f.Rules.Select(r => RuleView(r, FeatureFlagValueTypeEnum.Boolean)).ToList(),
                Revision = state?.Revision ?? 0,
                LastChangedBy = state?.LastChangedByName,
                LastChangedReason = state?.LastChangedReason,
                LastChangedUtc = state?.LastChangedUtc,
                Labels = metadata.Labels,
                Tags = metadata.Tags,
                CreatedUtc = f.CreatedUtc
            };
        }

        private void CountEvaluation(string surface, string reason)
        {
            string label = reason == "Disabled" ? "disabled" : reason == "Rule" ? "rule" : "default";
            TrellisKitTelemetry.Add(LiveUpdatesTelemetry.FlagEvaluations, 1, "surface", surface, "reason", label, "trelliskit.property", _PropertyId);
        }

        #endregion
    }
}
