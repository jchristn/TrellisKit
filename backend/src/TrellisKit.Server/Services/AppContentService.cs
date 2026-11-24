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
    using System.Text.RegularExpressions;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core;
    using TrellisKit.Core.Database;
    using TrellisKit.Core.Enumeration;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Exceptions;
    using TrellisKit.Core.Helpers;
    using TrellisKit.Core.Integrations.Interfaces;
    using TrellisKit.Core.Integrations.Models;
    using TrellisKit.Core.Models;
    using TrellisKit.Core.Requests;
    using TrellisKit.Core.Responses;
    using TrellisKit.Core.Settings;

    /// <summary>
    /// Remote strings, banners, and remote assets (docs/LIVE_UPDATES_CONTRACT.md sections 11, 14, and 16): admin drafts
    /// validated against the engine string catalog and the targeting rules, the published content cached per content
    /// version, the per-caller strings, banners, and assets parts of the app manifest (deltas since a version, or a full
    /// refresh), the anonymous re-enumeration routes, and content asset storage and delivery. Drafts publish, restore,
    /// and discard together with feature flags through <see cref="AppConfigService"/>.
    /// Thread safety: safe to call concurrently.
    /// </summary>
    public class AppContentService
    {
        #region Public-Members

        /// <summary>
        /// Banner styles.
        /// </summary>
        public static readonly IReadOnlyList<string> Styles = new[] { "Info", "Success", "Warning", "Critical" };

        /// <summary>
        /// Banner placements.
        /// </summary>
        public static readonly IReadOnlyList<string> Placements = new[] { "Global", "Home", "Feed", "Forums", "Library", "Events", "Directory", "Messages", "SignIn", "Account" };

        /// <summary>
        /// Remote asset upload content types.
        /// </summary>
        public static readonly IReadOnlyList<string> UploadTypes = new[] { "image/png", "image/jpeg", "image/webp" };

        /// <summary>
        /// Public path of local content asset delivery.
        /// </summary>
        public const string ContentAssetPath = Constants.ApiPrefix + "/app/content-assets";

        #endregion

        #region Private-Members

        private const string _Base = Constants.ApiPrefix + "/admin/app";
        private const int _MaxLongEdge = 4096;
        private static readonly Regex _Key = new Regex("^[a-z][a-zA-Z0-9]*(\\.[a-z][a-zA-Z0-9]*){0,3}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
        private static readonly Regex _Token = new Regex("\\{([A-Za-z][A-Za-z0-9]*)\\}", RegexOptions.Compiled | RegexOptions.CultureInvariant);
        private static readonly HashSet<string> _Clients = new HashSet<string>(StringComparer.Ordinal) { "mobile", "web" };
        private static readonly Lazy<StringCatalog> _Catalog = new Lazy<StringCatalog>(StringCatalog.LoadEmbedded, LazyThreadSafetyMode.ExecutionAndPublication);
        private readonly DatabaseDriverBase _Database;
        private readonly AppSettings _Settings;
        private readonly EnumerationService _Enumeration;
        private readonly MetadataService _Metadata;
        private readonly AuditService _Audit;
        private readonly UpdateObjectStore _Store;
        private readonly IImageProcessor? _Images;
        private readonly WordFilterService? _Words;
        private readonly RateLimitService _Limits;
        private readonly string _PropertyId;
        private readonly SemaphoreSlim _Lock = new SemaphoreSlim(1, 1);
        private bool _CatalogSynced = false;
        private long _CachedVersion = -1;
        private List<RemoteStringOverride> _Strings = new List<RemoteStringOverride>();
        private List<AppBanner> _Banners = new List<AppBanner>();
        private List<RemoteAsset> _Assets = new List<RemoteAsset>();

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the service.
        /// </summary>
        /// <param name="database">Property-bound database driver.</param>
        /// <param name="settings">Running settings.</param>
        /// <param name="enumeration">Enumeration service.</param>
        /// <param name="metadata">Metadata service.</param>
        /// <param name="audit">Audit service.</param>
        /// <param name="store">Update storage (content assets live under app-content/).</param>
        /// <param name="images">Image processor (re-encodes asset uploads), or null when unavailable.</param>
        /// <param name="words">Word filter, or null.</param>
        /// <param name="limits">Rate limits.</param>
        /// <param name="propertyId">Property id.</param>
        /// <exception cref="ArgumentNullException">A required argument is null.</exception>
        public AppContentService(DatabaseDriverBase database, AppSettings settings, EnumerationService enumeration, MetadataService metadata, AuditService audit, UpdateObjectStore store,
            IImageProcessor? images, WordFilterService? words, RateLimitService limits, string propertyId)
        {
            _Database = database ?? throw new ArgumentNullException(nameof(database));
            _Settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _Enumeration = enumeration ?? throw new ArgumentNullException(nameof(enumeration));
            _Metadata = metadata ?? throw new ArgumentNullException(nameof(metadata));
            _Audit = audit ?? throw new ArgumentNullException(nameof(audit));
            _Store = store ?? throw new ArgumentNullException(nameof(store));
            _Limits = limits ?? throw new ArgumentNullException(nameof(limits));
            _Images = images;
            _Words = words;
            _PropertyId = propertyId ?? throw new ArgumentNullException(nameof(propertyId));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// The engine string catalog embedded in the server.
        /// </summary>
        /// <returns>Catalog.</returns>
        public static StringCatalog Catalog()
        {
            return _Catalog.Value;
        }

        /// <summary>
        /// Object key of a content asset.
        /// </summary>
        /// <param name="propertyId">Property id.</param>
        /// <param name="sha256Hex">SHA-256 hex.</param>
        /// <returns>Key.</returns>
        public static string ContentAssetKey(string propertyId, string sha256Hex)
        {
            return "app-content/" + propertyId + "/" + sha256Hex;
        }

        /// <summary>
        /// The content hash of a banner (title and body): a dismissal holds while it is unchanged.
        /// </summary>
        /// <param name="title">Title.</param>
        /// <param name="body">Body.</param>
        /// <returns>16 hex characters.</returns>
        public static string ContentHash(string title, string body)
        {
            byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes((title ?? String.Empty) + "\n" + (body ?? String.Empty)));
            return Convert.ToHexString(hash).Substring(0, 16).ToLowerInvariant();
        }

        #region Strings

        /// <summary>
        /// POST /api/v1/admin/app/strings/enumerate.
        /// </summary>
        /// <param name="query">Query.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Page of catalog keys with their overrides.</returns>
        /// <exception cref="ApiErrorException">400 ValidationFailed.</exception>
        public async Task<EnumerationResult<RemoteStringView>> EnumerateStringsAsync(EnumerationQuery? query, CancellationToken token = default)
        {
            await EnsureCatalogAsync(token).ConfigureAwait(false);
            EnumerationRequest request = _Enumeration.Prepare(query, LiveUpdateCollections.Strings, true);
            Dictionary<string, string> errors = new Dictionary<string, string>();
            RemoteStringFilter filter = new RemoteStringFilter
            {
                Client = request.Query.FilterString("client"),
                Overridden = request.Query.FilterBool("overridden", errors),
                HasDraftChanges = request.Query.FilterBool("hasDraftChanges", errors)
            };
            if (filter.Client != null && !_Clients.Contains(filter.Client)) errors["filters.client"] = "Use web or mobile.";
            if (errors.Count > 0) throw ApiErrorException.Validation(errors);
            EnumerationResult<StringCatalogEntry> page = await _Database.AppContent.EnumerateCatalogAsync(request, filter, token).ConfigureAwait(false);
            List<string> keys = page.Objects.Select(e => e.Key).ToList();
            List<RemoteStringOverride> heads = await _Database.AppContent.ListStringHeadsAsync(keys, token).ConfigureAwait(false);
            Dictionary<string, ObjectMetadata> metadata = await _Metadata.LoadAsync(TargetTypeEnum.RemoteString, keys, true, token).ConfigureAwait(false);
            return page.With(page.Objects.Select(e => StringView(e, heads.FirstOrDefault(h => h.Key == e.Key && h.IsDraft), heads.FirstOrDefault(h => h.Key == e.Key && !h.IsDraft),
                metadata.TryGetValue(e.Key, out ObjectMetadata? m) ? m : new ObjectMetadata())).ToList());
        }

        /// <summary>
        /// GET /api/v1/admin/app/strings/{key}.
        /// </summary>
        /// <param name="key">Catalog key.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The key, its override, and history.</returns>
        /// <exception cref="ApiErrorException">404 NotFound.</exception>
        public async Task<RemoteStringDetailView> GetStringAsync(string key, CancellationToken token = default)
        {
            StringCatalogEntry entry = await RequireEntryAsync(key, false, token).ConfigureAwait(false);
            RemoteStringView view = await StringViewAsync(entry, token).ConfigureAwait(false);
            EnumerationRequest request = _Enumeration.Prepare(new EnumerationQuery { MaxResults = 10 }, LiveUpdateCollections.StringHistory, true, key);
            EnumerationResult<RemoteStringOverride> history = await _Database.AppContent.EnumerateStringHistoryAsync(request, key, token).ConfigureAwait(false);
            return new RemoteStringDetailView
            {
                String = view,
                History = new ContentHistoryPreview
                {
                    Objects = history.Objects.Select(h => (object)StringView(entry, null, h, new ObjectMetadata { Labels = view.Labels, Tags = view.Tags })).ToList(),
                    TotalRecords = history.TotalRecords ?? history.Objects.Count,
                    Enumerate = _Base + "/strings/" + key + "/history/enumerate"
                }
            };
        }

        /// <summary>
        /// POST /api/v1/admin/app/strings/{key}/history/enumerate.
        /// </summary>
        /// <param name="key">Catalog key.</param>
        /// <param name="query">Query.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Published revisions, newest first.</returns>
        /// <exception cref="ApiErrorException">404 NotFound.</exception>
        public async Task<EnumerationResult<RemoteStringView>> EnumerateStringHistoryAsync(string key, EnumerationQuery? query, CancellationToken token = default)
        {
            StringCatalogEntry entry = await RequireEntryAsync(key, false, token).ConfigureAwait(false);
            EnumerationRequest request = _Enumeration.Prepare(query, LiveUpdateCollections.StringHistory, true, key);
            EnumerationResult<RemoteStringOverride> page = await _Database.AppContent.EnumerateStringHistoryAsync(request, key, token).ConfigureAwait(false);
            ObjectMetadata metadata = await _Metadata.LoadOneAsync(TargetTypeEnum.RemoteString, key, true, token).ConfigureAwait(false);
            return page.With(page.Objects.Select(h => StringView(entry, null, h, metadata)).ToList());
        }

        /// <summary>
        /// PUT /api/v1/admin/app/strings/{key}: upsert the draft override. Audited appConfig.string.update.
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="key">Catalog key.</param>
        /// <param name="request">Body.</param>
        /// <param name="info">Request information.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The key with its draft.</returns>
        /// <exception cref="ApiErrorException">400 UnknownStringKey, InvalidStringTokens, or ValidationFailed.</exception>
        public async Task<RemoteStringView> PutStringAsync(ContentActor actor, string key, RemoteStringRequest request, RequestInfo info, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            ArgumentNullException.ThrowIfNull(request);
            StringCatalogEntry entry = await RequireEntryAsync(key, true, token).ConfigureAwait(false);
            Dictionary<string, string> errors = new Dictionary<string, string>();
            string value = request.Value ?? String.Empty;
            if (value.Trim().Length == 0 || value.Length > entry.MaxLength) errors["value"] = "Required, 1 to " + entry.MaxLength + " characters.";
            string? notes = String.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim();
            if (notes != null && notes.Length > 500) errors["notes"] = "At most 500 characters.";
            if (errors.Count > 0) throw ApiErrorException.Validation(errors);
            List<string> unknown = _Token.Matches(value).Select(m => m.Groups[1].Value).Distinct(StringComparer.Ordinal).Where(t => !entry.Tokens.Contains(t, StringComparer.Ordinal)).ToList();
            if (unknown.Count > 0)
                throw new ApiErrorException(400, "InvalidStringTokens", "The override uses tokens the key does not declare.",
                    new Dictionary<string, string> { { "value", "Unknown tokens: " + String.Join(", ", unknown.Select(t => "{" + t + "}")) + (entry.Tokens.Count == 0 ? ". This key takes no tokens." : ". Allowed: " + String.Join(", ", entry.Tokens.Select(t => "{" + t + "}")) + ".") } });
            if (_Words != null && await _Words.IsBlockedAsync(value, token).ConfigureAwait(false)) throw ApiErrorException.Field("value", "The text contains a blocked word.");
            RemoteStringOverride? draft = await _Database.AppContent.ReadStringDraftAsync(entry.Key, token).ConfigureAwait(false);
            RemoteStringOverride? current = await _Database.AppContent.ReadStringCurrentAsync(entry.Key, token).ConfigureAwait(false);
            bool hasOverride = (draft != null && draft.Value != null) || (current != null && current.Value != null);
            if (!hasOverride && await _Database.AppContent.CountStringOverridesAsync(token).ConfigureAwait(false) >= _Settings.AppConfig.MaxStringOverrides)
                throw ApiErrorException.Field("value", "At most " + _Settings.AppConfig.MaxStringOverrides + " string overrides (appConfig.maxStringOverrides).");
            await _Metadata.ValidateAsync(actor, request.Labels, request.Tags, true, token, TargetTypeEnum.RemoteString).ConfigureAwait(false);
            DateTime now = DateTime.UtcNow;
            RemoteStringOverride next = new RemoteStringOverride
            {
                RevisionId = IdGenerator.Generate("rsv_"),
                Id = draft?.Id ?? current?.Id ?? IdGenerator.Generate("rst_"),
                Key = entry.Key,
                Value = value,
                Notes = notes,
                State = current != null && current.Value != null ? FeatureFlagStateEnum.Published : FeatureFlagStateEnum.Draft,
                EditedByUserId = info.Context?.UserId,
                CreatedUtc = draft?.CreatedUtc ?? current?.CreatedUtc ?? now,
                UpdatedUtc = now
            };
            await _Database.AppContent.SaveStringDraftAsync(next, token).ConfigureAwait(false);
            await _Metadata.ApplyAsync(actor, TargetTypeEnum.RemoteString, entry.Key, request.Labels, request.Tags, token).ConfigureAwait(false);
            await _Audit.WriteAsync(info, "appConfig.string.update", TargetTypeEnum.RemoteString.ToString(), entry.Key, entry.Key, token).ConfigureAwait(false);
            return await StringViewAsync(entry, token).ConfigureAwait(false);
        }

        /// <summary>
        /// DELETE /api/v1/admin/app/strings/{key}: discard the pending draft, or (when only a published override exists) record a
        /// draft that removes it at the next publish. Audited appConfig.string.delete.
        /// </summary>
        /// <param name="key">Catalog key.</param>
        /// <param name="info">Request information.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The key with its state.</returns>
        /// <exception cref="ApiErrorException">404 NotFound (unknown key or no override).</exception>
        public async Task<RemoteStringView> DeleteStringAsync(string key, RequestInfo info, CancellationToken token = default)
        {
            StringCatalogEntry entry = await RequireEntryAsync(key, false, token).ConfigureAwait(false);
            RemoteStringOverride? current = await _Database.AppContent.ReadStringCurrentAsync(entry.Key, token).ConfigureAwait(false);
            bool removed = await _Database.AppContent.DeleteStringDraftAsync(entry.Key, token).ConfigureAwait(false);
            if (!removed)
            {
                if (current == null || current.Value == null) throw ApiErrorException.NotFound("String override");
                DateTime now = DateTime.UtcNow;
                await _Database.AppContent.SaveStringDraftAsync(new RemoteStringOverride
                {
                    RevisionId = IdGenerator.Generate("rsv_"),
                    Id = current.Id,
                    Key = entry.Key,
                    Value = null,
                    Notes = current.Notes,
                    State = FeatureFlagStateEnum.Archived,
                    EditedByUserId = info.Context?.UserId,
                    CreatedUtc = current.CreatedUtc,
                    UpdatedUtc = now
                }, token).ConfigureAwait(false);
            }
            await _Audit.WriteAsync(info, "appConfig.string.delete", TargetTypeEnum.RemoteString.ToString(), entry.Key, entry.Key + (removed ? " draft discarded" : " removal drafted"), token).ConfigureAwait(false);
            return await StringViewAsync(entry, token).ConfigureAwait(false);
        }

        #endregion

        #region Banners

        /// <summary>
        /// POST /api/v1/admin/app/banners/enumerate.
        /// </summary>
        /// <param name="query">Query.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Page of banners (the draft when one is pending).</returns>
        /// <exception cref="ApiErrorException">400 ValidationFailed.</exception>
        public async Task<EnumerationResult<BannerView>> EnumerateBannersAsync(EnumerationQuery? query, CancellationToken token = default)
        {
            EnumerationRequest request = _Enumeration.Prepare(query, LiveUpdateCollections.Banners, true);
            Dictionary<string, string> errors = new Dictionary<string, string>();
            BannerFilter filter = new BannerFilter
            {
                Active = request.Query.FilterBool("active", errors),
                Placement = request.Query.FilterString("placement"),
                Style = request.Query.FilterString("style"),
                State = request.Query.FilterEnum<FeatureFlagStateEnum>("state", errors),
                HasDraftChanges = request.Query.FilterBool("hasDraftChanges", errors),
                NowUtc = DateTime.UtcNow
            };
            if (filter.Placement != null && !Placements.Contains(filter.Placement)) errors["filters.placement"] = "Use " + String.Join(", ", Placements) + ".";
            if (filter.Style != null && !Styles.Contains(filter.Style)) errors["filters.style"] = "Use " + String.Join(", ", Styles) + ".";
            if (errors.Count > 0) throw ApiErrorException.Validation(errors);
            EnumerationResult<AppBanner> page = await _Database.AppContent.EnumerateBannersAsync(request, filter, token).ConfigureAwait(false);
            Dictionary<string, ObjectMetadata> metadata = await _Metadata.LoadAsync(TargetTypeEnum.Banner, page.Objects.Select(b => b.Id), true, token).ConfigureAwait(false);
            return page.With(page.Objects.Select(b => BannerViewOf(b, metadata.TryGetValue(b.Id, out ObjectMetadata? m) ? m : new ObjectMetadata(), b.IsDraft)).ToList());
        }

        /// <summary>
        /// POST /api/v1/admin/app/banners: create a draft. Audited appConfig.banner.create.
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="request">Body.</param>
        /// <param name="info">Request information.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The banner.</returns>
        /// <exception cref="ApiErrorException">400 ValidationFailed or InvalidTargetingRule.</exception>
        public async Task<BannerView> CreateBannerAsync(ContentActor actor, BannerRequest request, RequestInfo info, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            ArgumentNullException.ThrowIfNull(request);
            if (await _Database.AppContent.CountBannersAsync(token).ConfigureAwait(false) >= _Settings.AppConfig.MaxBanners) throw ApiErrorException.Field("title", "At most " + _Settings.AppConfig.MaxBanners + " banners (appConfig.maxBanners).");
            DateTime now = DateTime.UtcNow;
            AppBanner banner = new AppBanner { Id = IdGenerator.Generate("ban_"), Clients = new List<string> { "mobile", "web" }, CreatedUtc = now, State = FeatureFlagStateEnum.Draft };
            await ApplyBannerAsync(banner, request, BannerFields(), null, token).ConfigureAwait(false);
            await _Metadata.ValidateAsync(actor, request.Labels, request.Tags, true, token, TargetTypeEnum.Banner).ConfigureAwait(false);
            banner.RevisionId = IdGenerator.Generate("bnv_");
            banner.EditedByUserId = info.Context?.UserId;
            banner.UpdatedUtc = now;
            await _Database.AppContent.SaveBannerDraftAsync(banner, token).ConfigureAwait(false);
            ObjectMetadata metadata = await _Metadata.ApplyAsync(actor, TargetTypeEnum.Banner, banner.Id, request.Labels, request.Tags, token).ConfigureAwait(false);
            await _Audit.WriteAsync(info, "appConfig.banner.create", TargetTypeEnum.Banner.ToString(), banner.Id, banner.Title, token).ConfigureAwait(false);
            return BannerViewOf(banner, metadata, true);
        }

        /// <summary>
        /// GET /api/v1/admin/app/banners/{id}.
        /// </summary>
        /// <param name="id">Banner id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Draft or published banner, the published banner, and history.</returns>
        /// <exception cref="ApiErrorException">404 NotFound.</exception>
        public async Task<BannerDetailView> GetBannerAsync(string id, CancellationToken token = default)
        {
            AppBanner? draft = await _Database.AppContent.ReadBannerDraftAsync(id, token).ConfigureAwait(false);
            AppBanner? current = await _Database.AppContent.ReadBannerCurrentAsync(id, token).ConfigureAwait(false);
            AppBanner head = draft ?? current ?? throw ApiErrorException.NotFound("Banner");
            ObjectMetadata metadata = await _Metadata.LoadOneAsync(TargetTypeEnum.Banner, id, true, token).ConfigureAwait(false);
            EnumerationRequest request = _Enumeration.Prepare(new EnumerationQuery { MaxResults = 10 }, LiveUpdateCollections.BannerHistory, true, id);
            EnumerationResult<AppBanner> history = await _Database.AppContent.EnumerateBannerHistoryAsync(request, id, token).ConfigureAwait(false);
            return new BannerDetailView
            {
                Banner = BannerViewOf(head, metadata, draft != null),
                Published = current == null ? null : BannerViewOf(current, metadata, draft != null),
                History = new ContentHistoryPreview
                {
                    Objects = history.Objects.Select(h => (object)BannerViewOf(h, metadata, false)).ToList(),
                    TotalRecords = history.TotalRecords ?? history.Objects.Count,
                    Enumerate = _Base + "/banners/" + id + "/history/enumerate"
                }
            };
        }

        /// <summary>
        /// POST /api/v1/admin/app/banners/{id}/history/enumerate.
        /// </summary>
        /// <param name="id">Banner id.</param>
        /// <param name="query">Query.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Published revisions, newest first.</returns>
        /// <exception cref="ApiErrorException">404 NotFound.</exception>
        public async Task<EnumerationResult<BannerView>> EnumerateBannerHistoryAsync(string id, EnumerationQuery? query, CancellationToken token = default)
        {
            await RequireBannerAsync(id, token).ConfigureAwait(false);
            EnumerationRequest request = _Enumeration.Prepare(query, LiveUpdateCollections.BannerHistory, true, id);
            EnumerationResult<AppBanner> page = await _Database.AppContent.EnumerateBannerHistoryAsync(request, id, token).ConfigureAwait(false);
            ObjectMetadata metadata = await _Metadata.LoadOneAsync(TargetTypeEnum.Banner, id, true, token).ConfigureAwait(false);
            return page.With(page.Objects.Select(b => BannerViewOf(b, metadata, false)).ToList());
        }

        /// <summary>
        /// PUT /api/v1/admin/app/banners/{id}: replace the draft (absent fields keep their values; rules replace the whole
        /// list). If-Match must equal the banner's ETag. Audited appConfig.banner.update.
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="id">Banner id.</param>
        /// <param name="request">Body.</param>
        /// <param name="present">Fields present in the body.</param>
        /// <param name="ifMatch">If-Match header.</param>
        /// <param name="info">Request information.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The draft.</returns>
        /// <exception cref="ApiErrorException">400, 404, 412 PreconditionFailed.</exception>
        public async Task<BannerView> UpdateBannerAsync(ContentActor actor, string id, BannerRequest request, HashSet<string> present, string? ifMatch, RequestInfo info, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            ArgumentNullException.ThrowIfNull(present);
            AppBanner head = await RequireBannerAsync(id, token).ConfigureAwait(false);
            RequireMatch(ifMatch, head.RevisionId, "banner");
            AppBanner draft = Copy(head);
            await ApplyBannerAsync(draft, request, present, head, token).ConfigureAwait(false);
            await _Metadata.ValidateAsync(actor, request.Labels, request.Tags, true, token, TargetTypeEnum.Banner).ConfigureAwait(false);
            draft.RevisionId = IdGenerator.Generate("bnv_");
            draft.EditedByUserId = info.Context?.UserId;
            draft.UpdatedUtc = DateTime.UtcNow;
            await _Database.AppContent.SaveBannerDraftAsync(draft, token).ConfigureAwait(false);
            ObjectMetadata metadata = await _Metadata.ApplyAsync(actor, TargetTypeEnum.Banner, id, present.Contains("labels") ? request.Labels : null, present.Contains("tags") ? request.Tags : null, token).ConfigureAwait(false);
            await _Audit.WriteAsync(info, "appConfig.banner.update", TargetTypeEnum.Banner.ToString(), id, draft.Title, token).ConfigureAwait(false);
            return BannerViewOf(draft, metadata, true);
        }

        /// <summary>
        /// POST /api/v1/admin/app/banners/{id}/archive or /restore: a draft change of the state.
        /// </summary>
        /// <param name="id">Banner id.</param>
        /// <param name="archive">Archive (true) or restore (false).</param>
        /// <param name="info">Request information.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The draft.</returns>
        /// <exception cref="ApiErrorException">404, 409 Conflict (never published).</exception>
        public async Task<BannerView> ArchiveBannerAsync(string id, bool archive, RequestInfo info, CancellationToken token = default)
        {
            AppBanner head = await RequireBannerAsync(id, token).ConfigureAwait(false);
            if (!await _Database.AppContent.HasPublishedBannerAsync(id, token).ConfigureAwait(false)) throw new ApiErrorException(409, "Conflict", "Delete a banner that was never published instead of archiving it.");
            AppBanner draft = Copy(head);
            draft.State = archive ? FeatureFlagStateEnum.Archived : FeatureFlagStateEnum.Published;
            draft.RevisionId = IdGenerator.Generate("bnv_");
            draft.EditedByUserId = info.Context?.UserId;
            draft.UpdatedUtc = DateTime.UtcNow;
            await _Database.AppContent.SaveBannerDraftAsync(draft, token).ConfigureAwait(false);
            await _Audit.WriteAsync(info, archive ? "appConfig.banner.archive" : "appConfig.banner.restore", TargetTypeEnum.Banner.ToString(), id, draft.Title, token).ConfigureAwait(false);
            return BannerViewOf(draft, await _Metadata.LoadOneAsync(TargetTypeEnum.Banner, id, true, token).ConfigureAwait(false), true);
        }

        /// <summary>
        /// DELETE /api/v1/admin/app/banners/{id}: only banners that were never published. Audited appConfig.banner.delete.
        /// </summary>
        /// <param name="id">Banner id.</param>
        /// <param name="info">Request information.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        /// <exception cref="ApiErrorException">404, 409 Conflict.</exception>
        public async Task DeleteBannerAsync(string id, RequestInfo info, CancellationToken token = default)
        {
            AppBanner head = await RequireBannerAsync(id, token).ConfigureAwait(false);
            if (await _Database.AppContent.HasPublishedBannerAsync(id, token).ConfigureAwait(false)) throw new ApiErrorException(409, "Conflict", "Published banners are archived, not deleted.");
            await _Database.AppContent.DeleteBannerDraftAsync(id, true, token).ConfigureAwait(false);
            await _Audit.WriteAsync(info, "appConfig.banner.delete", TargetTypeEnum.Banner.ToString(), id, head.Title, token).ConfigureAwait(false);
        }

        #endregion

        #region Assets

        /// <summary>
        /// POST /api/v1/admin/app/assets/enumerate.
        /// </summary>
        /// <param name="query">Query.</param>
        /// <param name="origin">Request origin (local asset URLs).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Page of assets (the draft when one is pending).</returns>
        /// <exception cref="ApiErrorException">400 ValidationFailed.</exception>
        public async Task<EnumerationResult<RemoteAssetView>> EnumerateAssetsAsync(EnumerationQuery? query, string origin, CancellationToken token = default)
        {
            EnumerationRequest request = _Enumeration.Prepare(query, LiveUpdateCollections.Assets, true);
            Dictionary<string, string> errors = new Dictionary<string, string>();
            RemoteAssetFilter filter = new RemoteAssetFilter
            {
                State = request.Query.FilterEnum<FeatureFlagStateEnum>("state", errors),
                HasDraftChanges = request.Query.FilterBool("hasDraftChanges", errors)
            };
            if (errors.Count > 0) throw ApiErrorException.Validation(errors);
            EnumerationResult<RemoteAsset> page = await _Database.AppContent.EnumerateAssetsAsync(request, filter, token).ConfigureAwait(false);
            Dictionary<string, ObjectMetadata> metadata = await _Metadata.LoadAsync(TargetTypeEnum.RemoteAsset, page.Objects.Select(a => a.Id), true, token).ConfigureAwait(false);
            return page.With(page.Objects.Select(a => AssetViewOf(a, metadata.TryGetValue(a.Id, out ObjectMetadata? m) ? m : new ObjectMetadata(), a.IsDraft, origin)).ToList());
        }

        /// <summary>
        /// POST /api/v1/admin/app/assets: create a draft (content is uploaded next). Audited appConfig.asset.create.
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="request">Body.</param>
        /// <param name="origin">Request origin.</param>
        /// <param name="info">Request information.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The asset.</returns>
        /// <exception cref="ApiErrorException">400 ValidationFailed, 409 Conflict (key in use).</exception>
        public async Task<RemoteAssetView> CreateAssetAsync(ContentActor actor, RemoteAssetRequest request, string origin, RequestInfo info, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            ArgumentNullException.ThrowIfNull(request);
            if (await _Database.AppContent.CountAssetsAsync(token).ConfigureAwait(false) >= _Settings.AppConfig.MaxRemoteAssets) throw ApiErrorException.Field("key", "At most " + _Settings.AppConfig.MaxRemoteAssets + " remote assets (appConfig.maxRemoteAssets).");
            DateTime now = DateTime.UtcNow;
            RemoteAsset asset = new RemoteAsset { Id = IdGenerator.Generate("ras_"), CreatedUtc = now, State = FeatureFlagStateEnum.Draft };
            await ApplyAssetAsync(asset, request, new HashSet<string>(StringComparer.Ordinal) { "key", "altText" }, token).ConfigureAwait(false);
            await _Metadata.ValidateAsync(actor, request.Labels, request.Tags, true, token, TargetTypeEnum.RemoteAsset).ConfigureAwait(false);
            asset.RevisionId = IdGenerator.Generate("rav_");
            asset.EditedByUserId = info.Context?.UserId;
            asset.UpdatedUtc = now;
            await _Database.AppContent.SaveAssetDraftAsync(asset, token).ConfigureAwait(false);
            ObjectMetadata metadata = await _Metadata.ApplyAsync(actor, TargetTypeEnum.RemoteAsset, asset.Id, request.Labels, request.Tags, token).ConfigureAwait(false);
            await _Audit.WriteAsync(info, "appConfig.asset.create", TargetTypeEnum.RemoteAsset.ToString(), asset.Id, asset.Key, token).ConfigureAwait(false);
            return AssetViewOf(asset, metadata, true, origin);
        }

        /// <summary>
        /// GET /api/v1/admin/app/assets/{id}.
        /// </summary>
        /// <param name="id">Asset id.</param>
        /// <param name="origin">Request origin.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Draft or published asset, the published asset, and history.</returns>
        /// <exception cref="ApiErrorException">404 NotFound.</exception>
        public async Task<RemoteAssetDetailView> GetAssetAsync(string id, string origin, CancellationToken token = default)
        {
            RemoteAsset? draft = await _Database.AppContent.ReadAssetDraftAsync(id, token).ConfigureAwait(false);
            RemoteAsset? current = await _Database.AppContent.ReadAssetCurrentAsync(id, token).ConfigureAwait(false);
            RemoteAsset head = draft ?? current ?? throw ApiErrorException.NotFound("Remote asset");
            ObjectMetadata metadata = await _Metadata.LoadOneAsync(TargetTypeEnum.RemoteAsset, id, true, token).ConfigureAwait(false);
            EnumerationRequest request = _Enumeration.Prepare(new EnumerationQuery { MaxResults = 10 }, LiveUpdateCollections.AssetHistory, true, id);
            EnumerationResult<RemoteAsset> history = await _Database.AppContent.EnumerateAssetHistoryAsync(request, id, token).ConfigureAwait(false);
            return new RemoteAssetDetailView
            {
                Asset = AssetViewOf(head, metadata, draft != null, origin),
                Published = current == null ? null : AssetViewOf(current, metadata, draft != null, origin),
                History = new ContentHistoryPreview
                {
                    Objects = history.Objects.Select(h => (object)AssetViewOf(h, metadata, false, origin)).ToList(),
                    TotalRecords = history.TotalRecords ?? history.Objects.Count,
                    Enumerate = _Base + "/assets/" + id + "/history/enumerate"
                }
            };
        }

        /// <summary>
        /// POST /api/v1/admin/app/assets/{id}/history/enumerate.
        /// </summary>
        /// <param name="id">Asset id.</param>
        /// <param name="query">Query.</param>
        /// <param name="origin">Request origin.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Published revisions, newest first.</returns>
        /// <exception cref="ApiErrorException">404 NotFound.</exception>
        public async Task<EnumerationResult<RemoteAssetView>> EnumerateAssetHistoryAsync(string id, EnumerationQuery? query, string origin, CancellationToken token = default)
        {
            await RequireAssetAsync(id, token).ConfigureAwait(false);
            EnumerationRequest request = _Enumeration.Prepare(query, LiveUpdateCollections.AssetHistory, true, id);
            EnumerationResult<RemoteAsset> page = await _Database.AppContent.EnumerateAssetHistoryAsync(request, id, token).ConfigureAwait(false);
            ObjectMetadata metadata = await _Metadata.LoadOneAsync(TargetTypeEnum.RemoteAsset, id, true, token).ConfigureAwait(false);
            return page.With(page.Objects.Select(a => AssetViewOf(a, metadata, false, origin)).ToList());
        }

        /// <summary>
        /// PUT /api/v1/admin/app/assets/{id}: change the key or alt text (If-Match). Audited appConfig.asset.update.
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="id">Asset id.</param>
        /// <param name="request">Body.</param>
        /// <param name="present">Fields present in the body.</param>
        /// <param name="ifMatch">If-Match header.</param>
        /// <param name="origin">Request origin.</param>
        /// <param name="info">Request information.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The draft.</returns>
        /// <exception cref="ApiErrorException">400, 404, 409 Conflict, 412 PreconditionFailed.</exception>
        public async Task<RemoteAssetView> UpdateAssetAsync(ContentActor actor, string id, RemoteAssetRequest request, HashSet<string> present, string? ifMatch, string origin, RequestInfo info, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            ArgumentNullException.ThrowIfNull(present);
            RemoteAsset head = await RequireAssetAsync(id, token).ConfigureAwait(false);
            RequireMatch(ifMatch, head.RevisionId, "asset");
            RemoteAsset draft = Copy(head);
            bool published = await _Database.AppContent.HasPublishedAssetAsync(id, token).ConfigureAwait(false);
            await ApplyAssetAsync(draft, request, present, token).ConfigureAwait(false);
            if (published && !String.Equals(draft.Key, head.Key, StringComparison.Ordinal)) throw ApiErrorException.Field("key", "The key cannot change after the first publish.");
            await _Metadata.ValidateAsync(actor, request.Labels, request.Tags, true, token, TargetTypeEnum.RemoteAsset).ConfigureAwait(false);
            draft.RevisionId = IdGenerator.Generate("rav_");
            draft.EditedByUserId = info.Context?.UserId;
            draft.UpdatedUtc = DateTime.UtcNow;
            await _Database.AppContent.SaveAssetDraftAsync(draft, token).ConfigureAwait(false);
            ObjectMetadata metadata = await _Metadata.ApplyAsync(actor, TargetTypeEnum.RemoteAsset, id, present.Contains("labels") ? request.Labels : null, present.Contains("tags") ? request.Tags : null, token).ConfigureAwait(false);
            await _Audit.WriteAsync(info, "appConfig.asset.update", TargetTypeEnum.RemoteAsset.ToString(), id, draft.Key, token).ConfigureAwait(false);
            return AssetViewOf(draft, metadata, true, origin);
        }

        /// <summary>
        /// PUT /api/v1/admin/app/assets/{id}/content or /dark-content: decode the image, re-encode it to WebP without
        /// metadata, store it content addressed under app-content/{propertyId}/{sha256}, and record it on the draft.
        /// Audited appConfig.asset.content.
        /// </summary>
        /// <param name="id">Asset id.</param>
        /// <param name="body">Raw body.</param>
        /// <param name="contentType">Content-Type header.</param>
        /// <param name="contentLength">Content-Length, or 0 when unknown.</param>
        /// <param name="dark">Whether this is the dark variant.</param>
        /// <param name="origin">Request origin.</param>
        /// <param name="info">Request information.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The draft.</returns>
        /// <exception cref="ApiErrorException">400 ValidationFailed, 404 NotFound, 413 TooLarge, 503 ImagesUnavailable.</exception>
        public async Task<RemoteAssetView> UploadAssetContentAsync(string id, Stream body, string? contentType, long contentLength, bool dark, string origin, RequestInfo info, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(body);
            RemoteAsset head = await RequireAssetAsync(id, token).ConfigureAwait(false);
            string type = (contentType ?? String.Empty).Split(';')[0].Trim().ToLowerInvariant();
            if (!UploadTypes.Contains(type)) throw ApiErrorException.Field("contentType", "Send image/png, image/jpeg, or image/webp.");
            long max = (long)_Settings.AppConfig.MaxAssetMegabytes * 1024 * 1024;
            if (contentLength > max) throw new ApiErrorException(413, "TooLarge", "The image is larger than " + _Settings.AppConfig.MaxAssetMegabytes + " MB (appConfig.maxAssetMegabytes).");
            byte[] bytes = await ReadBoundedAsync(body, max, token).ConfigureAwait(false);
            if (bytes.Length == 0) throw ApiErrorException.Field("body", "Send the image bytes.");
            if (_Images == null) throw new ApiErrorException(503, "ImagesUnavailable", "Image processing is not available.");
            EncodedImage encoded;
            int width;
            int height;
            try
            {
                using (IDecodedImage decoded = _Images.Decode(bytes, _Settings.Media.MaxImagePixels))
                {
                    if (Math.Max(decoded.Width, decoded.Height) > _MaxLongEdge)
                    {
                        using (IDecodedImage resized = _Images.Resize(decoded, _MaxLongEdge))
                        {
                            encoded = _Images.EncodeWebp(resized, 90);
                            width = resized.Width;
                            height = resized.Height;
                        }
                    }
                    else
                    {
                        encoded = _Images.EncodeWebp(decoded, 90);
                        width = decoded.Width;
                        height = decoded.Height;
                    }
                }
            }
            catch (MediaProcessingException e)
            {
                throw ApiErrorException.Field("body", "The image could not be read (" + e.Message + ").");
            }
            string sha = Convert.ToHexString(SHA256.HashData(encoded.Data)).ToLowerInvariant();
            string key = ContentAssetKey(_PropertyId, sha);
            // Every stored content image is queued for garbage collection; UpdateAssetGcWorker deletes it only once no
            // remote asset revision references it and updates.assetGcRetentionDays passed since the last upload.
            await _Database.LiveUpdates.QueueGcObjectAsync(new UpdateGcObject { Kind = UpdateGcObject.ContentAssetKind, Sha256Hex = sha, StorageKey = key, SizeBytes = encoded.Data.LongLength }, DateTime.UtcNow, token).ConfigureAwait(false);
            if (await _Store.HeadAsync(key, token).ConfigureAwait(false) == null)
            {
                string temp = Path.Combine(Path.GetTempPath(), "tk-app-content-" + Guid.NewGuid().ToString("N"));
                try
                {
                    await File.WriteAllBytesAsync(temp, encoded.Data, token).ConfigureAwait(false);
                    await _Store.PutFileAsync(key, temp, "image/webp", token).ConfigureAwait(false);
                }
                finally
                {
                    try
                    {
                        File.Delete(temp);
                    }
                    catch (IOException)
                    {
                        // A temporary file left behind is removed by the operating system.
                    }
                }
            }
            RemoteAsset draft = Copy(head);
            if (dark)
            {
                draft.DarkSha256Hex = sha;
                draft.DarkWidth = width;
                draft.DarkHeight = height;
                draft.DarkSizeBytes = encoded.Data.Length;
            }
            else
            {
                draft.Sha256Hex = sha;
                draft.Width = width;
                draft.Height = height;
                draft.SizeBytes = encoded.Data.Length;
                draft.ContentType = "image/webp";
            }
            draft.RevisionId = IdGenerator.Generate("rav_");
            draft.EditedByUserId = info.Context?.UserId;
            draft.UpdatedUtc = DateTime.UtcNow;
            await _Database.AppContent.SaveAssetDraftAsync(draft, token).ConfigureAwait(false);
            await _Audit.WriteAsync(info, "appConfig.asset.content", TargetTypeEnum.RemoteAsset.ToString(), id, draft.Key + (dark ? " dark variant" : String.Empty) + " " + sha.Substring(0, 12), token).ConfigureAwait(false);
            return AssetViewOf(draft, await _Metadata.LoadOneAsync(TargetTypeEnum.RemoteAsset, id, true, token).ConfigureAwait(false), true, origin);
        }

        /// <summary>
        /// POST /api/v1/admin/app/assets/{id}/archive or /restore.
        /// </summary>
        /// <param name="id">Asset id.</param>
        /// <param name="archive">Archive (true) or restore (false).</param>
        /// <param name="origin">Request origin.</param>
        /// <param name="info">Request information.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The draft.</returns>
        /// <exception cref="ApiErrorException">404, 409 Conflict.</exception>
        public async Task<RemoteAssetView> ArchiveAssetAsync(string id, bool archive, string origin, RequestInfo info, CancellationToken token = default)
        {
            RemoteAsset head = await RequireAssetAsync(id, token).ConfigureAwait(false);
            if (!await _Database.AppContent.HasPublishedAssetAsync(id, token).ConfigureAwait(false)) throw new ApiErrorException(409, "Conflict", "Delete an asset that was never published instead of archiving it.");
            RemoteAsset draft = Copy(head);
            draft.State = archive ? FeatureFlagStateEnum.Archived : FeatureFlagStateEnum.Published;
            draft.RevisionId = IdGenerator.Generate("rav_");
            draft.EditedByUserId = info.Context?.UserId;
            draft.UpdatedUtc = DateTime.UtcNow;
            await _Database.AppContent.SaveAssetDraftAsync(draft, token).ConfigureAwait(false);
            await _Audit.WriteAsync(info, archive ? "appConfig.asset.archive" : "appConfig.asset.restore", TargetTypeEnum.RemoteAsset.ToString(), id, draft.Key, token).ConfigureAwait(false);
            return AssetViewOf(draft, await _Metadata.LoadOneAsync(TargetTypeEnum.RemoteAsset, id, true, token).ConfigureAwait(false), true, origin);
        }

        /// <summary>
        /// DELETE /api/v1/admin/app/assets/{id}: only assets that were never published. Audited appConfig.asset.delete.
        /// </summary>
        /// <param name="id">Asset id.</param>
        /// <param name="info">Request information.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        /// <exception cref="ApiErrorException">404, 409 Conflict.</exception>
        public async Task DeleteAssetAsync(string id, RequestInfo info, CancellationToken token = default)
        {
            RemoteAsset head = await RequireAssetAsync(id, token).ConfigureAwait(false);
            if (await _Database.AppContent.HasPublishedAssetAsync(id, token).ConfigureAwait(false)) throw new ApiErrorException(409, "Conflict", "Published assets are archived, not deleted.");
            await _Database.AppContent.DeleteAssetDraftAsync(id, true, token).ConfigureAwait(false);
            await _Audit.WriteAsync(info, "appConfig.asset.delete", TargetTypeEnum.RemoteAsset.ToString(), id, head.Key, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Read a content asset for local delivery (GET /api/v1/app/content-assets/{propertyId}/{sha256Hex}): only hashes a
        /// remote asset revision references are served.
        /// </summary>
        /// <param name="propertyId">Property id from the path.</param>
        /// <param name="sha256Hex">SHA-256 hex.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The bytes, or null when unknown.</returns>
        public async Task<byte[]?> ReadContentAssetAsync(string propertyId, string sha256Hex, CancellationToken token = default)
        {
            if (!String.Equals(propertyId, _PropertyId, StringComparison.Ordinal) || !LiveUpdateRules.Sha256HexPattern.IsMatch(sha256Hex ?? String.Empty)) return null;
            if (!await _Database.AppContent.IsContentHashReferencedAsync(sha256Hex!, token).ConfigureAwait(false)) return null;
            string key = ContentAssetKey(_PropertyId, sha256Hex!);
            ObjectHead? head = await _Store.HeadAsync(key, token).ConfigureAwait(false);
            if (head == null) return null;
            return head.SizeBytes == 0 ? Array.Empty<byte>() : await _Store.ReadRangeAsync(key, 0, (int)Math.Min(head.SizeBytes, Int32.MaxValue), token).ConfigureAwait(false);
        }

        /// <summary>
        /// Absolute URL of a content asset.
        /// </summary>
        /// <param name="origin">Request origin for local delivery.</param>
        /// <param name="sha256Hex">SHA-256 hex.</param>
        /// <returns>URL.</returns>
        public string ContentAssetUrl(string origin, string sha256Hex)
        {
            string configured = (_Settings.Updates.ContentAssetBaseUrl ?? String.Empty).Trim().TrimEnd('/');
            if (configured.Length > 0) return configured + "/" + _PropertyId + "/" + sha256Hex;
            return (origin ?? String.Empty).TrimEnd('/') + ContentAssetPath + "/" + _PropertyId + "/" + sha256Hex;
        }

        #endregion

        #region Publishing

        /// <summary>
        /// Pending drafts per content type.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Counts.</returns>
        public Task<ContentDraftCounts> CountDraftsAsync(CancellationToken token = default)
        {
            return _Database.AppContent.CountDraftsAsync(token);
        }

        /// <summary>
        /// Refuse a publish that would put an asset without content live.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        /// <exception cref="ApiErrorException">400 ValidationFailed (fieldErrors assets.{id}).</exception>
        public async Task ValidatePublishAsync(CancellationToken token = default)
        {
            List<RemoteAsset> drafts = await _Database.AppContent.ListAssetDraftsAsync(_Settings.AppConfig.MaxRemoteAssets + 100, token).ConfigureAwait(false);
            Dictionary<string, string> errors = new Dictionary<string, string>();
            foreach (RemoteAsset a in drafts.Where(a => a.State != FeatureFlagStateEnum.Archived && a.Sha256Hex == null)) errors["assets." + a.Id] = "Upload the image of " + a.Key + " before publishing.";
            if (errors.Count > 0) throw ApiErrorException.Validation(errors, "An asset draft has no content.");
        }

        /// <summary>
        /// Write drafts that make strings, banners, and assets equal their state at a version (restore). Returns the
        /// number of drafted strings, banners, and assets.
        /// </summary>
        /// <param name="version">Target version.</param>
        /// <param name="userId">Editor, or null.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Counts.</returns>
        public async Task<ContentDraftCounts> RestoreDraftsAsync(long version, string? userId, CancellationToken token = default)
        {
            ContentDraftCounts counts = new ContentDraftCounts();
            DateTime now = DateTime.UtcNow;
            int limit = _Settings.AppConfig.MaxStringOverrides + 100;
            Dictionary<string, RemoteStringOverride> thenStrings = (await _Database.AppContent.ListStringsAtVersionAsync(version, limit, token).ConfigureAwait(false)).ToDictionary(s => s.Key, StringComparer.Ordinal);
            Dictionary<string, RemoteStringOverride> nowStrings = (await _Database.AppContent.ListCurrentStringsAsync(limit, token).ConfigureAwait(false)).ToDictionary(s => s.Key, StringComparer.Ordinal);
            foreach (string key in thenStrings.Keys.Union(nowStrings.Keys, StringComparer.Ordinal))
            {
                thenStrings.TryGetValue(key, out RemoteStringOverride? old);
                nowStrings.TryGetValue(key, out RemoteStringOverride? cur);
                if (old != null && cur != null && old.RevisionId == cur.RevisionId) continue;
                RemoteStringOverride source = old ?? cur!;
                await _Database.AppContent.SaveStringDraftAsync(new RemoteStringOverride
                {
                    RevisionId = IdGenerator.Generate("rsv_"),
                    Id = cur?.Id ?? source.Id,
                    Key = key,
                    Value = old?.Value,
                    Notes = source.Notes,
                    State = old == null ? FeatureFlagStateEnum.Archived : FeatureFlagStateEnum.Published,
                    EditedByUserId = userId,
                    CreatedUtc = source.CreatedUtc,
                    UpdatedUtc = now
                }, token).ConfigureAwait(false);
                counts.Strings++;
            }
            int bannerLimit = _Settings.AppConfig.MaxBanners + 1000;
            Dictionary<string, AppBanner> thenBanners = (await _Database.AppContent.ListBannersAtVersionAsync(version, bannerLimit, token).ConfigureAwait(false)).ToDictionary(b => b.Id, StringComparer.Ordinal);
            Dictionary<string, AppBanner> nowBanners = (await _Database.AppContent.ListCurrentBannersAsync(bannerLimit, token).ConfigureAwait(false)).ToDictionary(b => b.Id, StringComparer.Ordinal);
            foreach (string id in thenBanners.Keys.Union(nowBanners.Keys, StringComparer.Ordinal))
            {
                thenBanners.TryGetValue(id, out AppBanner? old);
                nowBanners.TryGetValue(id, out AppBanner? cur);
                if (old != null && cur != null && old.RevisionId == cur.RevisionId) continue;
                if (old == null && cur!.State == FeatureFlagStateEnum.Archived) continue;
                AppBanner draft = Copy(old ?? cur!);
                draft.State = old == null ? FeatureFlagStateEnum.Archived : old.State == FeatureFlagStateEnum.Draft ? FeatureFlagStateEnum.Published : old.State;
                draft.RevisionId = IdGenerator.Generate("bnv_");
                draft.EditedByUserId = userId;
                draft.UpdatedUtc = now;
                await _Database.AppContent.SaveBannerDraftAsync(draft, token).ConfigureAwait(false);
                counts.Banners++;
            }
            int assetLimit = _Settings.AppConfig.MaxRemoteAssets + 1000;
            Dictionary<string, RemoteAsset> thenAssets = (await _Database.AppContent.ListAssetsAtVersionAsync(version, assetLimit, token).ConfigureAwait(false)).ToDictionary(a => a.Id, StringComparer.Ordinal);
            Dictionary<string, RemoteAsset> nowAssets = (await _Database.AppContent.ListCurrentAssetsAsync(assetLimit, token).ConfigureAwait(false)).ToDictionary(a => a.Id, StringComparer.Ordinal);
            foreach (string id in thenAssets.Keys.Union(nowAssets.Keys, StringComparer.Ordinal))
            {
                thenAssets.TryGetValue(id, out RemoteAsset? old);
                nowAssets.TryGetValue(id, out RemoteAsset? cur);
                if (old != null && cur != null && old.RevisionId == cur.RevisionId) continue;
                if (old == null && cur!.State == FeatureFlagStateEnum.Archived) continue;
                RemoteAsset draft = Copy(old ?? cur!);
                draft.State = old == null ? FeatureFlagStateEnum.Archived : old.State == FeatureFlagStateEnum.Draft ? FeatureFlagStateEnum.Published : old.State;
                draft.RevisionId = IdGenerator.Generate("rav_");
                draft.EditedByUserId = userId;
                draft.UpdatedUtc = now;
                await _Database.AppContent.SaveAssetDraftAsync(draft, token).ConfigureAwait(false);
                counts.Assets++;
            }
            return counts;
        }

        /// <summary>
        /// Discard content drafts: every draft when ids is null, else the named ones (ban_, ras_, rst_ ids or string keys;
        /// other ids are ignored). Never-published banners and assets lose their metadata.
        /// </summary>
        /// <param name="ids">Ids, or null.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Drafts discarded.</returns>
        public async Task<int> DiscardAsync(List<string>? ids, CancellationToken token = default)
        {
            List<RemoteStringOverride> stringDrafts = await _Database.AppContent.ListStringDraftsAsync(_Settings.AppConfig.MaxStringOverrides + 100, token).ConfigureAwait(false);
            int discarded = 0;
            List<string> bannerIds;
            List<string> assetIds;
            List<string> keys;
            if (ids == null)
            {
                keys = stringDrafts.Select(s => s.Key).ToList();
                bannerIds = (await _Database.AppContent.ListBannerDraftsAsync(_Settings.AppConfig.MaxBanners + 1000, token).ConfigureAwait(false)).Select(b => b.Id).ToList();
                assetIds = (await _Database.AppContent.ListAssetDraftsAsync(_Settings.AppConfig.MaxRemoteAssets + 1000, token).ConfigureAwait(false)).Select(a => a.Id).ToList();
            }
            else
            {
                keys = stringDrafts.Where(s => ids.Contains(s.Id, StringComparer.Ordinal) || ids.Contains(s.Key, StringComparer.Ordinal)).Select(s => s.Key).ToList();
                bannerIds = ids.Where(i => i.StartsWith("ban_", StringComparison.Ordinal)).ToList();
                assetIds = ids.Where(i => i.StartsWith("ras_", StringComparison.Ordinal)).ToList();
            }
            foreach (string key in keys.Distinct(StringComparer.Ordinal))
                if (await _Database.AppContent.DeleteStringDraftAsync(key, token).ConfigureAwait(false)) discarded++;
            foreach (string id in bannerIds.Distinct(StringComparer.Ordinal))
            {
                bool published = await _Database.AppContent.HasPublishedBannerAsync(id, token).ConfigureAwait(false);
                if (await _Database.AppContent.DeleteBannerDraftAsync(id, !published, token).ConfigureAwait(false)) discarded++;
            }
            foreach (string id in assetIds.Distinct(StringComparer.Ordinal))
            {
                bool published = await _Database.AppContent.HasPublishedAssetAsync(id, token).ConfigureAwait(false);
                if (await _Database.AppContent.DeleteAssetDraftAsync(id, !published, token).ConfigureAwait(false)) discarded++;
            }
            return discarded;
        }

        #endregion

        #region Manifest

        /// <summary>
        /// Fill the strings, banners, and assets parts of an app manifest for one caller (section 11.1): a full snapshot
        /// without since, else the changes after since; fullRefresh (and null parts) when since is older than the
        /// retained change log, ahead of the current version, or when more than appConfig.maxDeltaItems items changed.
        /// </summary>
        /// <param name="view">Manifest (ContentVersion set).</param>
        /// <param name="context">Caller attributes.</param>
        /// <param name="client">web or mobile.</param>
        /// <param name="since">The caller's cached content version, or null.</param>
        /// <param name="origin">Request origin (local asset URLs).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        public async Task FillManifestAsync(AppManifestView view, FlagEvaluationContext context, string client, long? since, string origin, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(view);
            ArgumentNullException.ThrowIfNull(context);
            await EnsureCacheAsync(view.ContentVersion, token).ConfigureAwait(false);
            List<RemoteStringOverride> strings = _Strings.Where(s => VisibleTo(s.Key, client)).ToList();
            List<RemoteAsset> assets = _Assets.Where(a => a.State != FeatureFlagStateEnum.Archived && a.Sha256Hex != null).ToList();
            List<AppBanner> banners = ActiveBanners(context, client);
            int maxDelta = _Settings.AppConfig.MaxDeltaItems;
            List<ContentChange>? changes = null;
            bool fullRefresh = false;
            if (since == null) fullRefresh = strings.Count + assets.Count > maxDelta;
            else if (since.Value > view.ContentVersion) fullRefresh = true;
            else if (since.Value < view.ContentVersion)
            {
                long? oldest = await _Database.AppContent.OldestChangeVersionAsync(token).ConfigureAwait(false);
                if (oldest == null || since.Value + 1 < oldest.Value) fullRefresh = true;
                else
                {
                    changes = await _Database.AppContent.ListChangesSinceAsync(since.Value, maxDelta + 1, token).ConfigureAwait(false);
                    if (changes.Count > maxDelta) fullRefresh = true;
                }
            }
            else changes = new List<ContentChange>();
            if (fullRefresh)
            {
                view.FullRefresh = true;
                view.Strings = null;
                view.Banners = null;
                view.Assets = null;
                return;
            }
            Dictionary<string, string> upserts = new Dictionary<string, string>(StringComparer.Ordinal);
            List<string> deletes = new List<string>();
            List<ClientAssetView> assetUpserts = new List<ClientAssetView>();
            List<string> assetDeletes = new List<string>();
            List<string> bannerDeletes = new List<string>();
            if (changes == null)
            {
                foreach (RemoteStringOverride s in strings) upserts[s.Key] = s.Value!;
                assetUpserts.AddRange(assets.Select(a => ClientAsset(a, origin)));
            }
            else
            {
                foreach (ContentChange change in changes)
                {
                    if (change.ObjectType == "RemoteString" && VisibleTo(change.ObjectId, client))
                    {
                        RemoteStringOverride? s = strings.FirstOrDefault(x => x.Key == change.ObjectId);
                        if (s != null) upserts[s.Key] = s.Value!;
                        else deletes.Add(change.ObjectId);
                    }
                    else if (change.ObjectType == "RemoteAsset")
                    {
                        RemoteAsset? a = assets.FirstOrDefault(x => x.Id == change.ObjectId);
                        if (a != null) assetUpserts.Add(ClientAsset(a, origin));
                        else assetDeletes.Add(change.ObjectId);
                    }
                    else if (change.ObjectType == "Banner" && !banners.Any(b => b.Id == change.ObjectId)) bannerDeletes.Add(change.ObjectId);
                }
            }
            view.Strings = new Dictionary<string, object>(StringComparer.Ordinal) { { "upserts", upserts }, { "deletes", deletes } };
            view.Banners = new Dictionary<string, object>(StringComparer.Ordinal) { { "objects", banners.Select(ClientBanner).ToList() }, { "deletes", bannerDeletes } };
            view.Assets = new Dictionary<string, object>(StringComparer.Ordinal) { { "upserts", assetUpserts }, { "deletes", assetDeletes } };
        }

        /// <summary>
        /// POST /api/v1/app/strings/enumerate (anonymous): published overrides for a client.
        /// </summary>
        /// <param name="query">Query (filter client).</param>
        /// <param name="client">Caller client (X-Client), used when the filter is absent.</param>
        /// <param name="clientId">Install or visitor id (rate limit), or null.</param>
        /// <param name="contentVersion">Current content version.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Page with the content version.</returns>
        /// <exception cref="ApiErrorException">400 ValidationFailed, 429 RateLimited.</exception>
        public async Task<AppContentPageView<ClientStringView>> EnumeratePublicStringsAsync(EnumerationQuery? query, string client, string? clientId, long contentVersion, CancellationToken token = default)
        {
            Limit(clientId);
            EnumerationRequest request = _Enumeration.Prepare(query, LiveUpdateCollections.PublicStrings, false);
            string target = request.Query.FilterString("client") ?? client;
            if (!_Clients.Contains(target)) throw ApiErrorException.Field("filters.client", "Use web or mobile.");
            EnumerationResult<RemoteStringOverride> page = await _Database.AppContent.EnumeratePublishedStringsAsync(request, target, token).ConfigureAwait(false);
            return AppContentPageView<ClientStringView>.From(page, page.Objects.Select(s => new ClientStringView { Key = s.Key, Value = s.Value ?? String.Empty, ContentVersion = s.ValidFromVersion ?? 0 }).ToList(), contentVersion);
        }

        /// <summary>
        /// POST /api/v1/app/banners/enumerate (anonymous): a standard page of the bounded active banner set for this caller.
        /// </summary>
        /// <param name="query">Query with ordering, search, dates, metadata and pagination.</param>
        /// <param name="context">Caller attributes.</param>
        /// <param name="client">web or mobile.</param>
        /// <param name="clientId">Install or visitor id (rate limit), or null.</param>
        /// <param name="contentVersion">Current content version.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Page with the content version.</returns>
        /// <exception cref="ApiErrorException">400 ValidationFailed, 429 RateLimited.</exception>
        public async Task<AppContentPageView<ClientBannerView>> EnumeratePublicBannersAsync(EnumerationQuery? query, FlagEvaluationContext context, string client, string? clientId, long contentVersion, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(context);
            Limit(clientId);
            await EnsureCacheAsync(contentVersion, token).ConfigureAwait(false);
            List<AppBanner> active = ActiveBanners(context, client);
            string scope = JsonSerializer.Serialize(new
            {
                client, clientId, contentVersion, context.UserId, context.SubjectId, context.Platform, context.AppVersion,
                context.UpdateId, context.RuntimeVersion, context.Channel, context.SignedIn, context.Membership, context.Country,
                revisions = active.Select(banner => banner.RevisionId).OrderBy(id => id, StringComparer.Ordinal).ToList()
            });
            EnumerationRequest request = _Enumeration.Prepare(query, LiveUpdateCollections.PublicBanners, false, scope);
            Dictionary<string, ObjectMetadata> metadata = await _Metadata.LoadAsync(TargetTypeEnum.Banner, active.Select(banner => banner.Id), false, token).ConfigureAwait(false);
            ObjectMetadata Metadata(AppBanner banner) => metadata.TryGetValue(banner.Id, out ObjectMetadata? value) ? value : new ObjectMetadata();
            EnumerationResult<AppBanner> page = CatalogEnumeration.Page(request, active, banner => banner.Id,
                banner => banner.CreatedUtc, banner => banner.Title + " " + banner.Body,
                banner => Metadata(banner).Labels, banner => Metadata(banner).Tags,
                banner => banner.Priority.ToString(CultureInfo.InvariantCulture));
            List<ClientBannerView> objects = page.Objects.Select(banner =>
            {
                ClientBannerView view = ClientBanner(banner);
                view.Labels = Metadata(banner).Labels;
                view.Tags = Metadata(banner).Tags;
                return view;
            }).ToList();
            return AppContentPageView<ClientBannerView>.From(page, objects, contentVersion);
        }

        /// <summary>
        /// POST /api/v1/app/assets/enumerate (anonymous): published remote asset descriptors.
        /// </summary>
        /// <param name="query">Query.</param>
        /// <param name="clientId">Install or visitor id (rate limit), or null.</param>
        /// <param name="contentVersion">Current content version.</param>
        /// <param name="origin">Request origin.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Page with the content version.</returns>
        /// <exception cref="ApiErrorException">429 RateLimited.</exception>
        public async Task<AppContentPageView<ClientAssetView>> EnumeratePublicAssetsAsync(EnumerationQuery? query, string? clientId, long contentVersion, string origin, CancellationToken token = default)
        {
            Limit(clientId);
            EnumerationRequest request = _Enumeration.Prepare(query, LiveUpdateCollections.PublicAssets, false);
            EnumerationResult<RemoteAsset> page = await _Database.AppContent.EnumeratePublishedAssetsAsync(request, token).ConfigureAwait(false);
            return AppContentPageView<ClientAssetView>.From(page, page.Objects.Select(a => ClientAsset(a, origin)).ToList(), contentVersion);
        }

        /// <summary>
        /// Force the next read to reload the published content (after a publish or restore).
        /// </summary>
        public void Invalidate()
        {
            _CachedVersion = -1;
        }

        #endregion

        #endregion

        #region Private-Methods

        private void Limit(string? clientId)
        {
            if (!String.IsNullOrEmpty(clientId)) _Limits.Enforce(_PropertyId + ":app:content:" + clientId, _Settings.AppConfig.ContentEnumeratePerHour, RateLimitService.Hour);
        }

        private static bool VisibleTo(string key, string client)
        {
            return key.StartsWith(client + ".", StringComparison.Ordinal) || key.StartsWith("shared.", StringComparison.Ordinal);
        }

        private List<AppBanner> ActiveBanners(FlagEvaluationContext context, string client)
        {
            DateTime now = context.NowUtc;
            List<AppBanner> result = new List<AppBanner>();
            foreach (AppBanner b in _Banners.OrderByDescending(b => b.Priority).ThenBy(b => b.Id, StringComparer.Ordinal))
            {
                if (b.State == FeatureFlagStateEnum.Archived || !b.Clients.Contains(client)) continue;
                if ((b.StartsUtc != null && b.StartsUtc.Value > now) || (b.EndsUtc != null && b.EndsUtc.Value <= now)) continue;
                if (b.Rules.Count > 0)
                {
                    FeatureFlag probe = new FeatureFlag { Key = b.Id, Enabled = true, DefaultValue = "false", OffValue = "false", Rules = b.Rules };
                    if (FeatureFlagEvaluator.Evaluate(probe, context).Value != "true") continue;
                }
                result.Add(b);
                if (result.Count >= _Settings.AppConfig.MaxActiveBanners) break;
            }
            return result;
        }

        private async Task EnsureCacheAsync(long version, CancellationToken token)
        {
            if (version == _CachedVersion) return;
            await _Lock.WaitAsync(token).ConfigureAwait(false);
            try
            {
                if (version == _CachedVersion) return;
                _Strings = await _Database.AppContent.ListCurrentStringsAsync(_Settings.AppConfig.MaxStringOverrides + 100, token).ConfigureAwait(false);
                _Banners = await _Database.AppContent.ListCurrentBannersAsync(_Settings.AppConfig.MaxBanners + 100, token).ConfigureAwait(false);
                _Assets = await _Database.AppContent.ListCurrentAssetsAsync(_Settings.AppConfig.MaxRemoteAssets + 100, token).ConfigureAwait(false);
                _CachedVersion = version;
            }
            finally
            {
                _Lock.Release();
            }
        }

        private async Task EnsureCatalogAsync(CancellationToken token)
        {
            if (_CatalogSynced) return;
            StringCatalog catalog = Catalog();
            await _Database.AppContent.SyncCatalogAsync(catalog.Sha256, catalog.Entries, token).ConfigureAwait(false);
            _CatalogSynced = true;
        }

        private async Task<StringCatalogEntry> RequireEntryAsync(string key, bool unknownIsBadRequest, CancellationToken token)
        {
            await EnsureCatalogAsync(token).ConfigureAwait(false);
            StringCatalogEntry? entry = String.IsNullOrWhiteSpace(key) ? null : await _Database.AppContent.ReadCatalogEntryAsync(key.Trim(), token).ConfigureAwait(false);
            if (entry != null) return entry;
            if (unknownIsBadRequest) throw new ApiErrorException(400, "UnknownStringKey", "The key is not in the engine string catalog.", new Dictionary<string, string> { { "key", "Unknown key " + key + "." } });
            throw ApiErrorException.NotFound("String key");
        }

        private async Task<RemoteStringView> StringViewAsync(StringCatalogEntry entry, CancellationToken token)
        {
            RemoteStringOverride? draft = await _Database.AppContent.ReadStringDraftAsync(entry.Key, token).ConfigureAwait(false);
            RemoteStringOverride? current = await _Database.AppContent.ReadStringCurrentAsync(entry.Key, token).ConfigureAwait(false);
            ObjectMetadata metadata = await _Metadata.LoadOneAsync(TargetTypeEnum.RemoteString, entry.Key, true, token).ConfigureAwait(false);
            return StringView(entry, draft, current, metadata);
        }

        private static RemoteStringView StringView(StringCatalogEntry entry, RemoteStringOverride? draft, RemoteStringOverride? current, ObjectMetadata metadata)
        {
            RemoteStringOverride? head = draft ?? (current != null && current.Value != null ? current : null);
            return new RemoteStringView
            {
                Key = entry.Key,
                Client = entry.Client,
                DefaultValue = entry.DefaultValue,
                Tokens = entry.Tokens,
                MaxLength = entry.MaxLength,
                Id = head?.Id,
                Value = head?.Value,
                PublishedValue = current?.Value,
                Notes = head?.Notes,
                Overridden = current != null && current.Value != null,
                PendingDelete = draft != null && draft.Value == null,
                State = head?.State,
                HasDraftChanges = draft != null,
                PublishedInVersion = current?.Value != null ? current.ValidFromVersion : null,
                ETag = head == null ? null : "\"" + head.RevisionId + "\"",
                Labels = metadata.Labels,
                Tags = metadata.Tags,
                UpdatedUtc = head?.UpdatedUtc
            };
        }

        private async Task<AppBanner> RequireBannerAsync(string id, CancellationToken token)
        {
            return await _Database.AppContent.ReadBannerDraftAsync(id, token).ConfigureAwait(false) ?? await _Database.AppContent.ReadBannerCurrentAsync(id, token).ConfigureAwait(false) ?? throw ApiErrorException.NotFound("Banner");
        }

        private async Task<RemoteAsset> RequireAssetAsync(string id, CancellationToken token)
        {
            return await _Database.AppContent.ReadAssetDraftAsync(id, token).ConfigureAwait(false) ?? await _Database.AppContent.ReadAssetCurrentAsync(id, token).ConfigureAwait(false) ?? throw ApiErrorException.NotFound("Remote asset");
        }

        private static void RequireMatch(string? ifMatch, string revisionId, string what)
        {
            if (String.IsNullOrWhiteSpace(ifMatch)) throw new ApiErrorException(412, "PreconditionFailed", "Send If-Match with the " + what + "'s ETag.");
            string v = ifMatch.Trim();
            if (v.StartsWith("W/", StringComparison.Ordinal)) v = v.Substring(2);
            if (v.Trim('"') != revisionId) throw new ApiErrorException(412, "PreconditionFailed", "The " + what + " changed since it was read; reload it.");
        }

        private static HashSet<string> BannerFields()
        {
            return new HashSet<string>(new[] { "title", "body", "style", "placement", "dismissible", "priority", "startsUtc", "endsUtc", "link", "imageAssetId", "clients", "rules" }, StringComparer.Ordinal);
        }

        private async Task ApplyBannerAsync(AppBanner b, BannerRequest r, HashSet<string> present, AppBanner? previous, CancellationToken token)
        {
            Dictionary<string, string> errors = new Dictionary<string, string>();
            if (present.Contains("title")) b.Title = (r.Title ?? String.Empty).Trim();
            if (present.Contains("body")) b.Body = (r.Body ?? String.Empty).Trim();
            if (present.Contains("style")) b.Style = String.IsNullOrWhiteSpace(r.Style) ? "Info" : r.Style.Trim();
            if (present.Contains("placement")) b.Placement = String.IsNullOrWhiteSpace(r.Placement) ? "Global" : r.Placement.Trim();
            if (present.Contains("dismissible") && r.Dismissible != null) b.Dismissible = r.Dismissible.Value;
            if (present.Contains("priority") && r.Priority != null) b.Priority = r.Priority.Value;
            if (present.Contains("startsUtc")) b.StartsUtc = r.StartsUtc?.ToUniversalTime();
            if (present.Contains("endsUtc")) b.EndsUtc = r.EndsUtc?.ToUniversalTime();
            if (present.Contains("link"))
            {
                BannerLinkRequest? link = r.Link;
                b.LinkLabel = String.IsNullOrWhiteSpace(link?.Label) ? null : link!.Label!.Trim();
                b.LinkPath = String.IsNullOrWhiteSpace(link?.Path) ? null : link!.Path!.Trim();
                b.LinkUrl = String.IsNullOrWhiteSpace(link?.Url) ? null : link!.Url!.Trim();
            }
            if (present.Contains("imageAssetId")) b.ImageAssetId = String.IsNullOrWhiteSpace(r.ImageAssetId) ? null : r.ImageAssetId.Trim();
            if (present.Contains("clients") && r.Clients != null) b.Clients = r.Clients.Select(c => (c ?? String.Empty).Trim().ToLowerInvariant()).Distinct(StringComparer.Ordinal).ToList();
            if (present.Contains("rules")) b.Rules = AppConfigService.BuildRules(r.Rules ?? new List<FlagRuleRequest>(), previous == null ? null : new FeatureFlag { Rules = previous.Rules }, FeatureFlagValueTypeEnum.Boolean, true, errors);
            if (b.Title.Length < 1 || b.Title.Length > 80) errors["title"] = "Required, 1 to 80 characters.";
            if (b.Body.Length > 500) errors["body"] = "At most 500 characters.";
            if (!Styles.Contains(b.Style)) errors["style"] = "Use " + String.Join(", ", Styles) + ".";
            if (!Placements.Contains(b.Placement)) errors["placement"] = "Use " + String.Join(", ", Placements) + ".";
            if (b.Style == "Critical") b.Dismissible = false;
            if (b.Priority < -1000 || b.Priority > 1000) errors["priority"] = "From -1000 to 1000.";
            if (b.StartsUtc != null && b.EndsUtc != null && b.EndsUtc <= b.StartsUtc) errors["endsUtc"] = "Must be after startsUtc.";
            if (b.LinkPath != null && b.LinkUrl != null) errors["link"] = "Send path or url, not both.";
            else if (b.LinkPath != null && (!b.LinkPath.StartsWith("/", StringComparison.Ordinal) || b.LinkPath.StartsWith("//", StringComparison.Ordinal) || b.LinkPath.Length > 300)) errors["link.path"] = "An app route starting with /, at most 300 characters.";
            else if (b.LinkUrl != null && (b.LinkUrl.Length > 2000 || !Uri.TryCreate(b.LinkUrl, UriKind.Absolute, out Uri? uri) || uri.Scheme != Uri.UriSchemeHttps)) errors["link.url"] = "An https URL, at most 2000 characters.";
            if ((b.LinkPath != null || b.LinkUrl != null) && (b.LinkLabel == null || b.LinkLabel.Length > 40)) errors["link.label"] = "Required with a link, 1 to 40 characters.";
            if (b.LinkPath == null && b.LinkUrl == null) b.LinkLabel = null;
            if (b.Clients.Count == 0 || b.Clients.Any(c => !_Clients.Contains(c))) errors["clients"] = "One or both of mobile, web.";
            if (errors.Count > 0) throw new ApiErrorException(400, errors.Keys.Any(k => k.StartsWith("rules", StringComparison.Ordinal)) ? "InvalidTargetingRule" : "ValidationFailed", "One or more fields are invalid.", errors);
            Dictionary<string, string> ruleErrors = FeatureFlagEvaluator.Validate(b.Rules, _Settings.AppConfig.MaxRulesPerFlag, _Settings.AppConfig.MaxConditionsPerRule, _Settings.AppConfig.MaxValuesPerCondition);
            if (ruleErrors.Count == 0) ruleErrors = await AppConfigService.ProfileFieldErrorsAsync(_Database, b.Rules, token).ConfigureAwait(false);
            if (ruleErrors.Count > 0) throw new ApiErrorException(400, "InvalidTargetingRule", "A targeting rule is invalid.", ruleErrors);
            if (b.ImageAssetId != null && await _Database.AppContent.ReadAssetDraftAsync(b.ImageAssetId, token).ConfigureAwait(false) == null &&
                await _Database.AppContent.ReadAssetCurrentAsync(b.ImageAssetId, token).ConfigureAwait(false) == null) throw ApiErrorException.Field("imageAssetId", "Unknown remote asset.");
            if (_Words != null && (present.Contains("title") || present.Contains("body")) && await _Words.IsBlockedAsync(b.Title + "\n" + b.Body, token).ConfigureAwait(false))
                throw ApiErrorException.Field(present.Contains("body") ? "body" : "title", "The text contains a blocked word.");
        }

        private async Task ApplyAssetAsync(RemoteAsset a, RemoteAssetRequest r, HashSet<string> present, CancellationToken token)
        {
            Dictionary<string, string> errors = new Dictionary<string, string>();
            if (present.Contains("key")) a.Key = (r.Key ?? String.Empty).Trim();
            if (present.Contains("altText")) a.AltText = String.IsNullOrWhiteSpace(r.AltText) ? null : r.AltText.Trim();
            if (a.Key.Length < 3 || a.Key.Length > 64 || !_Key.IsMatch(a.Key)) errors["key"] = "Use 3 to 64 characters: a lowercase letter, then letters and digits, with up to 3 more dot-separated parts, for example onboarding.welcome.";
            if (a.AltText != null && a.AltText.Length > 300) errors["altText"] = "At most 300 characters.";
            if (errors.Count > 0) throw ApiErrorException.Validation(errors);
            if (present.Contains("key") && await _Database.AppContent.AssetKeyInUseAsync(a.Key, a.Id, token).ConfigureAwait(false)) throw new ApiErrorException(409, "Conflict", "The asset key " + a.Key + " is in use.");
        }

        private static async Task<byte[]> ReadBoundedAsync(Stream body, long max, CancellationToken token)
        {
            using (MemoryStream buffer = new MemoryStream())
            {
                byte[] chunk = new byte[81920];
                while (true)
                {
                    int read = await body.ReadAsync(chunk.AsMemory(0, chunk.Length), token).ConfigureAwait(false);
                    if (read <= 0) break;
                    if (buffer.Length + read > max) throw new ApiErrorException(413, "TooLarge", "The image is larger than allowed (appConfig.maxAssetMegabytes).");
                    buffer.Write(chunk, 0, read);
                }
                return buffer.ToArray();
            }
        }

        private static AppBanner Copy(AppBanner b)
        {
            return new AppBanner
            {
                RevisionId = b.RevisionId,
                Id = b.Id,
                Title = b.Title,
                Body = b.Body,
                Style = b.Style,
                Placement = b.Placement,
                Dismissible = b.Dismissible,
                Priority = b.Priority,
                StartsUtc = b.StartsUtc,
                EndsUtc = b.EndsUtc,
                LinkLabel = b.LinkLabel,
                LinkPath = b.LinkPath,
                LinkUrl = b.LinkUrl,
                ImageAssetId = b.ImageAssetId,
                Clients = new List<string>(b.Clients),
                Rules = b.Rules.Select(r => new FeatureFlagRule
                {
                    Id = r.Id,
                    Position = r.Position,
                    Description = r.Description,
                    Value = r.Value,
                    RolloutBasisPoints = r.RolloutBasisPoints,
                    Conditions = r.Conditions.Select(c => new FeatureFlagCondition { Id = c.Id, Position = c.Position, Attribute = c.Attribute, Operator = c.Operator, Argument = c.Argument, Values = new List<string>(c.Values) }).ToList()
                }).ToList(),
                State = b.State,
                CreatedUtc = b.CreatedUtc,
                UpdatedUtc = b.UpdatedUtc
            };
        }

        private static RemoteAsset Copy(RemoteAsset a)
        {
            return new RemoteAsset
            {
                RevisionId = a.RevisionId,
                Id = a.Id,
                Key = a.Key,
                ContentType = a.ContentType,
                SizeBytes = a.SizeBytes,
                Width = a.Width,
                Height = a.Height,
                Sha256Hex = a.Sha256Hex,
                DarkSha256Hex = a.DarkSha256Hex,
                DarkWidth = a.DarkWidth,
                DarkHeight = a.DarkHeight,
                DarkSizeBytes = a.DarkSizeBytes,
                AltText = a.AltText,
                State = a.State,
                CreatedUtc = a.CreatedUtc,
                UpdatedUtc = a.UpdatedUtc
            };
        }

        private static BannerLinkView? LinkOf(AppBanner b)
        {
            if (b.LinkPath == null && b.LinkUrl == null) return null;
            return new BannerLinkView { Label = b.LinkLabel ?? String.Empty, Path = b.LinkPath, Url = b.LinkUrl };
        }

        private static BannerView BannerViewOf(AppBanner b, ObjectMetadata metadata, bool hasDraft)
        {
            return new BannerView
            {
                Id = b.Id,
                Title = b.Title,
                Body = b.Body,
                Style = b.Style,
                Placement = b.Placement,
                Dismissible = b.Dismissible,
                Priority = b.Priority,
                StartsUtc = b.StartsUtc,
                EndsUtc = b.EndsUtc,
                Link = LinkOf(b),
                ImageAssetId = b.ImageAssetId,
                Clients = b.Clients,
                Rules = b.Rules.Select(r => AppConfigService.RuleView(r, FeatureFlagValueTypeEnum.Boolean)).ToList(),
                ContentHash = ContentHash(b.Title, b.Body),
                State = b.State,
                HasDraftChanges = hasDraft,
                PublishedInVersion = b.IsDraft ? null : b.ValidFromVersion,
                ETag = "\"" + b.RevisionId + "\"",
                Labels = metadata.Labels,
                Tags = metadata.Tags,
                CreatedUtc = b.CreatedUtc,
                UpdatedUtc = b.UpdatedUtc
            };
        }

        private static ClientBannerView ClientBanner(AppBanner b)
        {
            return new ClientBannerView
            {
                Id = b.Id,
                Title = b.Title,
                Body = b.Body,
                Style = b.Style,
                Placement = b.Placement,
                Dismissible = b.Dismissible && b.Style != "Critical",
                Priority = b.Priority,
                StartsUtc = b.StartsUtc,
                EndsUtc = b.EndsUtc,
                Link = LinkOf(b),
                ImageAssetId = b.ImageAssetId,
                ContentHash = ContentHash(b.Title, b.Body),
                ContentVersion = b.ValidFromVersion ?? 0
            };
        }

        private RemoteAssetView AssetViewOf(RemoteAsset a, ObjectMetadata metadata, bool hasDraft, string origin)
        {
            return new RemoteAssetView
            {
                Id = a.Id,
                Key = a.Key,
                ContentType = a.ContentType,
                SizeBytes = a.SizeBytes,
                Width = a.Width,
                Height = a.Height,
                Sha256 = a.Sha256Hex,
                Url = a.Sha256Hex == null ? null : ContentAssetUrl(origin, a.Sha256Hex),
                AltText = a.AltText,
                DarkVariant = Dark(a, origin),
                State = a.State,
                HasDraftChanges = hasDraft,
                PublishedInVersion = a.IsDraft ? null : a.ValidFromVersion,
                ETag = "\"" + a.RevisionId + "\"",
                Labels = metadata.Labels,
                Tags = metadata.Tags,
                CreatedUtc = a.CreatedUtc,
                UpdatedUtc = a.UpdatedUtc
            };
        }

        private ClientAssetView ClientAsset(RemoteAsset a, string origin)
        {
            return new ClientAssetView
            {
                Id = a.Id,
                Key = a.Key,
                ContentType = a.ContentType,
                SizeBytes = a.SizeBytes,
                Width = a.Width,
                Height = a.Height,
                Sha256 = a.Sha256Hex ?? String.Empty,
                Url = a.Sha256Hex == null ? String.Empty : ContentAssetUrl(origin, a.Sha256Hex),
                AltText = a.AltText,
                DarkVariant = Dark(a, origin),
                ContentVersion = a.ValidFromVersion ?? 0
            };
        }

        private RemoteAssetVariantView? Dark(RemoteAsset a, string origin)
        {
            if (a.DarkSha256Hex == null) return null;
            return new RemoteAssetVariantView { Sha256 = a.DarkSha256Hex, Url = ContentAssetUrl(origin, a.DarkSha256Hex), Width = a.DarkWidth, Height = a.DarkHeight, SizeBytes = a.DarkSizeBytes };
        }

        #endregion
    }
}
