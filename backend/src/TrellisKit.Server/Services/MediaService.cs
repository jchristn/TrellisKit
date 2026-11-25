namespace TrellisKit.Server.Services
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core.Database;
    using TrellisKit.Core.Enumeration;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Exceptions;
    using TrellisKit.Core.Helpers;
    using TrellisKit.Core.Integrations;
    using TrellisKit.Core.Integrations.Implementations;
    using TrellisKit.Core.Integrations.Interfaces;
    using TrellisKit.Core.Integrations.Models;
    using TrellisKit.Core.Models;
    using TrellisKit.Core.Observability;
    using TrellisKit.Core.Requests;
    using TrellisKit.Core.Responses;
    using TrellisKit.Core.Settings;

    /// <summary>
    /// Media pipeline service (Phase 2 contract section 4 and 10.3): upload sessions, local uploads, complete, status,
    /// alt text, delete, the media grant, the admin library, and the public API used by the content and moderation
    /// features (attachability checks, attach and detach, views per target, readiness, retain and restore, public
    /// copies, purge, account deletion). Also stores link preview images.
    /// Extension points: <see cref="AttachedVisibility"/>, <see cref="AddSettledHandler"/>,
    /// <see cref="AddDetachedHandler"/>, and <see cref="AltTextFilter"/>.
    /// Thread safety: safe to call concurrently; state changes are conditional database updates.
    /// </summary>
    public class MediaService : ILinkPreviewImageStore
    {
        #region Public-Members

        /// <summary>
        /// Property id: the first segment of every object key this service builds (through MediaKeys).
        /// </summary>
        public string PropertyId { get; }

        /// <summary>
        /// Largest number of ids in GET /api/v1/media?ids=.
        /// </summary>
        public const int MaxBatchIds = 20;

        /// <summary>
        /// Longest alt text in Unicode scalar values.
        /// </summary>
        public const int MaxAltTextLength = 1000;

        /// <summary>
        /// Longest stored original file name.
        /// </summary>
        public const int MaxFileNameLength = 255;

        /// <summary>
        /// Long edge of stored link preview images.
        /// </summary>
        public const int LinkPreviewLongEdge = 1200;

        /// <summary>
        /// Rate limit bucket of member reads (rateLimits.memberReadsPerMinute).
        /// </summary>
        public const string ReadBucket = "member.reads";

        /// <summary>
        /// URL and payload builder.
        /// </summary>
        public MediaUrlBuilder Urls { get; }

        /// <summary>
        /// Stored object operations.
        /// </summary>
        public MediaStorageService Storage { get; }

        /// <summary>
        /// Processing pipeline (worker and webhook).
        /// </summary>
        public MediaPipeline Pipeline { get; }

        /// <summary>
        /// Wake-up signal of the media processing worker.
        /// </summary>
        public MediaWorkSignal Signal { get; } = new MediaWorkSignal();

        /// <summary>
        /// Validator of tk_media cookies (Local mode file route).
        /// </summary>
        public LocalMediaCookieSigner LocalCookies { get; }

        /// <summary>
        /// Decides whether a caller may see media that is attached to content (GET /api/v1/media/{id} and the batch
        /// route, for callers who are neither the owner nor a SystemAdmin). Default: any member may see attached media.
        /// The content feature replaces it with "can see the target".
        /// </summary>
        public Func<MediaAttachment, ContentActor, CancellationToken, Task<bool>> AttachedVisibility
        {
            get { return _AttachedVisibility; }
            set { _AttachedVisibility = value ?? throw new ArgumentNullException(nameof(AttachedVisibility)); }
        }

        /// <summary>
        /// Per-object URL tokens of private media (ticket and direct message attachments).
        /// </summary>
        public PrivateMediaTokens PrivateTokens { get; }

        /// <summary>
        /// Registered private purposes and their access rules (docs/PHASE5_CONTRACT.md sections 3.7 and 6.1): the support
        /// module registers Ticket, the messaging module registers Message. Register with <see cref="RegisterPrivateScope"/>.
        /// </summary>
        public IReadOnlyDictionary<MediaPurposeEnum, PrivateMediaScope> PrivateScopeResolvers
        {
            get
            {
                lock (_HandlerLock) return new Dictionary<MediaPurposeEnum, PrivateMediaScope>(_PrivateScopes);
            }
        }

        /// <summary>
        /// Root of the private file route: /api/v1/media/private/{token}/{file}.
        /// </summary>
        public const string PrivateFilesPath = Core.Constants.ApiPrefix + "/media/private";

        /// <summary>
        /// Optional text check of alt text (the word filter plugs in here; throw 400 ContentFiltered with
        /// fieldErrors.altText). Called with the caller and the normalized text before alt text is stored.
        /// </summary>
        public Func<ContentActor, string, CancellationToken, Task>? AltTextFilter { get; set; } = null;

        /// <summary>
        /// Supplies the property's current access configuration (who may read media files with a bearer token in Local
        /// mode). Default FullyGated.
        /// </summary>
        /// <exception cref="ArgumentNullException">Set to null.</exception>
        public Func<AccessPolicy> Access
        {
            get { return _Access; }
            set { _Access = value ?? throw new ArgumentNullException(nameof(Access)); }
        }

        #endregion

        #region Private-Members

        private const long _MiB = 1024L * 1024L;
        private readonly AppSettings _Settings;
        private readonly DatabaseDriverBase _Database;
        private readonly IntegrationSet _Integrations;
        private readonly RateLimitService _Limits;
        private readonly UserSummaryService _Summaries;
        private readonly MetadataService _Metadata;
        private readonly EnumerationService _Enumeration;
        private readonly TokenService _Tokens;
        private Func<AccessPolicy> _Access = () => AccessPolicy.FullyGatedDefault;
        private readonly AppLogger _Logger;
        private readonly List<Func<MediaAttachment, CancellationToken, Task>> _SettledHandlers = new List<Func<MediaAttachment, CancellationToken, Task>>();
        private readonly List<Func<TargetTypeEnum, string, CancellationToken, Task>> _DetachedHandlers = new List<Func<TargetTypeEnum, string, CancellationToken, Task>>();
        private readonly object _HandlerLock = new object();
        private Func<MediaAttachment, ContentActor, CancellationToken, Task<bool>> _AttachedVisibility = (m, a, t) => Task.FromResult(true);
        private readonly Dictionary<MediaPurposeEnum, PrivateMediaScope> _PrivateScopes = new Dictionary<MediaPurposeEnum, PrivateMediaScope>();

        private static readonly string[] _ImageTypes = new[] { "image/jpeg", "image/png", "image/webp" };
        private static readonly string[] _VideoTypes = new[] { "video/mp4", "video/quicktime" };
        private static readonly string[] _DocumentTypes = new[] { "application/pdf", "text/vtt" };

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the service.
        /// </summary>
        /// <param name="settings">Running settings (read live).</param>
        /// <param name="database">Database.</param>
        /// <param name="integrations">Integrations (read on each call).</param>
        /// <param name="limits">Rate limits.</param>
        /// <param name="summaries">User summaries.</param>
        /// <param name="metadata">Labels and tags.</param>
        /// <param name="enumeration">Enumeration preparation.</param>
        /// <param name="tokens">Access token validation (file route bearer access).</param>
        /// <param name="logger">Logger.</param>
        /// <param name="propertyId">Property id: the first segment of every object key.</param>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        public MediaService(AppSettings settings, DatabaseDriverBase database, IntegrationSet integrations, RateLimitService limits, UserSummaryService summaries,
            MetadataService metadata, EnumerationService enumeration, TokenService tokens, AppLogger logger, string propertyId)
        {
            PropertyId = propertyId ?? throw new ArgumentNullException(nameof(propertyId));
            _Settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _Database = database ?? throw new ArgumentNullException(nameof(database));
            _Integrations = integrations ?? throw new ArgumentNullException(nameof(integrations));
            _Limits = limits ?? throw new ArgumentNullException(nameof(limits));
            _Summaries = summaries ?? throw new ArgumentNullException(nameof(summaries));
            _Metadata = metadata ?? throw new ArgumentNullException(nameof(metadata));
            _Enumeration = enumeration ?? throw new ArgumentNullException(nameof(enumeration));
            _Tokens = tokens ?? throw new ArgumentNullException(nameof(tokens));
            _Logger = logger ?? throw new ArgumentNullException(nameof(logger));
            Urls = new MediaUrlBuilder(settings, propertyId);
            Storage = new MediaStorageService(integrations, propertyId, logger);
            Pipeline = new MediaPipeline(settings, database, integrations, Storage, propertyId, logger) { Settled = NotifySettledAsync };
            LocalCookies = new LocalMediaCookieSigner(integrations.TokenProtector);
            PrivateTokens = new PrivateMediaTokens(integrations.TokenProtector);
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Subscribe to media becoming Ready or Failed (the feed publish worker uses this). Handlers run after the
        /// state is stored; exceptions are logged and never affect processing.
        /// </summary>
        /// <param name="handler">Handler.</param>
        /// <exception cref="ArgumentNullException">The handler is null.</exception>
        public void AddSettledHandler(Func<MediaAttachment, CancellationToken, Task> handler)
        {
            ArgumentNullException.ThrowIfNull(handler);
            lock (_HandlerLock) _SettledHandlers.Add(handler);
        }

        /// <summary>
        /// Subscribe to an admin delete detaching media from a target (so the content feature can recompute
        /// attachmentcount and hasvideo). Called with the target type and id after the media was detached and deleted.
        /// </summary>
        /// <param name="handler">Handler.</param>
        /// <exception cref="ArgumentNullException">The handler is null.</exception>
        public void AddDetachedHandler(Func<TargetTypeEnum, string, CancellationToken, Task> handler)
        {
            ArgumentNullException.ThrowIfNull(handler);
            lock (_HandlerLock) _DetachedHandlers.Add(handler);
        }

        /// <summary>
        /// Register the access rules of a private purpose (replaces an earlier registration). Call during module attachment.
        /// </summary>
        /// <param name="purpose">Ticket or Message.</param>
        /// <param name="scope">Rules.</param>
        /// <exception cref="ArgumentNullException">The scope is null.</exception>
        /// <exception cref="ArgumentException">The purpose is Content.</exception>
        public void RegisterPrivateScope(MediaPurposeEnum purpose, PrivateMediaScope scope)
        {
            ArgumentNullException.ThrowIfNull(scope);
            if (purpose == MediaPurposeEnum.Content) throw new ArgumentException("Content media is not private.", nameof(purpose));
            lock (_HandlerLock) _PrivateScopes[purpose] = scope;
        }

        /// <summary>
        /// Charge one member read (rateLimits.memberReadsPerMinute; SystemAdmin exempt).
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <exception cref="ApiErrorException">429 RateLimited.</exception>
        public void EnforceRead(ContentActor actor)
        {
            _Limits.EnforceMember(actor, ReadBucket, _Settings.RateLimits.MemberReadsPerMinute, RateLimitService.Minute);
        }

        #region Member-Routes

        /// <summary>
        /// POST /api/v1/media/uploads: create an upload session (contract section 4.2).
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="request">Request.</param>
        /// <param name="origin">Request origin for Local mode URLs, for example http://127.0.0.1:8000.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Session.</returns>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        /// <exception cref="ApiErrorException">400 ValidationFailed or VideoTooLong, 403 Muted, PostingNotAllowed, MediaNotAllowed, or VideoUploadNotAllowed, 413 TooLarge, 415 UnsupportedMediaType, 429 RateLimited.</exception>
        /// <exception cref="IntegrationNotConfiguredException">S3 mode without the bucket (503).</exception>
        public async Task<UploadSessionResponse> CreateUploadAsync(ContentActor actor, MediaUploadRequest request, string origin, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            ArgumentNullException.ThrowIfNull(request);
            string userId = actor.RequireUserId();
            MediaPurposeEnum purpose = ParsePurpose(request.Purpose);
            PrivateMediaScope? scope = purpose == MediaPurposeEnum.Content ? null : Scope(purpose);
            if (scope == null || scope.RequirePostingPermissions) actor.RequireCanPostMedia();
            Dictionary<string, string> errors = new Dictionary<string, string>();
            MediaKindEnum? kind = ParseKind(request.Kind);
            if (kind == null) errors["kind"] = String.IsNullOrWhiteSpace(request.Kind) ? "Required." : "Use Image, Video, or Document.";
            if (kind == MediaKindEnum.Video)
            {
                if (scope != null && !scope.AllowVideo()) throw new ApiErrorException(403, "VideoUploadNotAllowed", "Video attachments are not allowed here.");
                if (scope == null || scope.RequirePostingPermissions) actor.RequireCanUploadVideo();
            }
            // Document media (lesson resources and captions) are uploaded by library staff only (docs/PHASE4_CONTRACT.md section 5.3).
            if (kind == MediaKindEnum.Document && !actor.IsLibraryStaff) throw ApiErrorException.Forbidden("Only library staff can upload documents.");
            string? scopeId = String.IsNullOrWhiteSpace(request.ScopeId) ? null : request.ScopeId.Trim();
            if (scope == null && scopeId != null) errors["scopeId"] = "Only private purposes (Ticket, Message) take a scope.";
            if (scope != null && scopeId != null && !await scope.CanAccess(userId, scopeId, token).ConfigureAwait(false)) throw ApiErrorException.NotFound(purpose.ToString());

            string contentType = (request.ContentType ?? String.Empty).Split(';')[0].Trim().ToLowerInvariant();
            if (contentType.Length == 0) errors["contentType"] = "Required.";
            else if (!_ImageTypes.Contains(contentType) && !_VideoTypes.Contains(contentType) && !(kind == MediaKindEnum.Document && _DocumentTypes.Contains(contentType)))
                throw new ApiErrorException(415, "UnsupportedMediaType", kind == MediaKindEnum.Document ? "Accepted document types are application/pdf and text/vtt." : "Accepted types are image/jpeg, image/png, image/webp, video/mp4, and video/quicktime.");
            else if (kind != null && kind != MediaKindEnum.Document && (kind == MediaKindEnum.Image) != _ImageTypes.Contains(contentType)) errors["contentType"] = "Does not match kind " + kind + ".";
            if (request.SizeBytes == null || request.SizeBytes < 1) errors["sizeBytes"] = "Must be 1 or more.";
            string? fileName = String.IsNullOrWhiteSpace(request.FileName) ? null : request.FileName.Trim();
            if (fileName != null && fileName.Length > MaxFileNameLength) errors["fileName"] = "At most " + MaxFileNameLength + " characters.";
            if (kind == MediaKindEnum.Video && (request.DurationSeconds == null || request.DurationSeconds <= 0 || Double.IsNaN(request.DurationSeconds.Value) || Double.IsInfinity(request.DurationSeconds.Value)))
                errors["durationSeconds"] = "Required for video and must be more than 0.";
            if (errors.Count > 0) throw ApiErrorException.Validation(errors);

            MediaSettings media = _Settings.Media;
            long size = request.SizeBytes!.Value;
            long maxBytes = MaxBytes(kind!.Value, actor);
            if (maxBytes > 0 && size > maxBytes) throw new ApiErrorException(413, "TooLarge", "The file is larger than the limit of " + maxBytes + " bytes.");
            if (kind == MediaKindEnum.Video)
            {
                int cap = DurationCapSeconds(actor);
                if (cap > 0 && request.DurationSeconds!.Value > cap) throw new ApiErrorException(400, "VideoTooLong", "Videos may be at most " + cap + " seconds long.");
            }

            bool isStaff = actor.IsStaffFor(TargetTypeEnum.Media);
            await _Metadata.ValidateAsync(actor, request.Labels, request.Tags, isStaff, token).ConfigureAwait(false);

            DateTime now = DateTime.UtcNow;
            if (!actor.IsSystemAdmin && !actor.IsLibraryEditor && media.MemberDailyUploadMegabytes > 0)
            {
                DateTime midnight = now.Date;
                long used = await _Database.Media.SumBytesSinceAsync(userId, midnight, token).ConfigureAwait(false);
                if (used + size > media.MemberDailyUploadMegabytes * _MiB)
                {
                    TrellisKitTelemetry.Add(TrellisKitTelemetry.MediaUploads, 1, "kind", KindLabel(kind.Value), "mode", "none", "outcome", "rejected");
                    throw ApiErrorException.RateLimited((int)Math.Ceiling((midnight.AddDays(1) - now).TotalSeconds), "The daily upload quota of " + media.MemberDailyUploadMegabytes + " MB is used up. Try again tomorrow (UTC).");
                }
            }
            _Limits.EnforceMember(actor, "media.uploads", media.UploadsPerHour, RateLimitService.Hour);

            IObjectStore store = _Integrations.ObjectStore;
            MediaLocationEnum location = kind == MediaKindEnum.Video ? MediaLocationEnum.VideoSource : MediaLocationEnum.Source;
            store.RequireConfigured(location);

            string id = IdGenerator.Generate(Core.Constants.MediaIdPrefix);
            MediaAttachment item = new MediaAttachment
            {
                Id = id,
                OwnerUserId = userId,
                Kind = kind.Value,
                Status = MediaStatusEnum.Uploading,
                ContentType = contentType,
                OriginalFileName = fileName,
                SizeBytes = size,
                DeclaredDurationSeconds = kind == MediaKindEnum.Video ? request.DurationSeconds : null,
                StorageMode = store.Mode,
                SourceBucket = store.LocationName(location),
                SourceKey = MediaKeys.Original(PropertyId, id),
                UploadExpiresUtc = now.AddHours(media.UploadExpiryHours),
                Purpose = purpose,
                ScopeId = scope == null ? null : scopeId,
                CreatedUtc = now,
                UpdatedUtc = now
            };
            bool multipart = kind == MediaKindEnum.Video && size > media.MultipartThresholdMegabytes * _MiB;
            if (multipart)
            {
                item.PartSizeBytes = PartSize(size, media.MultipartPartMegabytes);
                item.PartCount = (int)((size + item.PartSizeBytes - 1) / item.PartSizeBytes);
                item.MultipartUploadId = await store.InitiateMultipartAsync(Target(item, origin), token).ConfigureAwait(false);
            }
            await _Database.Media.CreateAsync(item, token).ConfigureAwait(false);
            ObjectMetadata metadata = await _Metadata.ApplyAsync(actor, TargetTypeEnum.Media, id, request.Labels, request.Tags, token).ConfigureAwait(false);
            TrellisKitTelemetry.Add(TrellisKitTelemetry.MediaUploads, 1, "kind", KindLabel(item.Kind), "mode", multipart ? "multipart" : "single", "outcome", "created");
            return new UploadSessionResponse
            {
                Media = Urls.Build(item, null, false, metadata),
                Upload = await InstructionsAsync(item, origin, false, token).ConfigureAwait(false)
            };
        }

        /// <summary>
        /// POST /api/v1/media/{id}/upload-urls: fresh URLs plus the parts storage already has (contract section 4.3).
        /// </summary>
        /// <param name="actor">Caller (the owner).</param>
        /// <param name="mediaId">Media identifier.</param>
        /// <param name="origin">Request origin for Local mode URLs.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Upload instructions.</returns>
        /// <exception cref="ApiErrorException">404 NotFound, 409 UploadIncomplete (not Uploading), 410 UploadExpired.</exception>
        public async Task<UploadUrlsResponse> RefreshUploadUrlsAsync(ContentActor actor, string mediaId, string origin, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            MediaAttachment media = await OwnedAsync(actor, mediaId, token).ConfigureAwait(false);
            if (media.Status != MediaStatusEnum.Uploading) throw new ApiErrorException(409, "UploadIncomplete", "The upload session is no longer open (status " + media.Status + ").");
            await RequireNotExpiredAsync(media, token).ConfigureAwait(false);
            return new UploadUrlsResponse { Upload = await InstructionsAsync(media, origin, true, token).ConfigureAwait(false) };
        }

        /// <summary>
        /// POST /api/v1/media/{id}/complete (contract section 4.4).
        /// </summary>
        /// <param name="actor">Caller (the owner).</param>
        /// <param name="mediaId">Media identifier.</param>
        /// <param name="request">Parts for multipart uploads.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Media and whether this call accepted it (202) or it was already done (200).</returns>
        /// <exception cref="ApiErrorException">400 ValidationFailed or VideoTooLong, 404 NotFound, 409 UploadIncomplete, 410 UploadExpired, 415 UnsupportedMediaType.</exception>
        public async Task<MediaCompleteOutcome> CompleteAsync(ContentActor actor, string mediaId, MediaCompleteRequest request, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            ArgumentNullException.ThrowIfNull(request);
            MediaAttachment media = await OwnedAsync(actor, mediaId, token).ConfigureAwait(false);
            if (media.Status != MediaStatusEnum.Uploading) return new MediaCompleteOutcome { Media = await ViewAsync(media, actor, token).ConfigureAwait(false), Accepted = false };
            await RequireNotExpiredAsync(media, token).ConfigureAwait(false);
            IObjectStore store = _Integrations.ObjectStore;
            string mode = media.PartCount > 0 ? "multipart" : "single";
            string kindLabel = KindLabel(media.Kind);
            Stopwatch verify = Stopwatch.StartNew();

            if (media.PartCount > 0)
            {
                List<MediaCompletePart> parts = request.Parts ?? new List<MediaCompletePart>();
                if (parts.Count == 0) throw ApiErrorException.Field("parts", "Required for multipart uploads.");
                if (parts.Count > 1000) throw ApiErrorException.Field("parts", "At most 1000 parts.");
                List<StoredPart> stored = await store.ListPartsAsync(media.SourceLocation, media.SourceKey, media.MultipartUploadId ?? String.Empty, token).ConfigureAwait(false);
                Dictionary<int, StoredPart> byNumber = stored.GroupBy(p => p.PartNumber).ToDictionary(g => g.Key, g => g.First());
                List<StoredPart> complete = new List<StoredPart>();
                long total = 0;
                for (int n = 1; n <= media.PartCount; n++)
                {
                    MediaCompletePart? reported = parts.FirstOrDefault(p => p.PartNumber == n);
                    if (reported == null || !byNumber.TryGetValue(n, out StoredPart? found) || !EtagEquals(found.ETag, reported.Etag))
                        throw new ApiErrorException(409, "UploadIncomplete", "Part " + n + " is missing or its ETag does not match.");
                    long expected = n < media.PartCount ? media.PartSizeBytes : media.SizeBytes - media.PartSizeBytes * (media.PartCount - 1);
                    if (found.SizeBytes != expected) throw new ApiErrorException(409, "UploadIncomplete", "Part " + n + " has " + found.SizeBytes + " bytes; expected " + expected + ".");
                    total += found.SizeBytes;
                    complete.Add(new StoredPart { PartNumber = n, ETag = found.ETag, SizeBytes = found.SizeBytes });
                }
                if (total != media.SizeBytes) throw new ApiErrorException(409, "UploadIncomplete", "The parts hold " + total + " bytes; the session declared " + media.SizeBytes + ".");
                try
                {
                    await store.CompleteMultipartAsync(media.SourceLocation, media.SourceKey, media.MultipartUploadId ?? String.Empty, complete, token).ConfigureAwait(false);
                }
                catch (InvalidOperationException e)
                {
                    throw new ApiErrorException(409, "UploadIncomplete", e.Message);
                }
                media.MultipartUploadId = null;
            }

            ObjectHead? head = await store.HeadAsync(media.SourceLocation, media.SourceKey, token).ConfigureAwait(false);
            if (head == null) throw new ApiErrorException(409, "UploadIncomplete", "The file has not been uploaded yet.");
            if (head.SizeBytes != media.SizeBytes)
            {
                if (media.PartCount == 0) throw new ApiErrorException(409, "UploadIncomplete", "The uploaded file has " + head.SizeBytes + " bytes; the session declared " + media.SizeBytes + ".");
                await FailUploadAsync(media, MediaFailureReasonEnum.Corrupt, token).ConfigureAwait(false);
                throw new ApiErrorException(409, "UploadIncomplete", "The assembled file has " + head.SizeBytes + " bytes; the session declared " + media.SizeBytes + ".");
            }

            byte[] header = await store.ReadRangeAsync(media.SourceLocation, media.SourceKey, 0, MediaSniffer.HeaderLength, token).ConfigureAwait(false);
            if (!MediaSniffer.Matches(media.ContentType, header))
            {
                await FailUploadAsync(media, MediaFailureReasonEnum.UnsupportedFormat, token).ConfigureAwait(false);
                TrellisKitTelemetry.Add(TrellisKitTelemetry.MediaUploads, 1, "kind", kindLabel, "mode", mode, "outcome", "rejected");
                throw new ApiErrorException(415, "UnsupportedMediaType", "The file content is not " + media.ContentType + ".");
            }

            if (media.Kind == MediaKindEnum.Video)
            {
                Mp4Info? info = await Mp4HeaderReader.ReadAsync((o, l, t) => store.ReadRangeAsync(media.SourceLocation, media.SourceKey, o, l, t), head.SizeBytes, 64L * _MiB, token).ConfigureAwait(false);
                if (info == null)
                {
                    await FailUploadAsync(media, MediaFailureReasonEnum.UnsupportedFormat, token).ConfigureAwait(false);
                    TrellisKitTelemetry.Add(TrellisKitTelemetry.MediaUploads, 1, "kind", kindLabel, "mode", mode, "outcome", "rejected");
                    throw new ApiErrorException(415, "UnsupportedMediaType", "The video has no readable movie header (moov and mvhd).");
                }
                media.DurationSeconds = Math.Round(info.DurationSeconds, 3);
                int cap = await OwnerDurationCapAsync(media, actor, token).ConfigureAwait(false);
                if (cap > 0 && info.DurationSeconds > cap + 0.5)
                {
                    await FailUploadAsync(media, MediaFailureReasonEnum.VideoTooLong, token).ConfigureAwait(false);
                    TrellisKitTelemetry.Add(TrellisKitTelemetry.MediaUploads, 1, "kind", kindLabel, "mode", mode, "outcome", "rejected");
                    throw new ApiErrorException(400, "VideoTooLong", "The video is " + Math.Round(info.DurationSeconds, 1) + " seconds long; the limit is " + cap + " seconds.");
                }
            }
            TrellisKitTelemetry.Record(TrellisKitTelemetry.MediaStageDuration, verify.Elapsed.TotalSeconds, "kind", kindLabel, "stage", "verify");

            DateTime now = DateTime.UtcNow;
            media.Status = MediaStatusEnum.Processing;
            media.UpdatedUtc = now;
            bool moved = await _Database.Media.UpdateAsync(media, MediaStatusEnum.Uploading, token).ConfigureAwait(false);
            MediaAttachment current = moved ? media : (await _Database.Media.ReadAsync(media.Id, token).ConfigureAwait(false) ?? media);
            if (moved)
            {
                TrellisKitTelemetry.Add(TrellisKitTelemetry.MediaUploads, 1, "kind", kindLabel, "mode", mode, "outcome", "completed");
                TrellisKitTelemetry.Add(TrellisKitTelemetry.MediaUploadBytes, media.SizeBytes, "kind", kindLabel);
                Signal.Signal();
            }
            return new MediaCompleteOutcome { Media = await ViewAsync(current, actor, token).ConfigureAwait(false), Accepted = moved };
        }

        /// <summary>
        /// GET /api/v1/media/{id}: owner, SystemAdmin, or (for attached media) callers <see cref="AttachedVisibility"/> allows.
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="mediaId">Media identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Media.</returns>
        /// <exception cref="ApiErrorException">404 NotFound.</exception>
        public async Task<MediaView> GetAsync(ContentActor actor, string mediaId, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            MediaAttachment? media = await _Database.Media.ReadAsync(mediaId, token).ConfigureAwait(false);
            if (media == null || !await CanSeeAsync(media, actor, token).ConfigureAwait(false)) throw ApiErrorException.NotFound("Media");
            return await ViewAsync(media, actor, token).ConfigureAwait(false);
        }

        /// <summary>
        /// GET /api/v1/media?ids=: up to <see cref="MaxBatchIds"/> ids; ids the caller cannot see are omitted.
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="ids">Identifiers in request order.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Visible media in request order.</returns>
        /// <exception cref="ApiErrorException">400 ValidationFailed (no ids or more than 20).</exception>
        public async Task<MediaBatchResponse> GetManyAsync(ContentActor actor, IReadOnlyList<string> ids, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            ArgumentNullException.ThrowIfNull(ids);
            List<string> list = ids.Where(i => !String.IsNullOrWhiteSpace(i)).Select(i => i.Trim()).Distinct(StringComparer.Ordinal).ToList();
            if (list.Count == 0) throw ApiErrorException.Field("ids", "Required: comma-separated media ids.");
            if (list.Count > MaxBatchIds) throw ApiErrorException.Field("ids", "At most " + MaxBatchIds + " ids.");
            List<MediaAttachment> rows = await _Database.Media.ReadManyAsync(list, token).ConfigureAwait(false);
            Dictionary<string, MediaAttachment> byId = rows.ToDictionary(r => r.Id, StringComparer.Ordinal);
            List<MediaAttachment> visible = new List<MediaAttachment>();
            foreach (string id in list)
            {
                if (byId.TryGetValue(id, out MediaAttachment? media) && await CanSeeAsync(media, actor, token).ConfigureAwait(false)) visible.Add(media);
            }
            return new MediaBatchResponse { Items = await ViewsAsync(visible, false, actor.IsStaffFor(TargetTypeEnum.Media), ViewerKey(actor), token).ConfigureAwait(false) };
        }

        /// <summary>
        /// PUT /api/v1/media/{id}: alt text (any status, also after attachment) and optional labels and tags.
        /// </summary>
        /// <param name="actor">Caller (owner or SystemAdmin).</param>
        /// <param name="mediaId">Media identifier.</param>
        /// <param name="request">Request.</param>
        /// <param name="present">Present top-level fields (altText is only changed when present).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Media.</returns>
        /// <exception cref="ApiErrorException">400 ValidationFailed or ContentFiltered, 403 Muted, 404 NotFound.</exception>
        public async Task<MediaView> UpdateAsync(ContentActor actor, string mediaId, MediaUpdateRequest request, HashSet<string> present, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            ArgumentNullException.ThrowIfNull(request);
            ArgumentNullException.ThrowIfNull(present);
            MediaAttachment? media = await _Database.Media.ReadAsync(mediaId, token).ConfigureAwait(false);
            if (media == null || media.IsDeleted || (!actor.IsSystemAdmin && media.OwnerUserId != actor.UserId)) throw ApiErrorException.NotFound("Media");
            actor.RequireNotMuted();
            await ApplyUpdateAsync(actor, media, request, present, token).ConfigureAwait(false);
            return await ViewAsync(media, actor, token).ConfigureAwait(false);
        }

        /// <summary>
        /// DELETE /api/v1/media/{id}: the owner deletes unattached media; a SystemAdmin deletes any media (detaching
        /// it first). Every stored object, including the original, is deleted.
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="mediaId">Media identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        /// <exception cref="ApiErrorException">403 Forbidden (owner, attached media), 404 NotFound.</exception>
        public async Task DeleteAsync(ContentActor actor, string mediaId, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            if (actor.IsSystemAdmin)
            {
                await AdminDeleteAsync(mediaId, token).ConfigureAwait(false);
                return;
            }
            MediaAttachment? media = await _Database.Media.ReadAsync(mediaId, token).ConfigureAwait(false);
            if (media == null || media.IsDeleted || media.OwnerUserId != actor.UserId) throw ApiErrorException.NotFound("Media");
            if (media.IsAttached) throw ApiErrorException.Forbidden("Attached media is removed by editing the post or comment it belongs to.");
            await DeleteMediaAsync(media, token).ConfigureAwait(false);
        }

        /// <summary>
        /// POST /api/v1/media/grant: issue the media cookies for the storage mode (contract section 4.7).
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="insecureLoopback">Whether the request came over plain http to a loopback host.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Grant.</returns>
        /// <exception cref="ApiErrorException">429 RateLimited.</exception>
        /// <exception cref="IntegrationNotConfiguredException">CloudFront signing is not configured (503).</exception>
        public Task<MediaGrantResponse> GrantAsync(ContentActor actor, bool insecureLoopback, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            IMediaDeliverySigner signer = _Integrations.MediaDelivery;
            string mode = signer.Mode == MediaDeliveryModeEnum.CloudFront ? "cloudfront" : "local";
            try
            {
                _Limits.EnforceMember(actor, "media.grants", _Settings.RateLimits.MediaGrantsPerHour, RateLimitService.Hour);
                DateTime expires = DateTime.UtcNow.AddMinutes(_Settings.Media.GrantMinutes);
                expires = new DateTime(expires.Ticks - (expires.Ticks % TimeSpan.TicksPerSecond), DateTimeKind.Utc);
                List<MediaCookie> cookies = signer.CreateCookies(actor.UserId ?? "admin", expires, insecureLoopback);
                TrellisKitTelemetry.Add(TrellisKitTelemetry.MediaGrants, 1, "mode", mode, "outcome", "granted");
                return Task.FromResult(new MediaGrantResponse { Mode = signer.Mode, ExpiresUtc = expires, Cookies = cookies });
            }
            catch (ApiErrorException)
            {
                TrellisKitTelemetry.Add(TrellisKitTelemetry.MediaGrants, 1, "mode", mode, "outcome", "rate_limited");
                throw;
            }
            catch (IntegrationNotConfiguredException)
            {
                TrellisKitTelemetry.Add(TrellisKitTelemetry.MediaGrants, 1, "mode", mode, "outcome", "not_configured");
                throw;
            }
        }

        #endregion

        #region Local-Mode

        /// <summary>
        /// PUT /api/v1/media/local-uploads/{id} and .../parts/{n}: verify the signature, check the session, and stream
        /// the body to disk (contract section 4.8).
        /// </summary>
        /// <param name="mediaId">Media identifier.</param>
        /// <param name="partNumber">Part number, or null for a single upload.</param>
        /// <param name="path">Request path without query.</param>
        /// <param name="exp">exp query value.</param>
        /// <param name="sig">sig query value.</param>
        /// <param name="contentType">Request Content-Type, or null.</param>
        /// <param name="contentLength">Request Content-Length (0 when absent).</param>
        /// <param name="body">Body stream.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The stored object or part with its quoted MD5 ETag.</returns>
        /// <exception cref="ApiErrorException">403 InvalidSignature, 404 NotFound, 409 UploadIncomplete (session closed), 410 UploadExpired, 413 TooLarge, 415 UnsupportedMediaType.</exception>
        public async Task<StoredPart> ReceiveLocalUploadAsync(string mediaId, int? partNumber, string path, string? exp, string? sig, string? contentType, long contentLength, Stream body, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(body);
            LocalObjectStore? store = _Integrations.ObjectStore as LocalObjectStore;
            if (store == null) throw ApiErrorException.NotFound("Upload");
            if (!store.Signer.Verify(path, exp, sig, DateTime.UtcNow)) throw new ApiErrorException(403, "InvalidSignature", "The upload URL signature is invalid or expired. Request fresh URLs with POST /api/v1/media/{id}/upload-urls.");
            MediaAttachment? media = await _Database.Media.ReadAsync(mediaId, token).ConfigureAwait(false);
            if (media == null || media.IsDeleted) throw ApiErrorException.NotFound("Upload");
            if (media.Status != MediaStatusEnum.Uploading) throw new ApiErrorException(409, "UploadIncomplete", "The upload session is no longer open (status " + media.Status + ").");
            if (media.UploadExpiresUtc <= DateTime.UtcNow) throw new ApiErrorException(410, "UploadExpired", "The upload session expired.");
            long max;
            if (partNumber == null)
            {
                if (media.PartCount > 0) throw ApiErrorException.NotFound("Upload");
                string declared = (contentType ?? String.Empty).Split(';')[0].Trim();
                if (!String.Equals(declared, media.ContentType, StringComparison.OrdinalIgnoreCase))
                    throw new ApiErrorException(415, "UnsupportedMediaType", "Send Content-Type " + media.ContentType + " exactly as declared for the session.");
                max = media.SizeBytes;
            }
            else
            {
                if (media.PartCount == 0 || partNumber < 1 || partNumber > media.PartCount) throw ApiErrorException.NotFound("Upload part");
                max = partNumber < media.PartCount ? media.PartSizeBytes : media.SizeBytes - media.PartSizeBytes * (media.PartCount - 1);
            }
            if (contentLength > max) throw new ApiErrorException(413, "TooLarge", "The body is larger than the " + max + " bytes this upload allows.");
            try
            {
                return await store.ReceiveAsync(media.SourceLocation, media.SourceKey, partNumber, body, max, token).ConfigureAwait(false);
            }
            catch (InvalidDataException)
            {
                throw new ApiErrorException(413, "TooLarge", "The body is larger than the " + max + " bytes this upload allows.");
            }
        }

        /// <summary>
        /// Whether a caller may read media files in Local mode: a valid tk_media cookie, or a bearer access token whose
        /// session has a membership (or a SystemAdmin).
        /// </summary>
        /// <param name="cookieValue">tk_media value, or null.</param>
        /// <param name="bearerToken">Bearer token, or null.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True when allowed.</returns>
        public async Task<bool> CanReadFilesAsync(string? cookieValue, string? bearerToken, CancellationToken token = default)
        {
            if (LocalCookies.Validate(cookieValue, DateTime.UtcNow) != null) return true;
            if (String.IsNullOrWhiteSpace(bearerToken)) return false;
            SessionPrincipal? principal = await _Tokens.ValidateAccessTokenAsync(bearerToken, token).ConfigureAwait(false);
            if (principal == null) return false;
            if (principal.HasMembership || principal.IsSystemAdmin) return true;
            // Outside FullyGated a signed-in, attested account reaches member media (per-object audience is enforced
            // where media ids are handed out).
            return _Access().Model != AccessModelEnum.FullyGated && principal.AgeAttested;
        }

        /// <summary>
        /// Local path of a served media file: the images folder (media/{id}/ or public/media/{id}/), then the
        /// transcoded folder (video/{id}/ or public/video/{id}/).
        /// </summary>
        /// <param name="mediaId">Media identifier.</param>
        /// <param name="file">Flat file name.</param>
        /// <param name="publicPath">Serve the public copy.</param>
        /// <returns>Path, or null when not found, unsafe, or not in Local mode.</returns>
        public string? ResolveLocalFile(string mediaId, string file, bool publicPath)
        {
            LocalObjectStore? store = _Integrations.ObjectStore as LocalObjectStore;
            if (store == null || !MediaKeys.IsMediaId(mediaId) || !MediaKeys.IsSafeFileName(file)) return null;
            string? path = store.ResolveExisting(MediaLocationEnum.Images, (publicPath ? MediaKeys.PublicImagePrefix(PropertyId, mediaId) : MediaKeys.ImagePrefix(PropertyId, mediaId)) + file);
            return path ?? store.ResolveExisting(MediaLocationEnum.Transcoded, (publicPath ? MediaKeys.PublicVideoPrefix(PropertyId, mediaId) : MediaKeys.VideoPrefix(PropertyId, mediaId)) + file);
        }

        /// <summary>
        /// Local path of a stored link preview image.
        /// </summary>
        /// <param name="urlHash">URL hash.</param>
        /// <param name="file">File name.</param>
        /// <returns>Path, or null.</returns>
        public string? ResolveLinkPreviewFile(string urlHash, string file)
        {
            LocalObjectStore? store = _Integrations.ObjectStore as LocalObjectStore;
            if (store == null || !MediaKeys.IsSafeHash(urlHash) || !MediaKeys.IsSafeFileName(file)) return null;
            return store.ResolveExisting(MediaLocationEnum.Images, MediaKeys.LinkPreviewFile(PropertyId, urlHash, file));
        }

        #endregion

        #region Content-API

        /// <summary>
        /// Validate the media a content write wants to attach, in order. Rules: none means no check; otherwise the caller
        /// needs canPostMedia (403 MediaNotAllowed, Muted, PostingNotAllowed); more than maxCount is 400
        /// MediaLimitExceeded; each id must exist, not be deleted, belong to the caller (unless SystemAdmin), not be
        /// attached to another target (the same target is fine for edits), and not be Uploading or Failed, otherwise 409
        /// MediaNotAttachable with fieldErrors["mediaIds[i]"]; more than maxVideos videos is 400 MediaLimitExceeded; any
        /// video needs canUploadVideo (403 VideoUploadNotAllowed). Processing media is attachable.
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="mediaIds">Media ids in display order.</param>
        /// <param name="maxCount">Most attachments (feed.maxAttachments or comments.maxAttachments).</param>
        /// <param name="maxVideos">Most videos (feed.maxVideosPerPost; comments 1).</param>
        /// <param name="targetType">Target being edited, or null for a create.</param>
        /// <param name="targetId">Target being edited, or null for a create.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The media in the given order.</returns>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        /// <exception cref="ApiErrorException">400 MediaLimitExceeded, 403 MediaNotAllowed or VideoUploadNotAllowed (or Muted, PostingNotAllowed), 409 MediaNotAttachable.</exception>
        public async Task<List<MediaAttachment>> ValidateAttachableAsync(ContentActor actor, IReadOnlyList<string> mediaIds, int maxCount, int maxVideos, TargetTypeEnum? targetType, string? targetId, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            ArgumentNullException.ThrowIfNull(mediaIds);
            if (mediaIds.Count == 0) return new List<MediaAttachment>();
            actor.RequireCanPostMedia();
            if (mediaIds.Count > maxCount) throw new ApiErrorException(400, "MediaLimitExceeded", "At most " + maxCount + " attachments are allowed.");
            List<MediaAttachment> rows = await _Database.Media.ReadManyAsync(mediaIds, token).ConfigureAwait(false);
            Dictionary<string, MediaAttachment> byId = rows.ToDictionary(r => r.Id, StringComparer.Ordinal);
            Dictionary<string, string> errors = new Dictionary<string, string>();
            HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
            List<MediaAttachment> ordered = new List<MediaAttachment>();
            for (int i = 0; i < mediaIds.Count; i++)
            {
                string key = "mediaIds[" + i + "]";
                string id = mediaIds[i] ?? String.Empty;
                if (!seen.Add(id)) { errors[key] = "Listed more than once."; continue; }
                if (!byId.TryGetValue(id, out MediaAttachment? media) || media.IsDeleted) { errors[key] = "Not found."; continue; }
                if (!actor.IsSystemAdmin && media.OwnerUserId != actor.UserId) { errors[key] = "Not your media."; continue; }
                if (media.IsPrivate) { errors[key] = "Private " + media.Purpose + " media cannot be attached here."; continue; }
                if (media.IsAttached && !(media.AttachedTargetType == targetType && media.AttachedTargetId == targetId)) { errors[key] = "Already attached to other content."; continue; }
                if (media.Status == MediaStatusEnum.Uploading) { errors[key] = "The upload is not complete."; continue; }
                if (media.Status == MediaStatusEnum.Failed) { errors[key] = "Processing failed (" + media.FailureReason + ")."; continue; }
                ordered.Add(media);
            }
            if (errors.Count > 0) throw new ApiErrorException(409, "MediaNotAttachable", "One or more media items cannot be attached.", errors);
            int videos = ordered.Count(m => m.Kind == MediaKindEnum.Video);
            if (videos > maxVideos) throw new ApiErrorException(400, "MediaLimitExceeded", "At most " + maxVideos + " video" + (maxVideos == 1 ? " is" : "s are") + " allowed.");
            if (videos > 0) actor.RequireCanUploadVideo();
            return ordered;
        }

        /// <summary>
        /// Attach media to a target in order (positions 0, 1, ...). Media of the target not in the list is detached
        /// (detachedutc set; the cleanup worker deletes it after media.unattachedRetentionHours). Call
        /// <see cref="ValidateAttachableAsync"/> first.
        /// </summary>
        /// <param name="targetType">Target type.</param>
        /// <param name="targetId">Target identifier.</param>
        /// <param name="orderedIds">Media ids in display order (empty detaches everything).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The target's attachments afterwards, in order.</returns>
        /// <exception cref="ArgumentNullException">An argument is null or empty.</exception>
        /// <exception cref="ApiErrorException">409 MediaNotAttachable when another write attached one of the items first.</exception>
        public async Task<List<MediaAttachment>> AttachAsync(TargetTypeEnum targetType, string targetId, IReadOnlyList<string> orderedIds, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(targetId)) throw new ArgumentNullException(nameof(targetId));
            ArgumentNullException.ThrowIfNull(orderedIds);
            List<string> ids = orderedIds.ToList();
            List<MediaAttachment> previous = await _Database.Media.ListByTargetsAsync(targetType, new[] { targetId }, token).ConfigureAwait(false);
            List<MediaAttachment> attached = await _Database.Media.AttachAsync(targetType, targetId, ids, DateTime.UtcNow, token).ConfigureAwait(false);
            HashSet<string> got = new HashSet<string>(attached.Select(m => m.Id), StringComparer.Ordinal);
            // Media dropped from public content leaves the public paths now, not when the cleanup worker deletes it.
            foreach (MediaAttachment dropped in previous.Where(m => m.IsPublic && !got.Contains(m.Id))) await SyncMediaPublicAsync(dropped.Id, false, token).ConfigureAwait(false);
            Dictionary<string, string> errors = new Dictionary<string, string>();
            for (int i = 0; i < ids.Count; i++) if (!got.Contains(ids[i])) errors["mediaIds[" + i + "]"] = "Already attached to other content.";
            if (errors.Count > 0) throw new ApiErrorException(409, "MediaNotAttachable", "One or more media items cannot be attached.", errors);
            return attached;
        }

        /// <summary>
        /// Detach media from a target (detachedutc set). The cleanup worker deletes detached media after
        /// media.unattachedRetentionHours.
        /// </summary>
        /// <param name="targetType">Target type.</param>
        /// <param name="targetId">Target identifier.</param>
        /// <param name="mediaIds">Only these, or null for all.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Media detached.</returns>
        /// <exception cref="ArgumentNullException">The target identifier is null or empty.</exception>
        public Task<int> DetachAsync(TargetTypeEnum targetType, string targetId, IEnumerable<string>? mediaIds, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(targetId)) throw new ArgumentNullException(nameof(targetId));
            return _Database.Media.DetachAsync(targetType, targetId, mediaIds, DateTime.UtcNow, token);
        }

        /// <summary>
        /// Validate the private media a write wants to attach (ticket or direct message attachments), in order: each id must
        /// exist, not be deleted, belong to the caller, have this purpose, be pending or already in this scope, not be
        /// attached elsewhere (the same target is fine), and not be Uploading or Failed, otherwise 409 MediaNotAttachable
        /// with fieldErrors["mediaIds[i]"]; more than maxCount is 400 MediaLimitExceeded; more than maxVideos videos is 400
        /// MediaLimitExceeded. Posting permissions are checked only when the purpose requires them.
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="mediaIds">Media ids in display order.</param>
        /// <param name="purpose">Private purpose.</param>
        /// <param name="scopeId">Scope the media will belong to, or null when the scope does not exist yet (a new ticket).</param>
        /// <param name="maxCount">Most attachments.</param>
        /// <param name="maxVideos">Most videos.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The media in the given order.</returns>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        /// <exception cref="ApiErrorException">400 MediaLimitExceeded, 403 MediaNotAllowed, 409 MediaNotAttachable.</exception>
        public async Task<List<MediaAttachment>> ValidatePrivateAttachableAsync(ContentActor actor, IReadOnlyList<string> mediaIds, MediaPurposeEnum purpose, string? scopeId, int maxCount, int maxVideos, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            ArgumentNullException.ThrowIfNull(mediaIds);
            if (mediaIds.Count == 0) return new List<MediaAttachment>();
            PrivateMediaScope scope = Scope(purpose) ?? throw new ApiErrorException(403, "MediaNotAllowed", "Attachments are not available here.");
            if (scope.RequirePostingPermissions) actor.RequireCanPostMedia();
            if (mediaIds.Count > maxCount) throw new ApiErrorException(400, "MediaLimitExceeded", "At most " + maxCount + " attachments are allowed.");
            List<MediaAttachment> rows = await _Database.Media.ReadManyAsync(mediaIds, token).ConfigureAwait(false);
            Dictionary<string, MediaAttachment> byId = rows.ToDictionary(r => r.Id, StringComparer.Ordinal);
            Dictionary<string, string> errors = new Dictionary<string, string>();
            HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
            List<MediaAttachment> ordered = new List<MediaAttachment>();
            for (int i = 0; i < mediaIds.Count; i++)
            {
                string key = "mediaIds[" + i + "]";
                string id = mediaIds[i] ?? String.Empty;
                if (!seen.Add(id)) { errors[key] = "Listed more than once."; continue; }
                if (!byId.TryGetValue(id, out MediaAttachment? media) || media.IsDeleted) { errors[key] = "Not found."; continue; }
                if (media.OwnerUserId != actor.UserId) { errors[key] = "Not your media."; continue; }
                if (media.Purpose != purpose) { errors[key] = "Upload it with purpose " + purpose + "."; continue; }
                if (media.ScopeId != null && media.ScopeId != scopeId) { errors[key] = "Uploaded for another " + purpose.ToString().ToLowerInvariant() + "."; continue; }
                if (media.IsAttached) { errors[key] = "Already attached."; continue; }
                if (media.Status == MediaStatusEnum.Uploading) { errors[key] = "The upload is not complete."; continue; }
                if (media.Status == MediaStatusEnum.Failed) { errors[key] = "Processing failed (" + media.FailureReason + ")."; continue; }
                ordered.Add(media);
            }
            if (errors.Count > 0) throw new ApiErrorException(409, "MediaNotAttachable", "One or more media items cannot be attached.", errors);
            int videos = ordered.Count(m => m.Kind == MediaKindEnum.Video);
            if (videos > maxVideos) throw new ApiErrorException(400, "MediaLimitExceeded", "At most " + maxVideos + " video" + (maxVideos == 1 ? " is" : "s are") + " allowed.");
            return ordered;
        }

        /// <summary>
        /// Bind validated private media to its scope and attach it to a target (for example a ticket message) in order.
        /// Call <see cref="ValidatePrivateAttachableAsync"/> first.
        /// </summary>
        /// <param name="purpose">Private purpose.</param>
        /// <param name="scopeId">Scope (ticket or conversation identifier).</param>
        /// <param name="targetType">Target type.</param>
        /// <param name="targetId">Target identifier.</param>
        /// <param name="orderedIds">Media ids in display order.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The target's attachments afterwards.</returns>
        /// <exception cref="ArgumentNullException">An argument is null or empty.</exception>
        /// <exception cref="ApiErrorException">409 MediaNotAttachable when another write took one of the items first.</exception>
        public async Task<List<MediaAttachment>> AttachPrivateAsync(MediaPurposeEnum purpose, string scopeId, TargetTypeEnum targetType, string targetId, IReadOnlyList<string> orderedIds, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(scopeId)) throw new ArgumentNullException(nameof(scopeId));
            ArgumentNullException.ThrowIfNull(orderedIds);
            if (orderedIds.Count == 0) return new List<MediaAttachment>();
            int bound = await _Database.Media.BindScopeAsync(orderedIds, purpose, scopeId, DateTime.UtcNow, token).ConfigureAwait(false);
            if (bound != orderedIds.Distinct(StringComparer.Ordinal).Count()) throw new ApiErrorException(409, "MediaNotAttachable", "One or more media items belong to another " + purpose.ToString().ToLowerInvariant() + ".");
            return await AttachAsync(targetType, targetId, orderedIds, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Media payloads of private attachments of many targets for one viewer, with per-object URLs valid for
        /// messaging.mediaUrlMinutes, keyed by target id in attachment order.
        /// </summary>
        /// <param name="targetType">Target type (for example TicketMessage).</param>
        /// <param name="targetIds">Target identifiers.</param>
        /// <param name="viewer">Viewer (the URLs work only for this viewer while the scope's access rules allow them).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Views by target id.</returns>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        public async Task<Dictionary<string, List<MediaView>>> ListPrivateViewsForTargetsAsync(TargetTypeEnum targetType, IEnumerable<string> targetIds, ContentActor viewer, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(targetIds);
            ArgumentNullException.ThrowIfNull(viewer);
            List<MediaAttachment> rows = (await _Database.Media.ListByTargetsAsync(targetType, targetIds, token).ConfigureAwait(false)).Where(m => m.IsPrivate).ToList();
            List<MediaView> views = await ViewsAsync(rows, false, viewer.IsStaffFor(TargetTypeEnum.Media), ViewerKey(viewer), token).ConfigureAwait(false);
            Dictionary<string, List<MediaView>> result = new Dictionary<string, List<MediaView>>(StringComparer.Ordinal);
            for (int i = 0; i < rows.Count; i++)
            {
                string target = rows[i].AttachedTargetId!;
                if (!result.TryGetValue(target, out List<MediaView>? list))
                {
                    list = new List<MediaView>();
                    result[target] = list;
                }
                list.Add(views[i]);
            }
            return result;
        }

        /// <summary>
        /// Resolve a private file URL (GET /api/v1/media/private/{token}/{file}): the token must be valid, issued in this
        /// property, unexpired, and name a live private media item with the same purpose and scope; the viewer must still
        /// pass the purpose's access rules (re-checked on every request) or own a pending upload. Files are looked up in the
        /// private prefix of the source location (and, during the moment between processing and the move, the served
        /// locations).
        /// </summary>
        /// <param name="tokenValue">Token path segment.</param>
        /// <param name="file">Flat file name.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The object to stream.</returns>
        /// <exception cref="ApiErrorException">403 InvalidSignature (forged, foreign, or expired URL, or a viewer without access), 404 NotFound.</exception>
        public async Task<PrivateMediaFile> ResolvePrivateFileAsync(string tokenValue, string file, CancellationToken token = default)
        {
            PrivateMediaGrant? grant = PrivateTokens.Read(tokenValue);
            if (grant == null || !String.Equals(grant.PropertyId, PropertyId, StringComparison.Ordinal)) throw new ApiErrorException(403, "InvalidSignature", "This media URL is not valid.");
            if (grant.ExpiresUtc <= DateTime.UtcNow) throw new ApiErrorException(403, "InvalidSignature", "This media URL expired; reload the message or ticket for a fresh one.");
            if (!MediaKeys.IsMediaId(grant.MediaId) || !MediaKeys.IsSafeFileName(file)) throw ApiErrorException.NotFound("File");
            MediaAttachment? media = await _Database.Media.ReadAsync(grant.MediaId, token).ConfigureAwait(false);
            if (media == null || media.IsDeleted || media.Purpose != grant.Purpose || (media.ScopeId ?? String.Empty) != grant.ScopeId || media.Status != MediaStatusEnum.Ready || media.IsRetained)
                throw ApiErrorException.NotFound("File");
            if (!await ViewerAllowedAsync(media, grant.ViewerKey, token).ConfigureAwait(false)) throw new ApiErrorException(403, "InvalidSignature", "You no longer have access to this media.");
            IObjectStore store = _Integrations.ObjectStore;
            List<KeyValuePair<MediaLocationEnum, string>> candidates = new List<KeyValuePair<MediaLocationEnum, string>>
            {
                new KeyValuePair<MediaLocationEnum, string>(MediaLocationEnum.Source, MediaKeys.PrivateImagePrefix(PropertyId, media.Id) + file),
                new KeyValuePair<MediaLocationEnum, string>(MediaLocationEnum.Source, MediaKeys.PrivateVideoPrefix(PropertyId, media.Id) + file),
                new KeyValuePair<MediaLocationEnum, string>(MediaLocationEnum.Images, MediaKeys.ImagePrefix(PropertyId, media.Id) + file),
                new KeyValuePair<MediaLocationEnum, string>(MediaLocationEnum.Transcoded, MediaKeys.VideoPrefix(PropertyId, media.Id) + file)
            };
            foreach (KeyValuePair<MediaLocationEnum, string> candidate in candidates)
            {
                ObjectHead? head = await store.HeadAsync(candidate.Key, candidate.Value, token).ConfigureAwait(false);
                if (head == null) continue;
                return new PrivateMediaFile { Location = candidate.Key, Key = candidate.Value, SizeBytes = head.SizeBytes, ContentType = MediaKeys.ContentTypeFor(file), ExpiresUtc = grant.ExpiresUtc };
            }
            throw ApiErrorException.NotFound("File");
        }

        /// <summary>Resolve a processed gallery output after the caller has checked page visibility. Originals are never served.</summary>
        public async Task<PrivateMediaFile> ResolveGalleryFileAsync(string mediaId, string file, CancellationToken token = default)
        {
            if (!MediaKeys.IsMediaId(mediaId) || !MediaKeys.IsSafeFileName(file)) throw ApiErrorException.NotFound("File");
            foreach (MediaLocationEnum location in new[] { MediaLocationEnum.Images, MediaLocationEnum.Transcoded })
            {
                string key = (location == MediaLocationEnum.Images ? MediaKeys.ImagePrefix(PropertyId, mediaId) : MediaKeys.VideoPrefix(PropertyId, mediaId)) + file;
                ObjectHead? head = await _Integrations.ObjectStore.HeadAsync(location, key, token).ConfigureAwait(false);
                if (head != null) return new PrivateMediaFile { Location = location, Key = key, SizeBytes = head.SizeBytes, ContentType = MediaKeys.ContentTypeFor(file), ExpiresUtc = DateTime.UtcNow };
            }
            throw ApiErrorException.NotFound("File");
        }

        /// <summary>
        /// Read a byte range of a resolved private file.
        /// </summary>
        /// <param name="file">Resolved file.</param>
        /// <param name="offset">Start offset.</param>
        /// <param name="length">Bytes (at most 4 MiB per call).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Bytes.</returns>
        /// <exception cref="ArgumentNullException">The file is null.</exception>
        public Task<byte[]> ReadPrivateRangeAsync(PrivateMediaFile file, long offset, int length, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(file);
            return _Integrations.ObjectStore.ReadRangeAsync(file.Location, file.Key, offset, Math.Clamp(length, 1, 4 * 1024 * 1024), token);
        }

        /// <summary>
        /// Throw 404 unless the media exists, belongs to the caller, and has the purpose (the ticket upload routes use it so
        /// they only ever touch the caller's own private uploads).
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="mediaId">Media identifier.</param>
        /// <param name="purpose">Required purpose.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        /// <exception cref="ArgumentNullException">The actor is null.</exception>
        /// <exception cref="ApiErrorException">404 NotFound.</exception>
        public async Task RequirePurposeAsync(ContentActor actor, string mediaId, MediaPurposeEnum purpose, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            MediaAttachment? media = await _Database.Media.ReadAsync(mediaId, token).ConfigureAwait(false);
            if (media == null || media.IsDeleted || media.Purpose != purpose || String.IsNullOrEmpty(actor.UserId) || media.OwnerUserId != actor.UserId) throw ApiErrorException.NotFound("Media");
        }

        /// <summary>
        /// Whether a media item is private (the grant cookie file route refuses private media).
        /// </summary>
        /// <param name="mediaId">Media identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True when the media exists and has a private purpose.</returns>
        public async Task<bool> IsPrivateAsync(string mediaId, CancellationToken token = default)
        {
            if (!MediaKeys.IsMediaId(mediaId)) return false;
            MediaAttachment? media = await _Database.Media.ReadAsync(mediaId, token).ConfigureAwait(false);
            return media != null && media.IsPrivate;
        }

        /// <summary>
        /// Media payloads of many targets in one set of queries (one for media, one for variants, two for metadata),
        /// keyed by target id in attachment order. Targets without media are absent.
        /// </summary>
        /// <param name="targetType">Target type.</param>
        /// <param name="targetIds">Target identifiers (a page of content).</param>
        /// <param name="publicPaths">Use the public copy paths for media copied there (public feed).</param>
        /// <param name="includeStaffMetadata">Include staff: labels and tags.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Views by target id.</returns>
        /// <exception cref="ArgumentNullException">The ids are null.</exception>
        public async Task<Dictionary<string, List<MediaView>>> ListViewsForTargetsAsync(TargetTypeEnum targetType, IEnumerable<string> targetIds, bool publicPaths, bool includeStaffMetadata, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(targetIds);
            List<MediaAttachment> rows = await _Database.Media.ListByTargetsAsync(targetType, targetIds, token).ConfigureAwait(false);
            List<MediaView> views = await ViewsAsync(rows, publicPaths, includeStaffMetadata, null, token).ConfigureAwait(false);
            Dictionary<string, List<MediaView>> result = new Dictionary<string, List<MediaView>>(StringComparer.Ordinal);
            for (int i = 0; i < rows.Count; i++)
            {
                string target = rows[i].AttachedTargetId!;
                if (!result.TryGetValue(target, out List<MediaView>? list))
                {
                    list = new List<MediaView>();
                    result[target] = list;
                }
                list.Add(views[i]);
            }
            return result;
        }

        /// <summary>
        /// Processing state of a target's attachments.
        /// </summary>
        /// <param name="targetType">Target type.</param>
        /// <param name="targetId">Target identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Readiness (AllReady true when there are no attachments).</returns>
        public async Task<MediaReadiness> GetReadinessAsync(TargetTypeEnum targetType, string targetId, CancellationToken token = default)
        {
            List<MediaAttachment> rows = await _Database.Media.ListByTargetsAsync(targetType, new[] { targetId }, token).ConfigureAwait(false);
            return new MediaReadiness
            {
                Count = rows.Count,
                AllReady = rows.All(m => m.Status == MediaStatusEnum.Ready),
                AnyFailed = rows.Any(m => m.Status == MediaStatusEnum.Failed)
            };
        }

        #endregion

        #region Moderation-API

        /// <summary>
        /// Moderator Remove: move the served objects (variants, posters, HLS, MP4) of a target's media to
        /// retained/removed/{id}/ and delete their public copies (isretained true, ispublic false). Originals stay.
        /// </summary>
        /// <param name="targetType">Target type.</param>
        /// <param name="targetId">Target identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Media retained.</returns>
        public async Task<int> RetainTargetAsync(TargetTypeEnum targetType, string targetId, CancellationToken token = default)
        {
            int count = 0;
            foreach (MediaAttachment media in await _Database.Media.ListByTargetsAsync(targetType, new[] { targetId }, token).ConfigureAwait(false))
            {
                if (media.IsRetained) continue;
                await Storage.MoveToRetainedAsync(media, token).ConfigureAwait(false);
                media.IsRetained = true;
                media.IsPublic = false;
                media.UpdatedUtc = DateTime.UtcNow;
                await _Database.Media.UpdateAsync(media, null, token).ConfigureAwait(false);
                count++;
            }
            return count;
        }

        /// <summary>
        /// Moderator Restore: move retained objects back to the served locations (isretained false). Public copies are
        /// not recreated; call <see cref="PublishTargetAsync"/> again when the content is still public.
        /// </summary>
        /// <param name="targetType">Target type.</param>
        /// <param name="targetId">Target identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Media restored.</returns>
        public async Task<int> RestoreTargetAsync(TargetTypeEnum targetType, string targetId, CancellationToken token = default)
        {
            int count = 0;
            foreach (MediaAttachment media in await _Database.Media.ListByTargetsAsync(targetType, new[] { targetId }, token).ConfigureAwait(false))
            {
                if (!media.IsRetained) continue;
                await Storage.RestoreFromRetainedAsync(media, token).ConfigureAwait(false);
                media.IsRetained = false;
                media.UpdatedUtc = DateTime.UtcNow;
                await _Database.Media.UpdateAsync(media, null, token).ConfigureAwait(false);
                count++;
            }
            return count;
        }

        /// <summary>
        /// Copy a target's Ready, non-retained media to the public paths (ispublic true), for Public posts.
        /// </summary>
        /// <param name="targetType">Target type.</param>
        /// <param name="targetId">Target identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Media published.</returns>
        public async Task<int> PublishTargetAsync(TargetTypeEnum targetType, string targetId, CancellationToken token = default)
        {
            int count = 0;
            foreach (MediaAttachment media in await _Database.Media.ListByTargetsAsync(targetType, new[] { targetId }, token).ConfigureAwait(false))
            {
                if (media.Status != MediaStatusEnum.Ready || media.IsRetained) continue;
                await Storage.CopyToPublicAsync(media, token).ConfigureAwait(false);
                if (!media.IsPublic)
                {
                    media.IsPublic = true;
                    media.UpdatedUtc = DateTime.UtcNow;
                    await _Database.Media.UpdateAsync(media, null, token).ConfigureAwait(false);
                }
                count++;
            }
            return count;
        }

        /// <summary>
        /// Delete the public copies of a target's media (ispublic false).
        /// </summary>
        /// <param name="targetType">Target type.</param>
        /// <param name="targetId">Target identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Media unpublished.</returns>
        public async Task<int> UnpublishTargetAsync(TargetTypeEnum targetType, string targetId, CancellationToken token = default)
        {
            int count = 0;
            foreach (MediaAttachment media in await _Database.Media.ListByTargetsAsync(targetType, new[] { targetId }, token).ConfigureAwait(false))
            {
                await Storage.DeletePublicAsync(media, token).ConfigureAwait(false);
                if (!media.IsPublic) continue;
                media.IsPublic = false;
                media.UpdatedUtc = DateTime.UtcNow;
                await _Database.Media.UpdateAsync(media, null, token).ConfigureAwait(false);
                count++;
            }
            return count;
        }

        /// <summary>
        /// Publish (copy to the public paths) or withdraw a target's media, depending on whether the content is visible to
        /// signed-out visitors (for example a Public, published event or venue). Only media of content the caller decided
        /// is public is ever copied, so members-only media never reaches the public paths.
        /// </summary>
        /// <param name="targetType">Target type.</param>
        /// <param name="targetId">Target identifier.</param>
        /// <param name="shouldBePublic">Whether the content is public now.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Media published or withdrawn.</returns>
        public Task<int> SyncTargetPublicAsync(TargetTypeEnum targetType, string targetId, bool shouldBePublic, CancellationToken token = default)
        {
            return shouldBePublic ? PublishTargetAsync(targetType, targetId, token) : UnpublishTargetAsync(targetType, targetId, token);
        }

        /// <summary>
        /// Publish or withdraw one media item regardless of what it is attached to (an event cover shared by the
        /// occurrences of a recurring series). Media that is not Ready, retained, deleted, or private is never published.
        /// </summary>
        /// <param name="mediaId">Media identifier.</param>
        /// <param name="shouldBePublic">Whether some public content uses it now.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True when the public copy now exists.</returns>
        public async Task<bool> SyncMediaPublicAsync(string mediaId, bool shouldBePublic, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(mediaId)) return false;
            MediaAttachment? media = await _Database.Media.ReadAsync(mediaId, token).ConfigureAwait(false);
            if (media == null) return false;
            bool publish = shouldBePublic && media.Status == MediaStatusEnum.Ready && !media.IsRetained && !media.IsDeleted && !media.IsPrivate;
            if (publish)
            {
                await Storage.CopyToPublicAsync(media, token).ConfigureAwait(false);
                if (!media.IsPublic)
                {
                    media.IsPublic = true;
                    media.UpdatedUtc = DateTime.UtcNow;
                    await _Database.Media.UpdateAsync(media, null, token).ConfigureAwait(false);
                }
                return true;
            }
            await Storage.DeletePublicAsync(media, token).ConfigureAwait(false);
            if (media.IsPublic)
            {
                media.IsPublic = false;
                media.UpdatedUtc = DateTime.UtcNow;
                await _Database.Media.UpdateAsync(media, null, token).ConfigureAwait(false);
            }
            return false;
        }

        /// <summary>
        /// Purge: delete every object of a target's media, including originals, and mark the media deleted.
        /// </summary>
        /// <param name="targetType">Target type.</param>
        /// <param name="targetId">Target identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Media deleted.</returns>
        public async Task<int> PurgeTargetAsync(TargetTypeEnum targetType, string targetId, CancellationToken token = default)
        {
            int count = 0;
            foreach (MediaAttachment media in await _Database.Media.ListByTargetsAsync(targetType, new[] { targetId }, token).ConfigureAwait(false))
            {
                await DeleteMediaAsync(media, token).ConfigureAwait(false);
                count++;
            }
            return count;
        }

        /// <summary>
        /// Account deletion: delete a member's unattached media (attached media follows its content).
        /// </summary>
        /// <param name="userId">Member identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Media deleted.</returns>
        public Task<int> DeleteUnattachedForUserAsync(string userId, CancellationToken token = default)
        {
            return DeleteForUserAsync(userId, true, token);
        }

        /// <summary>
        /// Delete all of a member's media, attached or not, with every object including originals.
        /// </summary>
        /// <param name="userId">Member identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Media deleted.</returns>
        public Task<int> DeleteAllForUserAsync(string userId, CancellationToken token = default)
        {
            return DeleteForUserAsync(userId, false, token);
        }

        /// <summary>
        /// Delete one media item: every stored object (original included), then the row is marked deleted with its
        /// variants, labels, and tags removed in the same transaction.
        /// </summary>
        /// <param name="media">Media.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        /// <exception cref="ArgumentNullException">The media is null.</exception>
        public async Task DeleteMediaAsync(MediaAttachment media, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(media);
            await Storage.DeleteAllAsync(media, token).ConfigureAwait(false);
            await _Database.Media.MarkDeletedAsync(media.Id, DateTime.UtcNow, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Expire a stale upload session: abort or delete the partial upload and fail it with UploadExpired.
        /// </summary>
        /// <param name="media">Uploading media.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True when this call expired it.</returns>
        /// <exception cref="ArgumentNullException">The media is null.</exception>
        public async Task<bool> ExpireUploadAsync(MediaAttachment media, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(media);
            IObjectStore store = _Integrations.ObjectStore;
            if (!String.IsNullOrEmpty(media.MultipartUploadId)) await store.AbortMultipartAsync(media.SourceLocation, media.SourceKey, media.MultipartUploadId, token).ConfigureAwait(false);
            await store.DeleteAsync(media.SourceLocation, media.SourceKey, token).ConfigureAwait(false);
            media.MultipartUploadId = null;
            bool changed = await FailUploadAsync(media, MediaFailureReasonEnum.UploadExpired, token).ConfigureAwait(false);
            if (changed) TrellisKitTelemetry.Add(TrellisKitTelemetry.MediaUploads, 1, "kind", KindLabel(media.Kind), "mode", media.PartCount > 0 ? "multipart" : "single", "outcome", "expired");
            return changed;
        }

        #endregion

        #region Admin

        /// <summary>
        /// Admin media library enumeration (collection <see cref="MediaCollections.AdminLibrary"/>).
        /// </summary>
        /// <param name="query">Query (filters kind, status, ownerUserId, attached).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Page of items.</returns>
        /// <exception cref="ApiErrorException">400 ValidationFailed or InvalidContinuationToken.</exception>
        public async Task<EnumerationResult<AdminMediaItem>> EnumerateAdminAsync(EnumerationQuery? query, CancellationToken token = default)
        {
            EnumerationRequest request = _Enumeration.Prepare(query, MediaCollections.AdminLibrary, true);
            Dictionary<string, string> errors = new Dictionary<string, string>();
            MediaFilter filter = new MediaFilter
            {
                Kind = query?.FilterEnum<MediaKindEnum>("kind", errors),
                Status = query?.FilterEnum<MediaStatusEnum>("status", errors),
                OwnerUserId = query?.FilterString("ownerUserId"),
                Attached = query?.FilterBool("attached", errors)
            };
            if (errors.Count > 0) throw ApiErrorException.Validation(errors);
            EnumerationResult<MediaAttachment> page = await _Database.Media.EnumerateAsync(request, filter, token).ConfigureAwait(false);
            List<AdminMediaItem> items = await AdminItemsAsync(page.Objects, token).ConfigureAwait(false);
            return page.With(items);
        }

        /// <summary>
        /// Admin library totals.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Summary.</returns>
        public Task<MediaSummary> SummaryAsync(CancellationToken token = default)
        {
            return _Database.Media.SummaryAsync(DateTime.UtcNow, token);
        }

        /// <summary>
        /// Admin media detail.
        /// </summary>
        /// <param name="mediaId">Media identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Detail.</returns>
        /// <exception cref="ApiErrorException">404 NotFound.</exception>
        public async Task<AdminMediaDetail> AdminDetailAsync(string mediaId, CancellationToken token = default)
        {
            MediaAttachment? media = await _Database.Media.ReadAsync(mediaId, token).ConfigureAwait(false);
            if (media == null || media.IsDeleted) throw ApiErrorException.NotFound("Media");
            AdminMediaItem item = (await AdminItemsAsync(new List<MediaAttachment> { media }, token).ConfigureAwait(false))[0];
            List<MediaVariant> variants = await _Database.Media.ReadVariantsAsync(media.Id, token).ConfigureAwait(false);
            return new AdminMediaDetail
            {
                Media = item.Media,
                Owner = item.Owner,
                AttachedTo = item.AttachedTo,
                StoredBytes = item.StoredBytes,
                FileName = item.FileName,
                CreatedUtc = item.CreatedUtc,
                Processor = media.Processor,
                ProcessorJobId = media.ProcessorJobId,
                ProcessingStartedUtc = media.ProcessingStartedUtc,
                Attempts = media.Attempts,
                Variants = variants.Select(v => new AdminMediaVariantView { Name = v.Name, Format = v.Format, Width = v.Width, Height = v.Height, SizeBytes = v.SizeBytes }).ToList()
            };
        }

        /// <summary>
        /// PUT /api/v1/admin/media/{id}: alt text and optional labels and tags.
        /// </summary>
        /// <param name="actor">Caller (SystemAdmin or break-glass).</param>
        /// <param name="mediaId">Media identifier.</param>
        /// <param name="request">Request.</param>
        /// <param name="present">Present top-level fields.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Detail.</returns>
        /// <exception cref="ApiErrorException">400 ValidationFailed, 404 NotFound.</exception>
        public async Task<AdminMediaDetail> AdminUpdateAsync(ContentActor actor, string mediaId, MediaUpdateRequest request, HashSet<string> present, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            ArgumentNullException.ThrowIfNull(request);
            ArgumentNullException.ThrowIfNull(present);
            MediaAttachment? media = await _Database.Media.ReadAsync(mediaId, token).ConfigureAwait(false);
            if (media == null || media.IsDeleted) throw ApiErrorException.NotFound("Media");
            await ApplyUpdateAsync(actor, media, request, present, token).ConfigureAwait(false);
            return await AdminDetailAsync(mediaId, token).ConfigureAwait(false);
        }

        /// <summary>
        /// POST /api/v1/admin/media/{id}/reprocess: a Failed item whose original still exists goes back to Processing.
        /// </summary>
        /// <param name="mediaId">Media identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Detail.</returns>
        /// <exception cref="ApiErrorException">404 NotFound, 409 NotReprocessable.</exception>
        public async Task<AdminMediaDetail> ReprocessAsync(string mediaId, CancellationToken token = default)
        {
            MediaAttachment? media = await _Database.Media.ReadAsync(mediaId, token).ConfigureAwait(false);
            if (media == null || media.IsDeleted) throw ApiErrorException.NotFound("Media");
            if (media.Status != MediaStatusEnum.Failed) throw new ApiErrorException(409, "NotReprocessable", "Only Failed media can be reprocessed (status " + media.Status + ").");
            ObjectHead? head = await _Integrations.ObjectStore.HeadAsync(media.SourceLocation, media.SourceKey, token).ConfigureAwait(false);
            if (head == null) throw new ApiErrorException(409, "NotReprocessable", "The original upload no longer exists.");
            await Storage.DeleteOutputsAsync(media, token).ConfigureAwait(false);
            await _Database.Media.DeleteVariantsAsync(media.Id, token).ConfigureAwait(false);
            media.Status = MediaStatusEnum.Processing;
            media.FailureReason = null;
            media.Attempts = 0;
            media.ProcessorJobId = null;
            media.Processor = null;
            media.ClaimedBy = null;
            media.ClaimUntilUtc = null;
            media.ProcessingStartedUtc = null;
            media.ReadyUtc = null;
            media.Width = null;
            media.Height = null;
            media.Blurhash = null;
            media.HlsManifestFile = null;
            media.Mp4File = null;
            media.PosterFile = null;
            media.PosterThumbFile = null;
            media.UpdatedUtc = DateTime.UtcNow;
            if (!await _Database.Media.UpdateAsync(media, MediaStatusEnum.Failed, token).ConfigureAwait(false)) throw new ApiErrorException(409, "NotReprocessable", "The media changed; reload it.");
            Signal.Signal();
            return await AdminDetailAsync(mediaId, token).ConfigureAwait(false);
        }

        /// <summary>
        /// DELETE /api/v1/admin/media/{id}: detach from its target (the content keeps its other attachments), delete
        /// every object, then notify the detached handlers.
        /// </summary>
        /// <param name="mediaId">Media identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        /// <exception cref="ApiErrorException">404 NotFound.</exception>
        public async Task AdminDeleteAsync(string mediaId, CancellationToken token = default)
        {
            MediaAttachment? media = await _Database.Media.ReadAsync(mediaId, token).ConfigureAwait(false);
            if (media == null || media.IsDeleted) throw ApiErrorException.NotFound("Media");
            TargetTypeEnum? targetType = media.AttachedTargetType;
            string? targetId = media.AttachedTargetId;
            if (targetType != null && targetId != null) await _Database.Media.DetachAsync(targetType.Value, targetId, new[] { media.Id }, DateTime.UtcNow, token).ConfigureAwait(false);
            await DeleteMediaAsync(media, token).ConfigureAwait(false);
            if (targetType == null || targetId == null) return;
            List<Func<TargetTypeEnum, string, CancellationToken, Task>> handlers;
            lock (_HandlerLock) handlers = _DetachedHandlers.ToList();
            foreach (Func<TargetTypeEnum, string, CancellationToken, Task> handler in handlers)
            {
                try
                {
                    await handler(targetType.Value, targetId, token).ConfigureAwait(false);
                }
                catch (Exception e) when (e is not OperationCanceledException)
                {
                    _Logger.Error("[MediaService] detached handler for " + targetType + " " + targetId + " failed", e);
                }
            }
        }

        #endregion

        #region Link-Previews

        /// <inheritdoc />
        public async Task<StoredPreviewImage?> StoreLinkPreviewImageAsync(string urlHash, byte[] data, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(urlHash)) throw new ArgumentNullException(nameof(urlHash));
            ArgumentNullException.ThrowIfNull(data);
            if (!MediaKeys.IsSafeHash(urlHash)) throw new ArgumentException("The URL hash must be 8 to 128 letters, digits, dashes, or underscores.", nameof(urlHash));
            string? type = MediaSniffer.Detect(data.Length >= MediaSniffer.HeaderLength ? data.AsSpan(0, MediaSniffer.HeaderLength).ToArray() : data);
            if (type == null || !type.StartsWith("image/", StringComparison.Ordinal)) return null;
            IImageProcessor processor = _Integrations.ImageProcessor;
            EncodedImage encoded;
            try
            {
                using (IDecodedImage decoded = processor.Decode(data, _Settings.Media.MaxImagePixels))
                using (IDecodedImage resized = processor.Resize(decoded, LinkPreviewLongEdge))
                {
                    encoded = processor.EncodeJpeg(resized, 85);
                }
            }
            catch (MediaProcessingException)
            {
                return null;
            }
            string key = MediaKeys.LinkPreview(PropertyId, urlHash);
            await _Integrations.ObjectStore.PutAsync(MediaLocationEnum.Images, key, encoded.Data, "image/jpeg", token).ConfigureAwait(false);
            return new StoredPreviewImage { Key = key, Width = encoded.Width, Height = encoded.Height };
        }

        /// <inheritdoc />
        public string? LinkPreviewImageUrl(string? key)
        {
            return Urls.LinkPreviewUrl(key);
        }

        #endregion

        /// <summary>
        /// Build one payload for a caller (metadata with staff entries when the caller is staff for Media).
        /// </summary>
        /// <param name="media">Media.</param>
        /// <param name="actor">Caller.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Payload.</returns>
        public async Task<MediaView> ViewAsync(MediaAttachment media, ContentActor actor, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(media);
            ArgumentNullException.ThrowIfNull(actor);
            // Signed-out callers cannot read the member media paths, so they get the public copy when one exists (only
            // media of public content is ever copied there); without one the URL stays on the member path.
            bool signedOut = actor.IsAnonymous || String.IsNullOrEmpty(actor.UserId);
            List<MediaView> views = await ViewsAsync(new List<MediaAttachment> { media }, signedOut, actor.IsStaffFor(TargetTypeEnum.Media), ViewerKey(actor), token).ConfigureAwait(false);
            return signedOut ? MediaUrlBuilder.ForPublic(views[0]) : views[0];
        }

        /// <summary>
        /// Part size for a multipart session: max(partMegabytes MiB, ceil(size / 1000)) rounded up to a whole MiB.
        /// </summary>
        /// <param name="sizeBytes">Declared size.</param>
        /// <param name="partMegabytes">media.multipartPartMegabytes.</param>
        /// <returns>Part size in bytes.</returns>
        public static long PartSize(long sizeBytes, int partMegabytes)
        {
            long minimum = partMegabytes * _MiB;
            long forCount = (sizeBytes + 999) / 1000;
            long size = Math.Max(minimum, forCount);
            return (size + _MiB - 1) / _MiB * _MiB;
        }

        #endregion

        #region Private-Methods

        private static string KindLabel(MediaKindEnum kind)
        {
            return kind == MediaKindEnum.Image ? "image" : kind == MediaKindEnum.Document ? "document" : "video";
        }

        private static MediaKindEnum? ParseKind(string? raw)
        {
            if (String.IsNullOrWhiteSpace(raw) || Int32.TryParse(raw, out int _)) return null;
            if (Enum.TryParse(raw.Trim(), true, out MediaKindEnum kind) && Enum.IsDefined(kind)) return kind;
            return null;
        }

        private long MaxBytes(MediaKindEnum kind, ContentActor actor)
        {
            MediaSettings media = _Settings.Media;
            if (kind == MediaKindEnum.Image) return media.MaxImageBytes;
            if (kind == MediaKindEnum.Document) return media.MaxDocumentBytes;
            // Library editors upload lesson media with the admin limits (resolved decision 3).
            if (actor.IsSystemAdmin || actor.IsLibraryEditor) return media.MaxAdminVideoMegabytes * _MiB;
            return media.MaxMemberVideoMegabytes * _MiB;
        }

        private int DurationCapSeconds(ContentActor actor)
        {
            return actor.IsSystemAdmin || actor.IsLibraryEditor ? _Settings.Media.MaxAdminVideoSeconds : _Settings.Media.MaxMemberVideoSeconds;
        }

        private async Task<int> OwnerDurationCapAsync(MediaAttachment media, ContentActor actor, CancellationToken token)
        {
            if (actor.UserId == media.OwnerUserId) return DurationCapSeconds(actor);
            HashSet<string> admins = await _Summaries.SystemAdminsAsync(new[] { media.OwnerUserId }, token).ConfigureAwait(false);
            return admins.Contains(media.OwnerUserId) ? _Settings.Media.MaxAdminVideoSeconds : _Settings.Media.MaxMemberVideoSeconds;
        }

        private static bool EtagEquals(string? a, string? b)
        {
            return String.Equals((a ?? String.Empty).Trim().Trim('"'), (b ?? String.Empty).Trim().Trim('"'), StringComparison.OrdinalIgnoreCase);
        }

        private static UploadTarget Target(MediaAttachment media, string origin)
        {
            return new UploadTarget { MediaId = media.Id, Location = media.SourceLocation, Key = media.SourceKey, ContentType = media.ContentType, SizeBytes = media.SizeBytes, Origin = origin ?? String.Empty };
        }

        private async Task<UploadInstructions> InstructionsAsync(MediaAttachment media, string origin, bool includeUploaded, CancellationToken token)
        {
            IObjectStore store = _Integrations.ObjectStore;
            DateTime expires = DateTime.UtcNow.AddMinutes(_Settings.Media.UploadUrlMinutes);
            if (expires > media.UploadExpiresUtc) expires = media.UploadExpiresUtc;
            expires = new DateTime(expires.Ticks - (expires.Ticks % TimeSpan.TicksPerSecond), DateTimeKind.Utc);
            UploadTarget target = Target(media, origin);
            UploadInstructions upload = new UploadInstructions { ExpiresUtc = expires };
            if (media.PartCount == 0)
            {
                upload.Mode = UploadModeEnum.Single;
                upload.Single = await store.PresignPutAsync(target, expires, token).ConfigureAwait(false);
                return upload;
            }
            upload.Mode = UploadModeEnum.Multipart;
            List<StoredPart> stored = includeUploaded
                ? await store.ListPartsAsync(media.SourceLocation, media.SourceKey, media.MultipartUploadId ?? String.Empty, token).ConfigureAwait(false)
                : new List<StoredPart>();
            upload.UploadedParts = stored.Where(p => p.PartNumber >= 1 && p.PartNumber <= media.PartCount)
                .Select(p => new UploadedPartView { PartNumber = p.PartNumber, Etag = p.ETag, SizeBytes = p.SizeBytes }).ToList();
            upload.Multipart = new MultipartUploadView
            {
                PartSizeBytes = media.PartSizeBytes,
                PartCount = media.PartCount,
                Parts = await store.PresignPartsAsync(target, media.MultipartUploadId ?? String.Empty, Enumerable.Range(1, media.PartCount), expires, token).ConfigureAwait(false)
            };
            return upload;
        }

        private async Task<MediaAttachment> OwnedAsync(ContentActor actor, string mediaId, CancellationToken token)
        {
            MediaAttachment? media = await _Database.Media.ReadAsync(mediaId, token).ConfigureAwait(false);
            if (media == null || media.IsDeleted || (media.OwnerUserId != actor.UserId && !actor.IsSystemAdmin)) throw ApiErrorException.NotFound("Media");
            return media;
        }

        private async Task RequireNotExpiredAsync(MediaAttachment media, CancellationToken token)
        {
            if (media.UploadExpiresUtc > DateTime.UtcNow) return;
            await ExpireUploadAsync(media, token).ConfigureAwait(false);
            throw new ApiErrorException(410, "UploadExpired", "The upload session expired after " + _Settings.Media.UploadExpiryHours + " hours; start a new upload.");
        }

        private async Task<bool> FailUploadAsync(MediaAttachment media, MediaFailureReasonEnum reason, CancellationToken token)
        {
            media.Status = MediaStatusEnum.Failed;
            media.FailureReason = reason;
            media.UpdatedUtc = DateTime.UtcNow;
            bool changed = await _Database.Media.UpdateAsync(media, MediaStatusEnum.Uploading, token).ConfigureAwait(false);
            if (changed) await NotifySettledAsync(media, token).ConfigureAwait(false);
            return changed;
        }

        private async Task<bool> CanSeeAsync(MediaAttachment media, ContentActor actor, CancellationToken token)
        {
            if (media.IsDeleted) return false;
            if (actor.IsSystemAdmin || (actor.UserId != null && media.OwnerUserId == actor.UserId)) return true;
            if (media.IsPrivate) return actor.UserId != null && await ViewerAllowedAsync(media, actor.UserId, token).ConfigureAwait(false);
            if (!media.IsAttached) return false;
            return await _AttachedVisibility(media, actor, token).ConfigureAwait(false);
        }

        private async Task ApplyUpdateAsync(ContentActor actor, MediaAttachment media, MediaUpdateRequest request, HashSet<string> present, CancellationToken token)
        {
            bool isStaff = actor.IsStaffFor(TargetTypeEnum.Media);
            string? altText = null;
            if (present.Contains("altText"))
            {
                altText = String.IsNullOrWhiteSpace(request.AltText) ? null : MarkdownNormalizer.NormalizeSingleLine(request.AltText);
                if (altText != null && MarkdownNormalizer.Length(altText) > MaxAltTextLength) throw ApiErrorException.Field("altText", "At most " + MaxAltTextLength + " characters.");
                if (altText != null && AltTextFilter != null && !isStaff) await AltTextFilter(actor, altText, token).ConfigureAwait(false);
            }
            await _Metadata.ValidateAsync(actor, request.Labels, request.Tags, isStaff, token).ConfigureAwait(false);
            if (present.Contains("altText"))
            {
                media.AltText = String.IsNullOrEmpty(altText) ? null : altText;
                media.UpdatedUtc = DateTime.UtcNow;
                await _Database.Media.UpdateAsync(media, null, token).ConfigureAwait(false);
            }
            await _Metadata.ApplyAsync(actor, TargetTypeEnum.Media, media.Id, request.Labels, request.Tags, token).ConfigureAwait(false);
        }

        private async Task<List<MediaView>> ViewsAsync(List<MediaAttachment> rows, bool publicPaths, bool includeStaff, string? viewerKey, CancellationToken token)
        {
            if (rows.Count == 0) return new List<MediaView>();
            List<string> ids = rows.Select(r => r.Id).ToList();
            Dictionary<string, List<MediaVariant>> variants = await _Database.Media.ReadVariantsManyAsync(rows.Where(r => (r.Kind == MediaKindEnum.Image || r.Kind == MediaKindEnum.Document) && r.Status == MediaStatusEnum.Ready).Select(r => r.Id), token).ConfigureAwait(false);
            Dictionary<string, ObjectMetadata> metadata = await _Metadata.LoadAsync(TargetTypeEnum.Media, ids, includeStaff, token).ConfigureAwait(false);
            DateTime expires = DateTime.UtcNow.AddMinutes(_Settings.Messaging.MediaUrlMinutes);
            expires = new DateTime(expires.Ticks - (expires.Ticks % TimeSpan.TicksPerSecond), DateTimeKind.Utc);
            List<MediaView> views = new List<MediaView>();
            foreach (MediaAttachment r in rows)
            {
                List<MediaVariant>? v = variants.TryGetValue(r.Id, out List<MediaVariant>? found) ? found : null;
                ObjectMetadata? m = metadata.TryGetValue(r.Id, out ObjectMetadata? meta) ? meta : null;
                if (r.IsPrivate && viewerKey != null)
                {
                    string tokenValue = PrivateTokens.Issue(new PrivateMediaGrant { PropertyId = PropertyId, MediaId = r.Id, Purpose = r.Purpose, ScopeId = r.ScopeId ?? String.Empty, ViewerKey = viewerKey, ExpiresUtc = expires });
                    views.Add(Urls.Build(r, v, m, file => PrivateFilesPath + "/" + tokenValue + "/" + file));
                }
                else views.Add(Urls.Build(r, v, publicPaths, m));
            }
            return views;
        }

        private static string? ViewerKey(ContentActor actor)
        {
            if (!String.IsNullOrEmpty(actor.UserId)) return actor.UserId;
            return actor.IsBreakGlass ? PrivateMediaTokens.BreakGlassViewer : null;
        }

        private PrivateMediaScope? Scope(MediaPurposeEnum purpose)
        {
            lock (_HandlerLock) return _PrivateScopes.TryGetValue(purpose, out PrivateMediaScope? scope) ? scope : null;
        }

        private static MediaPurposeEnum ParsePurpose(string? raw)
        {
            if (String.IsNullOrWhiteSpace(raw)) return MediaPurposeEnum.Content;
            if (!Int32.TryParse(raw, out int _) && Enum.TryParse(raw.Trim(), true, out MediaPurposeEnum purpose) && Enum.IsDefined(purpose)) return purpose;
            throw ApiErrorException.Field("purpose", "Use Content, Ticket, or Message.");
        }

        private async Task<bool> ViewerAllowedAsync(MediaAttachment media, string viewerKey, CancellationToken token)
        {
            if (String.IsNullOrEmpty(viewerKey)) return false;
            if (viewerKey == PrivateMediaTokens.BreakGlassViewer) return true;
            if (String.IsNullOrEmpty(media.ScopeId)) return String.Equals(media.OwnerUserId, viewerKey, StringComparison.Ordinal);
            PrivateMediaScope? scope = Scope(media.Purpose);
            if (scope == null) return false;
            return await scope.CanAccess(viewerKey, media.ScopeId, token).ConfigureAwait(false);
        }

        private async Task<List<AdminMediaItem>> AdminItemsAsync(List<MediaAttachment> rows, CancellationToken token)
        {
            List<MediaView> views = await ViewsAsync(rows, false, true, null, token).ConfigureAwait(false);
            Dictionary<string, UserSummary> owners = await _Summaries.BuildAsync(rows.Select(r => (string?)r.OwnerUserId), token).ConfigureAwait(false);
            Dictionary<string, List<MediaVariant>> variants = await _Database.Media.ReadVariantsManyAsync(rows.Select(r => r.Id), token).ConfigureAwait(false);
            List<AdminMediaItem> items = new List<AdminMediaItem>();
            for (int i = 0; i < rows.Count; i++)
            {
                MediaAttachment media = rows[i];
                long variantBytes = variants.TryGetValue(media.Id, out List<MediaVariant>? list) ? list.Sum(v => v.SizeBytes) : 0;
                items.Add(new AdminMediaItem
                {
                    Media = views[i],
                    Owner = owners.TryGetValue(media.OwnerUserId, out UserSummary? owner) ? owner : new UserSummary(),
                    AttachedTo = media.IsAttached ? new MediaTargetView { TargetType = media.AttachedTargetType!.Value, TargetId = media.AttachedTargetId! } : null,
                    StoredBytes = media.SizeBytes + variantBytes,
                    FileName = media.OriginalFileName,
                    CreatedUtc = media.CreatedUtc
                });
            }
            return items;
        }

        private async Task<int> DeleteForUserAsync(string userId, bool unattachedOnly, CancellationToken token)
        {
            if (String.IsNullOrEmpty(userId)) throw new ArgumentNullException(nameof(userId));
            int count = 0;
            for (int batch = 0; batch < 1000; batch++)
            {
                List<MediaAttachment> rows = await _Database.Media.ListByOwnerAsync(userId, unattachedOnly, 100, token).ConfigureAwait(false);
                if (rows.Count == 0) break;
                foreach (MediaAttachment media in rows)
                {
                    await DeleteMediaAsync(media, token).ConfigureAwait(false);
                    count++;
                }
            }
            return count;
        }

        private async Task NotifySettledAsync(MediaAttachment media, CancellationToken token)
        {
            if (media.IsPrivate && media.Status == MediaStatusEnum.Ready)
            {
                try
                {
                    await Storage.MoveToPrivateAsync(media, token).ConfigureAwait(false);
                }
                catch (Exception e) when (e is not OperationCanceledException)
                {
                    _Logger.Error("[MediaService] moving private media " + media.Id + " out of the served locations failed", e);
                }
            }
            List<Func<MediaAttachment, CancellationToken, Task>> handlers;
            lock (_HandlerLock) handlers = _SettledHandlers.ToList();
            foreach (Func<MediaAttachment, CancellationToken, Task> handler in handlers)
            {
                try
                {
                    await handler(media, token).ConfigureAwait(false);
                }
                catch (Exception e) when (e is not OperationCanceledException)
                {
                    _Logger.Error("[MediaService] settled handler for " + media.Id + " failed", e);
                }
            }
        }

        #endregion
    }
}
