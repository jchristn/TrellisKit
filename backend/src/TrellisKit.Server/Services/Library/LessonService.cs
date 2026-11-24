namespace TrellisKit.Server.Services
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text.RegularExpressions;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core.Enumeration;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Exceptions;
    using TrellisKit.Core.Helpers;
    using TrellisKit.Core.Models;
    using TrellisKit.Core.Observability;
    using TrellisKit.Core.Requests;
    using TrellisKit.Core.Responses;

    /// <summary>
    /// Library lessons (docs/PHASE4_CONTRACT.md sections 5.1 to 5.4, 5.10, and 5.12): member lists and details with
    /// locked stubs and samples, the library staff routes (create, edit, publish, schedule, archive, move, order,
    /// delete), video, resource, and caption media through the Phase 2 pipeline, scheduled publication, and public
    /// sample lessons.
    /// Thread safety: safe to call concurrently.
    /// </summary>
    public class LessonService
    {
        #region Public-Members

        /// <summary>
        /// Most caption tracks per lesson (fixed bound).
        /// </summary>
        public const int MaxCaptions = 10;

        /// <summary>
        /// Longest marker label (fixed bound).
        /// </summary>
        public const int MaxMarkerLabelLength = 80;

        /// <summary>
        /// Longest Reference resource text (fixed bound).
        /// </summary>
        public const int MaxReferenceLength = 300;

        /// <summary>
        /// Most public sample lessons in one page.
        /// </summary>
        public const int MaxPublicResults = 20;

        #endregion

        #region Private-Members

        private static readonly Regex _Language = new Regex("^[A-Za-z]{2,3}(-[A-Za-z0-9]{1,8}){0,3}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
        private static readonly IReadOnlyCollection<LibraryStatusEnum> _Any = Array.Empty<LibraryStatusEnum>();
        private readonly ApplicationServices _Services;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="services">Property services.</param>
        /// <exception cref="ArgumentNullException">The services are null.</exception>
        public LessonService(ApplicationServices services)
        {
            _Services = services ?? throw new ArgumentNullException(nameof(services));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// POST /api/v1/lessons/enumerate: published lessons the caller may read, plus locked stubs.
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="query">Query.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Page.</returns>
        /// <exception cref="ApiErrorException">400 ValidationFailed, 429 RateLimited.</exception>
        public async Task<EnumerationResult<LessonSummaryView>> ListAsync(ContentActor actor, EnumerationQuery? query, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            _Services.Media.EnforceRead(actor);
            EnumerationQuery q = query ?? new EnumerationQuery();
            LessonFilter filter = ParseFilter(q, actor, false);
            filter.Audiences = LibraryAccess.Readable(actor);
            filter.StubAudiences = LibraryAccess.Stubs(actor);
            EnumerationRequest request = _Services.Enumeration.Prepare(q, LibraryCollections.Lessons, actor.IsLibraryStaff, "lessons");
            EnumerationResult<Lesson> page = await _Services.Database.Library.Catalog.EnumerateLessonsAsync(request, filter, token).ConfigureAwait(false);
            return page.With(await _Services.Library.Views.LessonSummariesAsync(page.Objects, actor, false, token).ConfigureAwait(false));
        }

        /// <summary>
        /// POST /api/v1/series/{id}/lessons/enumerate: the series' published lessons in position order; a series the
        /// caller sees as a locked stub lists only the lessons the caller may open.
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="seriesIdOrSlug">Series.</param>
        /// <param name="query">Query.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Page.</returns>
        /// <exception cref="ApiErrorException">400 ValidationFailed, 404 NotFound.</exception>
        public async Task<EnumerationResult<LessonSummaryView>> ListSeriesAsync(ContentActor actor, string seriesIdOrSlug, EnumerationQuery? query, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            _Services.Media.EnforceRead(actor);
            LibrarySeries series = await _Services.Library.Series.RequireVisibleAsync(actor, seriesIdOrSlug, token).ConfigureAwait(false);
            EnumerationQuery q = query ?? new EnumerationQuery();
            if (q.MaxResults == null) q.MaxResults = Math.Min(_Services.Settings.Library.MaxLessonsPerSeries, 100);
            LessonFilter filter = new LessonFilter { SeriesIds = new List<string> { series.Id }, Audiences = LibraryAccess.Readable(actor) };
            if (LibraryAccess.CanRead(actor, series.Audience)) filter.StubAudiences = LibraryAccess.Stubs(actor);
            EnumerationRequest request = _Services.Enumeration.Prepare(q, LibraryCollections.SeriesLessons, actor.IsLibraryStaff, "series-lessons|" + series.Id);
            EnumerationResult<Lesson> page = await _Services.Database.Library.Catalog.EnumerateLessonsAsync(request, filter, token).ConfigureAwait(false);
            return page.With(await _Services.Library.Views.LessonSummariesAsync(page.Objects, actor, false, token).ConfigureAwait(false));
        }

        /// <summary>
        /// GET /api/v1/lessons/{idOrSlug}.
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="idOrSlug">Identifier or slug.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Lesson (a locked stub when the caller cannot read it).</returns>
        /// <exception cref="ApiErrorException">404 NotFound, 429 RateLimited.</exception>
        public async Task<LessonView> GetAsync(ContentActor actor, string idOrSlug, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            _Services.Media.EnforceRead(actor);
            Lesson lesson = await RequireVisibleAsync(actor, idOrSlug, token).ConfigureAwait(false);
            // A signed-out guest (anonymous browsing) plays a Public lesson from the public media paths, which need no
            // media grant (docs/PHASE4_CONTRACT.md section 5.3, item 4).
            bool guestSample = (actor.IsAnonymous || String.IsNullOrEmpty(actor.UserId)) && lesson.Audience == ContentAudienceEnum.Public;
            return await _Services.Library.Views.LessonAsync(lesson, actor, guestSample, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Load a lesson the caller may at least see as a locked stub: live lessons in live series, and archived
        /// lessons within library.archivedPlaybackDays for members with progress. Library staff see every undeleted lesson.
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="idOrSlug">Identifier or slug.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Lesson (with child lists).</returns>
        /// <exception cref="ApiErrorException">404 NotFound.</exception>
        public async Task<Lesson> RequireVisibleAsync(ContentActor actor, string idOrSlug, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            string key = (idOrSlug ?? String.Empty).Trim();
            Lesson? lesson = key.Length == 0 ? null : key.StartsWith(LibraryIdPrefixes.Lesson, StringComparison.Ordinal)
                ? await _Services.Database.Library.Catalog.ReadLessonAsync(key, token).ConfigureAwait(false)
                : await _Services.Database.Library.Catalog.ReadLessonBySlugAsync(key, token).ConfigureAwait(false);
            if (lesson == null) throw ApiErrorException.NotFound("Lesson");
            if (actor.IsLibraryStaff) return lesson;
            LibrarySeries? series = await _Services.Database.Library.Catalog.ReadSeriesAsync(lesson.SeriesId, token).ConfigureAwait(false);
            if (LibraryAccess.IsLive(lesson, series))
            {
                if (LibraryAccess.CanRead(actor, lesson.Audience) || LibraryAccess.ShowsLocked(actor, lesson.Audience)) return lesson;
                throw ApiErrorException.NotFound("Lesson");
            }
            if (!String.IsNullOrEmpty(actor.UserId) && LibraryAccess.CanRead(actor, lesson.Audience) &&
                LibraryAccess.InArchiveWindow(lesson, series, _Services.Settings.Library.ArchivedPlaybackDays, DateTime.UtcNow) &&
                await _Services.Database.Library.Progress.ReadAsync(actor.UserId, lesson.Id, token).ConfigureAwait(false) != null)
                return lesson;
            throw ApiErrorException.NotFound("Lesson");
        }

        /// <summary>
        /// Load a lesson the caller may play and take part on (progress, completion): visible, readable in full, and
        /// meeting the participation tier.
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="lessonId">Lesson.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Lesson.</returns>
        /// <exception cref="ApiErrorException">401, 402, 403, 404.</exception>
        public async Task<Lesson> RequirePlayableAsync(ContentActor actor, string lessonId, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            Lesson lesson = await RequireVisibleAsync(actor, lessonId, token).ConfigureAwait(false);
            if (!LibraryAccess.CanRead(actor, lesson.Audience) || !LibraryAccess.CanParticipate(actor, lesson.Audience)) throw LibraryAccess.Denied(actor, TargetTypeEnum.Lesson, lesson.Id);
            return lesson;
        }

        /// <summary>
        /// POST /api/v1/public/lessons/enumerate: Public published lessons (anonymous) while
        /// library.publicSamplesEnabled is on.
        /// </summary>
        /// <param name="actor">Caller (anonymous).</param>
        /// <param name="query">Query.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Page.</returns>
        /// <exception cref="ApiErrorException">400 ValidationFailed, 404 NotAvailable, 429 RateLimited.</exception>
        public async Task<EnumerationResult<PublicLessonView>> ListPublicAsync(ContentActor actor, EnumerationQuery? query, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            await RequirePublicSamplesAsync(actor, token).ConfigureAwait(false);
            EnumerationQuery q = query ?? new EnumerationQuery();
            if (q.MaxResults == null || q.MaxResults > MaxPublicResults) q.MaxResults = Math.Min(q.MaxResults ?? MaxPublicResults, MaxPublicResults);
            EnumerationRequest request = _Services.Enumeration.Prepare(q, LibraryCollections.PublicLessons, false, "public-lessons", MaxPublicResults);
            LessonFilter filter = new LessonFilter { PublicOnly = true, Audiences = new List<ContentAudienceEnum> { ContentAudienceEnum.Public } };
            EnumerationResult<Lesson> page = await _Services.Database.Library.Catalog.EnumerateLessonsAsync(request, filter, token).ConfigureAwait(false);
            List<Lesson> full = await _Services.Database.Library.Catalog.ReadLessonsAsync(page.Objects.Select(l => l.Id), true, token).ConfigureAwait(false);
            Dictionary<string, Lesson> byId = full.ToDictionary(l => l.Id, StringComparer.Ordinal);
            List<PublicLessonView> views = new List<PublicLessonView>();
            foreach (Lesson lesson in page.Objects) views.Add(ToPublic(await _Services.Library.Views.LessonAsync(byId.TryGetValue(lesson.Id, out Lesson? l) ? l : lesson, actor, true, token).ConfigureAwait(false)));
            return page.With(views);
        }

        /// <summary>
        /// GET /api/v1/public/lessons/{id}.
        /// </summary>
        /// <param name="actor">Caller (anonymous).</param>
        /// <param name="idOrSlug">Identifier or slug.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Public lesson.</returns>
        /// <exception cref="ApiErrorException">404 NotFound or NotAvailable, 429 RateLimited.</exception>
        public async Task<PublicLessonView> GetPublicAsync(ContentActor actor, string idOrSlug, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            await RequirePublicSamplesAsync(actor, token).ConfigureAwait(false);
            string key = (idOrSlug ?? String.Empty).Trim();
            Lesson? lesson = key.Length == 0 ? null : key.StartsWith(LibraryIdPrefixes.Lesson, StringComparison.Ordinal)
                ? await _Services.Database.Library.Catalog.ReadLessonAsync(key, token).ConfigureAwait(false)
                : await _Services.Database.Library.Catalog.ReadLessonBySlugAsync(key, token).ConfigureAwait(false);
            if (lesson == null || lesson.Audience != ContentAudienceEnum.Public) throw ApiErrorException.NotFound("Lesson");
            LibrarySeries? series = await _Services.Database.Library.Catalog.ReadSeriesAsync(lesson.SeriesId, token).ConfigureAwait(false);
            if (!LibraryAccess.IsLive(lesson, series)) throw ApiErrorException.NotFound("Lesson");
            return ToPublic(await _Services.Library.Views.LessonAsync(lesson, actor, true, token).ConfigureAwait(false));
        }

        #region Admin

        /// <summary>
        /// POST /api/v1/admin/lessons/enumerate (library staff).
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="query">Query.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Page.</returns>
        /// <exception cref="ApiErrorException">400 ValidationFailed, 403 Forbidden.</exception>
        public async Task<EnumerationResult<LessonSummaryView>> ListAdminAsync(ContentActor actor, EnumerationQuery? query, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            LibraryAccess.RequireStaff(actor);
            EnumerationQuery q = query ?? new EnumerationQuery();
            LessonFilter filter = ParseFilter(q, actor, true);
            EnumerationRequest request = _Services.Enumeration.Prepare(q, LibraryCollections.AdminLessons, true, "admin-lessons");
            EnumerationResult<Lesson> page = await _Services.Database.Library.Catalog.EnumerateLessonsAsync(request, filter, token).ConfigureAwait(false);
            return page.With(await _Services.Library.Views.LessonSummariesAsync(page.Objects, actor, false, token).ConfigureAwait(false));
        }

        /// <summary>
        /// GET /api/v1/admin/lessons/{id} (library staff).
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="lessonId">Lesson.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Lesson.</returns>
        /// <exception cref="ApiErrorException">403 Forbidden, 404 NotFound.</exception>
        public async Task<LessonView> GetAdminAsync(ContentActor actor, string lessonId, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            LibraryAccess.RequireStaff(actor);
            Lesson lesson = await RequireAsync(lessonId, token).ConfigureAwait(false);
            return await _Services.Library.Views.LessonAsync(lesson, actor, false, token).ConfigureAwait(false);
        }

        /// <summary>
        /// POST /api/v1/admin/series/{id}/lessons (library staff): a Draft lesson at the end of the series.
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="info">Request information.</param>
        /// <param name="seriesId">Series.</param>
        /// <param name="request">Body.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Lesson.</returns>
        /// <exception cref="ApiErrorException">400 ValidationFailed or UnknownOption, 403 Forbidden, 404 NotFound, 409 SlugInUse, LessonLimitReached, or MediaNotAttachable.</exception>
        public async Task<LessonView> CreateAsync(ContentActor actor, RequestInfo info, string seriesId, LessonRequest request, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            ArgumentNullException.ThrowIfNull(info);
            ArgumentNullException.ThrowIfNull(request);
            LibraryAccess.RequireStaff(actor);
            LibrarySeries series = await _Services.Library.Series.RequireAsync(seriesId, token).ConfigureAwait(false);
            List<string> existing = await _Services.Database.Library.Catalog.LessonIdsOfSeriesAsync(series.Id, _Services.Settings.Library.MaxLessonsPerSeries + 1, token).ConfigureAwait(false);
            if (existing.Count >= _Services.Settings.Library.MaxLessonsPerSeries)
                throw new ApiErrorException(409, "LessonLimitReached", "This series already has " + _Services.Settings.Library.MaxLessonsPerSeries + " lessons (library.maxLessonsPerSeries).");
            LibraryConfig config = await _Services.Library.Config.CurrentAsync(token).ConfigureAwait(false);
            DateTime now = DateTime.UtcNow;
            Lesson lesson = new Lesson
            {
                Id = IdGenerator.Generate(LibraryIdPrefixes.Lesson),
                SeriesId = series.Id,
                Audience = series.Audience,
                CommentsEnabled = _Services.Library.Config.CommentsEnabledByDefault(config),
                Status = LibraryStatusEnum.Draft,
                CreatedByUserId = actor.UserId,
                CreatedUtc = now,
                UpdatedUtc = now
            };
            HashSet<string> present = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "title", "slug", "summary", "description", "kind", "videoMediaId", "levels", "audience", "downloadable",
                "commentsEnabled", "notifyStaffOnComment", "markers", "resources", "captions" };
            List<MediaAttachment> media = await ApplyAsync(actor, lesson, request, present, true, token).ConfigureAwait(false);
            await _Services.Metadata.ValidateAsync(actor, request.Labels, request.Tags, true, token, TargetTypeEnum.Lesson).ConfigureAwait(false);
            try
            {
                lesson = await _Services.Database.Library.Catalog.CreateLessonAsync(lesson, token).ConfigureAwait(false);
            }
            catch (DuplicateRecordException)
            {
                throw SlugInUse();
            }
            await AttachMediaAsync(lesson, media, token).ConfigureAwait(false);
            await _Services.Metadata.ApplyAsync(actor, TargetTypeEnum.Lesson, lesson.Id, request.Labels, request.Tags, token).ConfigureAwait(false);
            await _Services.Audit.WriteAsync(info, "lesson.create", TargetTypeEnum.Lesson.ToString(), lesson.Id, lesson.Title, token).ConfigureAwait(false);
            await _Services.Forums.Search.EnqueueAsync(TargetTypeEnum.Lesson, lesson.Id, token).ConfigureAwait(false);
            return await AdminViewAsync(lesson.Id, actor, token).ConfigureAwait(false);
        }

        /// <summary>
        /// PUT /api/v1/admin/lessons/{id} (library staff): omitted fields stay; markers, resources, and captions are
        /// replaced when present. Replacing the video resets nobody's progress and clamps positions past the new duration.
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="info">Request information.</param>
        /// <param name="lessonId">Lesson.</param>
        /// <param name="request">Body.</param>
        /// <param name="present">Fields present in the body.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Lesson.</returns>
        /// <exception cref="ApiErrorException">400 ValidationFailed or UnknownOption, 403 Forbidden, 404 NotFound, 409 SlugInUse or MediaNotAttachable.</exception>
        public async Task<LessonView> UpdateAsync(ContentActor actor, RequestInfo info, string lessonId, LessonRequest request, HashSet<string> present, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            ArgumentNullException.ThrowIfNull(info);
            ArgumentNullException.ThrowIfNull(request);
            ArgumentNullException.ThrowIfNull(present);
            LibraryAccess.RequireStaff(actor);
            Lesson lesson = await RequireAsync(lessonId, token).ConfigureAwait(false);
            string previousSlug = lesson.Slug;
            string? previousVideo = lesson.VideoMediaId;
            ContentAudienceEnum previousAudience = lesson.Audience;
            List<MediaAttachment> media = await ApplyAsync(actor, lesson, request, present, false, token).ConfigureAwait(false);
            await _Services.Metadata.ValidateAsync(actor, request.Labels, request.Tags, true, token, TargetTypeEnum.Lesson).ConfigureAwait(false);
            bool videoChanged = !String.Equals(previousVideo, lesson.VideoMediaId, StringComparison.Ordinal);
            if (videoChanged && lesson.Status == LibraryStatusEnum.Published && lesson.Kind == LessonKindEnum.Video)
            {
                MediaAttachment? video = media.FirstOrDefault(m => m.Id == lesson.VideoMediaId);
                if (video == null || video.Status != MediaStatusEnum.Ready) lesson.Status = LibraryStatusEnum.Processing;
            }
            lesson.UpdatedUtc = DateTime.UtcNow;
            try
            {
                lesson = await _Services.Database.Library.Catalog.UpdateLessonAsync(lesson, previousSlug, token).ConfigureAwait(false);
            }
            catch (DuplicateRecordException)
            {
                throw SlugInUse();
            }
            await AttachMediaAsync(lesson, media, token).ConfigureAwait(false);
            await _Services.Metadata.ApplyAsync(actor, TargetTypeEnum.Lesson, lesson.Id, present.Contains("labels") ? request.Labels : null, present.Contains("tags") ? request.Tags : null, token).ConfigureAwait(false);
            if (previousAudience == ContentAudienceEnum.Public && lesson.Audience != ContentAudienceEnum.Public) await _Services.Media.UnpublishTargetAsync(TargetTypeEnum.Lesson, lesson.Id, token).ConfigureAwait(false);
            await SyncPublicAsync(lesson, token).ConfigureAwait(false);
            await _Services.Audit.WriteAsync(info, "lesson.update", TargetTypeEnum.Lesson.ToString(), lesson.Id, lesson.Title + (videoChanged ? " (video replaced)" : String.Empty), token).ConfigureAwait(false);
            await _Services.Forums.Search.EnqueueAsync(TargetTypeEnum.Lesson, lesson.Id, token).ConfigureAwait(false);
            if (lesson.Status == LibraryStatusEnum.Processing) await TryPublishAsync(lesson.Id, token).ConfigureAwait(false);
            return await AdminViewAsync(lesson.Id, actor, token).ConfigureAwait(false);
        }

        /// <summary>
        /// POST /api/v1/admin/lessons/{id}/publish: Published when the lesson is an article or its video is Ready;
        /// Processing (published automatically when Ready) while the video processes.
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="info">Request information.</param>
        /// <param name="lessonId">Lesson.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Lesson.</returns>
        /// <exception cref="ApiErrorException">400 LessonVideoRequired, 403 Forbidden, 404 NotFound.</exception>
        public async Task<LessonView> PublishAsync(ContentActor actor, RequestInfo info, string lessonId, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            ArgumentNullException.ThrowIfNull(info);
            LibraryAccess.RequireStaff(actor);
            Lesson lesson = await RequireAsync(lessonId, token).ConfigureAwait(false);
            MediaStatusEnum? video = await RequireVideoAsync(lesson, token).ConfigureAwait(false);
            if (lesson.Status != LibraryStatusEnum.Published)
            {
                if (lesson.Kind == LessonKindEnum.Video && video != MediaStatusEnum.Ready)
                    await _Services.Database.Library.Catalog.SetLessonStatusAsync(lesson.Id, LibraryStatusEnum.Processing, null, _Any, DateTime.UtcNow, token).ConfigureAwait(false);
                else
                    await PublishCoreAsync(lesson, token).ConfigureAwait(false);
            }
            await _Services.Audit.WriteAsync(info, "lesson.publish", TargetTypeEnum.Lesson.ToString(), lesson.Id, lesson.Title, token).ConfigureAwait(false);
            return await AdminViewAsync(lesson.Id, actor, token).ConfigureAwait(false);
        }

        /// <summary>
        /// POST /api/v1/admin/lessons/{id}/schedule ({ publishAtUtc }): Scheduled; LibraryPublishWorker publishes it
        /// once due and its video is Ready.
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="info">Request information.</param>
        /// <param name="lessonId">Lesson.</param>
        /// <param name="request">Body.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Lesson.</returns>
        /// <exception cref="ApiErrorException">400 ValidationFailed or LessonVideoRequired, 403 Forbidden, 404 NotFound, 409 AlreadyPublished.</exception>
        public async Task<LessonView> ScheduleAsync(ContentActor actor, RequestInfo info, string lessonId, LessonScheduleRequest request, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            ArgumentNullException.ThrowIfNull(info);
            ArgumentNullException.ThrowIfNull(request);
            LibraryAccess.RequireStaff(actor);
            Lesson lesson = await RequireAsync(lessonId, token).ConfigureAwait(false);
            if (request.PublishAtUtc == null) throw ApiErrorException.Field("publishAtUtc", "Required (UTC).");
            DateTime at = DateTime.SpecifyKind(request.PublishAtUtc.Value.ToUniversalTime(), DateTimeKind.Utc);
            if (lesson.Status == LibraryStatusEnum.Published) throw new ApiErrorException(409, "AlreadyPublished", "The lesson is already published.");
            await RequireVideoAsync(lesson, token).ConfigureAwait(false);
            await _Services.Database.Library.Catalog.SetLessonStatusAsync(lesson.Id, LibraryStatusEnum.Scheduled, at, _Any, DateTime.UtcNow, token).ConfigureAwait(false);
            await _Services.Audit.WriteAsync(info, "lesson.schedule", TargetTypeEnum.Lesson.ToString(), lesson.Id, lesson.Title + " at " + at.ToString("O", System.Globalization.CultureInfo.InvariantCulture), token).ConfigureAwait(false);
            if (at <= DateTime.UtcNow) await TryPublishAsync(lesson.Id, token).ConfigureAwait(false);
            return await AdminViewAsync(lesson.Id, actor, token).ConfigureAwait(false);
        }

        /// <summary>
        /// POST /api/v1/admin/lessons/{id}/archive and .../unarchive.
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="info">Request information.</param>
        /// <param name="lessonId">Lesson.</param>
        /// <param name="archive">Archive (true) or unarchive (false).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Lesson.</returns>
        /// <exception cref="ApiErrorException">403 Forbidden, 404 NotFound.</exception>
        public async Task<LessonView> SetArchivedAsync(ContentActor actor, RequestInfo info, string lessonId, bool archive, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            ArgumentNullException.ThrowIfNull(info);
            LibraryAccess.RequireStaff(actor);
            Lesson lesson = await RequireAsync(lessonId, token).ConfigureAwait(false);
            LibraryStatusEnum status = archive ? LibraryStatusEnum.Archived : (lesson.PublishedUtc != null ? LibraryStatusEnum.Published : LibraryStatusEnum.Draft);
            await _Services.Database.Library.Catalog.SetLessonStatusAsync(lesson.Id, status, null, _Any, DateTime.UtcNow, token).ConfigureAwait(false);
            if (archive) await _Services.Media.UnpublishTargetAsync(TargetTypeEnum.Lesson, lesson.Id, token).ConfigureAwait(false);
            else await SyncPublicAsync((await RequireAsync(lesson.Id, token).ConfigureAwait(false)), token).ConfigureAwait(false);
            await _Services.Audit.WriteAsync(info, archive ? "lesson.archive" : "lesson.unarchive", TargetTypeEnum.Lesson.ToString(), lesson.Id, lesson.Title, token).ConfigureAwait(false);
            await _Services.Forums.Search.EnqueueAsync(TargetTypeEnum.Lesson, lesson.Id, token).ConfigureAwait(false);
            return await AdminViewAsync(lesson.Id, actor, token).ConfigureAwait(false);
        }

        /// <summary>
        /// DELETE /api/v1/admin/lessons/{id}: soft delete (progress is kept for export until purge); download
        /// licenses are revoked.
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="info">Request information.</param>
        /// <param name="lessonId">Lesson.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        /// <exception cref="ApiErrorException">403 Forbidden, 404 NotFound.</exception>
        public async Task DeleteAsync(ContentActor actor, RequestInfo info, string lessonId, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            ArgumentNullException.ThrowIfNull(info);
            LibraryAccess.RequireStaff(actor);
            Lesson lesson = await RequireAsync(lessonId, token).ConfigureAwait(false);
            await _Services.Database.Library.Catalog.DeleteLessonAsync(lesson.Id, DateTime.UtcNow, token).ConfigureAwait(false);
            await _Services.Media.UnpublishTargetAsync(TargetTypeEnum.Lesson, lesson.Id, token).ConfigureAwait(false);
            await _Services.Database.Library.Downloads.RevokeAllAsync(null, lesson.Id, "lesson_deleted", DateTime.UtcNow, token).ConfigureAwait(false);
            await _Services.Audit.WriteAsync(info, "lesson.delete", TargetTypeEnum.Lesson.ToString(), lesson.Id, lesson.Title, token).ConfigureAwait(false);
            await _Services.Forums.Search.EnqueueAsync(TargetTypeEnum.Lesson, lesson.Id, token).ConfigureAwait(false);
        }

        /// <summary>
        /// POST /api/v1/admin/lessons/{id}/move ({ seriesId, position }).
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="info">Request information.</param>
        /// <param name="lessonId">Lesson.</param>
        /// <param name="request">Body.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Lesson.</returns>
        /// <exception cref="ApiErrorException">400 ValidationFailed, 403 Forbidden, 404 NotFound, 409 SlugInUse or LessonLimitReached.</exception>
        public async Task<LessonView> MoveAsync(ContentActor actor, RequestInfo info, string lessonId, LessonMoveRequest request, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            ArgumentNullException.ThrowIfNull(info);
            ArgumentNullException.ThrowIfNull(request);
            LibraryAccess.RequireStaff(actor);
            Lesson lesson = await RequireAsync(lessonId, token).ConfigureAwait(false);
            if (String.IsNullOrWhiteSpace(request.SeriesId)) throw ApiErrorException.Field("seriesId", "Required.");
            LibrarySeries target = await _Services.Library.Series.RequireAsync(request.SeriesId, token).ConfigureAwait(false);
            List<string> ids = await _Services.Database.Library.Catalog.LessonIdsOfSeriesAsync(target.Id, _Services.Settings.Library.MaxLessonsPerSeries + 1, token).ConfigureAwait(false);
            if (target.Id != lesson.SeriesId && ids.Count >= _Services.Settings.Library.MaxLessonsPerSeries)
                throw new ApiErrorException(409, "LessonLimitReached", "The target series already has " + _Services.Settings.Library.MaxLessonsPerSeries + " lessons.");
            if (request.Position != null && request.Position < 1) throw ApiErrorException.Field("position", "Use 1 or more.");
            int position = request.Position ?? (target.Id == lesson.SeriesId ? ids.Count : ids.Count + 1);
            try
            {
                await _Services.Database.Library.Catalog.MoveLessonAsync(lesson.Id, target.Id, position, DateTime.UtcNow, token).ConfigureAwait(false);
            }
            catch (DuplicateRecordException)
            {
                throw new ApiErrorException(409, "SlugInUse", "A lesson in the target series already uses this slug.", new Dictionary<string, string> { { "slug", "Change the slug first." } });
            }
            await _Services.Audit.WriteAsync(info, "lesson.move", TargetTypeEnum.Lesson.ToString(), lesson.Id, lesson.Title + " to " + target.Id, token).ConfigureAwait(false);
            await _Services.Forums.Search.EnqueueAsync(TargetTypeEnum.Lesson, lesson.Id, token).ConfigureAwait(false);
            return await AdminViewAsync(lesson.Id, actor, token).ConfigureAwait(false);
        }

        /// <summary>
        /// PUT /api/v1/admin/series/{id}/lessons/order ({ lessonIds }): every undeleted lesson of the series exactly once.
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="info">Request information.</param>
        /// <param name="seriesId">Series.</param>
        /// <param name="request">Body.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Rows reordered.</returns>
        /// <exception cref="ApiErrorException">400 ValidationFailed, 403 Forbidden, 404 NotFound.</exception>
        public async Task<LibraryOrderResponse> OrderAsync(ContentActor actor, RequestInfo info, string seriesId, LibraryOrderRequest request, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            ArgumentNullException.ThrowIfNull(info);
            ArgumentNullException.ThrowIfNull(request);
            LibraryAccess.RequireStaff(actor);
            LibrarySeries series = await _Services.Library.Series.RequireAsync(seriesId, token).ConfigureAwait(false);
            int max = _Services.Settings.Library.MaxLessonsPerSeries;
            List<string> ids = (request.LessonIds ?? new List<string>()).Select(i => (i ?? String.Empty).Trim()).ToList();
            if (ids.Count > max) throw ApiErrorException.Field("lessonIds", "At most " + max + " lessons.");
            if (ids.Distinct(StringComparer.Ordinal).Count() != ids.Count) throw ApiErrorException.Field("lessonIds", "Each lesson may appear only once.");
            List<string> current = await _Services.Database.Library.Catalog.LessonIdsOfSeriesAsync(series.Id, max + 1, token).ConfigureAwait(false);
            HashSet<string> set = new HashSet<string>(current, StringComparer.Ordinal);
            List<string> unknown = ids.Where(i => !set.Contains(i)).ToList();
            if (unknown.Count > 0) throw ApiErrorException.Field("lessonIds", "Not in this series: " + String.Join(", ", unknown.Take(5)) + ".");
            if (ids.Count != current.Count) throw ApiErrorException.Field("lessonIds", "List every lesson of the series exactly once (" + current.Count + " lessons).");
            int updated = await _Services.Database.Library.Catalog.ReorderLessonsAsync(series.Id, ids, DateTime.UtcNow, token).ConfigureAwait(false);
            await _Services.Audit.WriteAsync(info, "lesson.order", TargetTypeEnum.Series.ToString(), series.Id, updated + " lessons", token).ConfigureAwait(false);
            return new LibraryOrderResponse { Updated = updated };
        }

        #endregion

        /// <summary>
        /// LibraryPublishWorker pass: publish due Scheduled and Processing lessons whose video is Ready (articles at once).
        /// </summary>
        /// <param name="limit">Most lessons checked.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Lessons published.</returns>
        public async Task<int> PublishDueAsync(int limit, CancellationToken token = default)
        {
            int published = 0;
            List<Lesson> due = await _Services.Database.Library.Catalog.DueLessonsAsync(DateTime.UtcNow, Math.Clamp(limit, 1, 1000), token).ConfigureAwait(false);
            foreach (Lesson lesson in due)
            {
                token.ThrowIfCancellationRequested();
                if (await TryPublishAsync(lesson.Id, token).ConfigureAwait(false)) published++;
            }
            return published;
        }

        /// <summary>
        /// Publish a Scheduled or Processing lesson when it is due and its video is Ready; a due Scheduled lesson whose
        /// video still processes becomes Processing.
        /// </summary>
        /// <param name="lessonId">Lesson.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True when published by this call.</returns>
        public async Task<bool> TryPublishAsync(string lessonId, CancellationToken token = default)
        {
            Lesson? lesson = await _Services.Database.Library.Catalog.ReadLessonAsync(lessonId, token).ConfigureAwait(false);
            if (lesson == null || (lesson.Status != LibraryStatusEnum.Scheduled && lesson.Status != LibraryStatusEnum.Processing)) return false;
            if (lesson.PublishAtUtc != null && lesson.PublishAtUtc > DateTime.UtcNow) return false;
            if (lesson.Kind == LessonKindEnum.Video)
            {
                MediaAttachment? video = lesson.VideoMediaId == null ? null : await _Services.Database.Media.ReadAsync(lesson.VideoMediaId, token).ConfigureAwait(false);
                if (video == null || video.IsDeleted || video.Status != MediaStatusEnum.Ready)
                {
                    if (lesson.Status == LibraryStatusEnum.Scheduled)
                        await _Services.Database.Library.Catalog.SetLessonStatusAsync(lesson.Id, LibraryStatusEnum.Processing, null, new[] { LibraryStatusEnum.Scheduled }, DateTime.UtcNow, token).ConfigureAwait(false);
                    return false;
                }
                int seconds = (int)Math.Round(video.DurationSeconds ?? 0);
                if (seconds != lesson.DurationSeconds) await _Services.Database.Library.Catalog.SetLessonDurationAsync(lesson.Id, seconds, DateTime.UtcNow, token).ConfigureAwait(false);
            }
            return await PublishCoreAsync(lesson, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Media settled handler: refresh durations, publish waiting lessons, queue document text, and refresh public copies.
        /// </summary>
        /// <param name="media">Media that became Ready or Failed.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        public async Task OnMediaSettledAsync(MediaAttachment media, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(media);
            if (media.Kind == MediaKindEnum.Image) return;
            List<string> ids = await _Services.Database.Library.Catalog.LessonIdsUsingMediaAsync(media.Id, token).ConfigureAwait(false);
            foreach (string id in ids)
            {
                Lesson? lesson = await _Services.Database.Library.Catalog.ReadLessonAsync(id, token).ConfigureAwait(false);
                if (lesson == null) continue;
                if (media.Status == MediaStatusEnum.Ready && media.Kind == MediaKindEnum.Video && lesson.VideoMediaId == media.Id)
                {
                    int seconds = (int)Math.Round(media.DurationSeconds ?? 0);
                    if (seconds != lesson.DurationSeconds) await _Services.Database.Library.Catalog.SetLessonDurationAsync(lesson.Id, seconds, DateTime.UtcNow, token).ConfigureAwait(false);
                    if (lesson.Status == LibraryStatusEnum.Processing || lesson.Status == LibraryStatusEnum.Scheduled) await TryPublishAsync(lesson.Id, token).ConfigureAwait(false);
                }
                if (media.Status == MediaStatusEnum.Ready && media.Kind == MediaKindEnum.Document)
                    await _Services.Library.Documents.EnqueueAsync(media, token).ConfigureAwait(false);
                await SyncPublicAsync((await _Services.Database.Library.Catalog.ReadLessonAsync(id, token).ConfigureAwait(false)) ?? lesson, token).ConfigureAwait(false);
                await _Services.Forums.Search.EnqueueAsync(TargetTypeEnum.Lesson, lesson.Id, token).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Queue every lesson of a series for search indexing (after a series change).
        /// </summary>
        /// <param name="seriesId">Series.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        public async Task ReindexSeriesAsync(string seriesId, CancellationToken token = default)
        {
            foreach (string id in await _Services.Database.Library.Catalog.LessonIdsOfSeriesAsync(seriesId, _Services.Settings.Library.MaxLessonsPerSeries, token).ConfigureAwait(false))
                await _Services.Forums.Search.EnqueueAsync(TargetTypeEnum.Lesson, id, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Read an undeleted lesson for staff routes.
        /// </summary>
        /// <param name="lessonId">Lesson.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Lesson.</returns>
        /// <exception cref="ApiErrorException">404 NotFound.</exception>
        public async Task<Lesson> RequireAsync(string lessonId, CancellationToken token = default)
        {
            Lesson? lesson = String.IsNullOrWhiteSpace(lessonId) ? null : await _Services.Database.Library.Catalog.ReadLessonAsync(lessonId.Trim(), token).ConfigureAwait(false);
            if (lesson == null) throw ApiErrorException.NotFound("Lesson");
            return lesson;
        }

        #endregion

        #region Private-Methods

        private async Task<bool> PublishCoreAsync(Lesson lesson, CancellationToken token)
        {
            bool first = lesson.PublishedUtc == null;
            bool changed = await _Services.Database.Library.Catalog.SetLessonStatusAsync(lesson.Id, LibraryStatusEnum.Published, null,
                new[] { LibraryStatusEnum.Draft, LibraryStatusEnum.Scheduled, LibraryStatusEnum.Processing }, DateTime.UtcNow, token).ConfigureAwait(false);
            if (!changed) return false;
            LibraryTelemetry.Count(LibraryTelemetry.LessonsPublished, "kind", lesson.Kind == LessonKindEnum.Video ? "video" : "article");
            Lesson? published = await _Services.Database.Library.Catalog.ReadLessonAsync(lesson.Id, token).ConfigureAwait(false);
            if (published != null) await SyncPublicAsync(published, token).ConfigureAwait(false);
            await _Services.Forums.Search.EnqueueAsync(TargetTypeEnum.Lesson, lesson.Id, token).ConfigureAwait(false);
            if (first) await _Services.Notifications.StartFanoutAsync(NotificationTypeEnum.NewLessonInSeries, lesson.Id, token).ConfigureAwait(false);
            return true;
        }

        private async Task SyncPublicAsync(Lesson lesson, CancellationToken token)
        {
            if (lesson.Audience != ContentAudienceEnum.Public || lesson.DeletedUtc != null) return;
            if (lesson.Status != LibraryStatusEnum.Published) return;
            await _Services.Media.PublishTargetAsync(TargetTypeEnum.Lesson, lesson.Id, token).ConfigureAwait(false);
        }

        private async Task<MediaStatusEnum?> RequireVideoAsync(Lesson lesson, CancellationToken token)
        {
            if (lesson.Kind != LessonKindEnum.Video) return null;
            MediaAttachment? video = lesson.VideoMediaId == null ? null : await _Services.Database.Media.ReadAsync(lesson.VideoMediaId, token).ConfigureAwait(false);
            if (video == null || video.IsDeleted || video.Status == MediaStatusEnum.Failed || video.Status == MediaStatusEnum.Uploading)
                throw new ApiErrorException(400, "LessonVideoRequired", "A Video lesson needs an uploaded video (videoMediaId) before it can be published or scheduled.");
            return video.Status;
        }

        private async Task<LessonView> AdminViewAsync(string lessonId, ContentActor actor, CancellationToken token)
        {
            Lesson lesson = await RequireAsync(lessonId, token).ConfigureAwait(false);
            return await _Services.Library.Views.LessonAsync(lesson, actor, false, token).ConfigureAwait(false);
        }

        private async Task RequirePublicSamplesAsync(ContentActor actor, CancellationToken token)
        {
            if (!_Services.Property.Property.Features.Library) throw new ApiErrorException(404, "NotAvailable", "The library is turned off for this property (features.library).");
            LibraryConfig config = await _Services.Library.Config.CurrentAsync(token).ConfigureAwait(false);
            if (!_Services.Library.Config.PublicSamplesEnabled(config)) throw new ApiErrorException(404, "NotAvailable", "Public sample lessons are turned off.");
            _Services.Limits.EnforceIp(actor.SourceIp, "library.public", _Services.Settings.RateLimits.PublicFeedPerMinute, RateLimitService.Minute);
        }

        private static PublicLessonView ToPublic(LessonView view)
        {
            return new PublicLessonView
            {
                Id = view.Id,
                Series = view.Series,
                Position = view.Position,
                Slug = view.Slug,
                Title = view.Title,
                Summary = view.Summary,
                Description = view.Description,
                Kind = view.Kind,
                DurationSeconds = view.DurationSeconds,
                Levels = view.Levels,
                Audience = view.Audience,
                Video = view.Video == null ? null : MediaUrlBuilder.ForPublic(view.Video),
                Markers = view.Markers,
                Captions = view.Captions.Where(c => c.Media != null).Select(c => new LessonCaptionView { Language = c.Language, Label = c.Label, Media = MediaUrlBuilder.ForPublic(c.Media!) }).ToList(),
                CommentCount = view.CommentCount,
                ReactionTotal = view.ReactionTotal,
                Labels = view.Labels,
                Tags = view.Tags,
                PublishedUtc = view.PublishedUtc
            };
        }

        private async Task AttachMediaAsync(Lesson lesson, List<MediaAttachment> media, CancellationToken token)
        {
            List<string> ids = new List<string>();
            if (lesson.VideoMediaId != null) ids.Add(lesson.VideoMediaId);
            ids.AddRange(lesson.Resources.Where(r => r.MediaId != null).Select(r => r.MediaId!));
            ids.AddRange(lesson.Captions.Select(c => c.MediaId));
            await _Services.Media.AttachAsync(TargetTypeEnum.Lesson, lesson.Id, ids.Distinct(StringComparer.Ordinal).ToList(), token).ConfigureAwait(false);
            foreach (MediaAttachment m in media.Where(m => m.Kind == MediaKindEnum.Document && m.Status == MediaStatusEnum.Ready))
                await _Services.Library.Documents.EnqueueAsync(m, token).ConfigureAwait(false);
        }

        private LessonFilter ParseFilter(EnumerationQuery q, ContentActor actor, bool admin)
        {
            Dictionary<string, string> errors = new Dictionary<string, string>();
            LessonFilter filter = new LessonFilter
            {
                Admin = admin,
                Levels = ForumFilters.List(q, "levels") ?? new List<string>(),
                Kind = q.FilterEnum<LessonKindEnum>("kind", errors),
                Audience = q.FilterEnum<ContentAudienceEnum>("audience", errors)
            };
            if (admin)
            {
                string? series = q.FilterString("seriesId");
                if (series != null) filter.SeriesIds = new List<string> { series };
                filter.Statuses = ForumFilters.Enums<LibraryStatusEnum>(q, "status", errors);
                filter.HasVideo = q.FilterBool("hasVideo", errors);
                filter.MediaStatus = q.FilterEnum<MediaStatusEnum>("mediaStatus", errors);
            }
            else
            {
                List<string> seriesIds = ForumFilters.List(q, "seriesIds") ?? new List<string>();
                if (seriesIds.Count > 50) errors["filters.seriesIds"] = "At most 50 series.";
                filter.SeriesIds = seriesIds.Take(50).ToList();
                filter.HasResources = q.FilterBool("hasResources", errors);
                long? min = q.FilterLong("minDurationSeconds", errors);
                long? max = q.FilterLong("maxDurationSeconds", errors);
                filter.MinDurationSeconds = min == null ? null : (int)Math.Clamp(min.Value, 0, Int32.MaxValue);
                filter.MaxDurationSeconds = max == null ? null : (int)Math.Clamp(max.Value, 0, Int32.MaxValue);
                LessonProgressStateEnum? progress = q.FilterEnum<LessonProgressStateEnum>("progress", errors);
                if (progress != null)
                {
                    if (String.IsNullOrEmpty(actor.UserId)) errors["filters.progress"] = "Needs a member session.";
                    else
                    {
                        filter.Progress = progress;
                        filter.ProgressUserId = actor.UserId;
                    }
                }
            }
            if (errors.Count > 0) throw ApiErrorException.Validation(errors);
            return filter;
        }

        private async Task<List<MediaAttachment>> ApplyAsync(ContentActor actor, Lesson lesson, LessonRequest request, HashSet<string> present, bool creating, CancellationToken token)
        {
            Dictionary<string, string> errors = new Dictionary<string, string>();
            if (present.Contains("title"))
            {
                string? title = LibraryText.Title(request.Title, "title", errors);
                if (title != null) lesson.Title = title;
            }
            if (present.Contains("slug") && !(creating && String.IsNullOrWhiteSpace(request.Slug)))
            {
                string slug = (request.Slug ?? String.Empty).Trim().ToLowerInvariant();
                if (!LibraryText.IsSlug(slug)) errors["slug"] = "Use 2 to 80 of a-z, 0-9, and -.";
                else lesson.Slug = slug;
            }
            if (creating && String.IsNullOrEmpty(lesson.Slug) && !String.IsNullOrEmpty(lesson.Title)) lesson.Slug = LibraryText.DeriveSlug(lesson.Title, "lesson");
            if (present.Contains("summary")) lesson.Summary = LibraryText.Optional(request.Summary, LibraryText.MaxSummaryLength, "summary", errors, false);
            if (present.Contains("description")) lesson.Description = LibraryText.Optional(request.Description, _Services.Settings.Library.MaxArticleLength, "description", errors, true);
            if (present.Contains("kind") && request.Kind != null) lesson.Kind = request.Kind.Value;
            if (present.Contains("audience") && request.Audience != null)
            {
                if (request.Audience == ContentAudienceEnum.Public && !actor.IsSystemAdmin) errors["audience"] = "Only a SystemAdmin can make a lesson Public.";
                else lesson.Audience = request.Audience.Value;
            }
            if (present.Contains("downloadable") && request.Downloadable != null) lesson.Downloadable = request.Downloadable.Value;
            if (present.Contains("commentsEnabled") && request.CommentsEnabled != null) lesson.CommentsEnabled = request.CommentsEnabled.Value;
            if (present.Contains("notifyStaffOnComment") && request.NotifyStaffOnComment != null) lesson.NotifyStaffOnComment = request.NotifyStaffOnComment.Value;
            List<string> wanted = new List<string>();
            if (present.Contains("videoMediaId")) lesson.VideoMediaId = String.IsNullOrWhiteSpace(request.VideoMediaId) ? null : request.VideoMediaId.Trim();
            if (lesson.Kind == LessonKindEnum.Article) lesson.VideoMediaId = null;
            if (lesson.VideoMediaId != null) wanted.Add(lesson.VideoMediaId);
            if (present.Contains("resources") && request.Resources != null)
            {
                if (request.Resources.Count > _Services.Settings.Library.MaxResourcesPerLesson) errors["resources"] = "At most " + _Services.Settings.Library.MaxResourcesPerLesson + " resources.";
                lesson.Resources = new List<LessonResource>();
                for (int i = 0; i < request.Resources.Count && i < _Services.Settings.Library.MaxResourcesPerLesson; i++)
                {
                    LessonResourceInput input = request.Resources[i] ?? new LessonResourceInput();
                    string field = "resources[" + i + "]";
                    LessonResource resource = new LessonResource { Id = IdGenerator.Generate(LibraryIdPrefixes.Resource), LessonId = lesson.Id, Position = i };
                    string title = (input.Title ?? String.Empty).Trim();
                    if (title.Length < 1 || title.Length > 120) errors[field + ".title"] = "Use 1 to 120 characters.";
                    resource.Title = MarkdownNormalizer.NormalizeSingleLine(title);
                    if (input.Kind == null) errors[field + ".kind"] = "Use Document, Link, or Reference.";
                    else resource.Kind = input.Kind.Value;
                    switch (input.Kind)
                    {
                        case LessonResourceKindEnum.Document:
                            if (String.IsNullOrWhiteSpace(input.MediaId)) errors[field + ".mediaId"] = "Required for Document resources.";
                            else
                            {
                                resource.MediaId = input.MediaId.Trim();
                                wanted.Add(resource.MediaId);
                            }
                            break;
                        case LessonResourceKindEnum.Link:
                            if (!Uri.TryCreate((input.Url ?? String.Empty).Trim(), UriKind.Absolute, out Uri? uri) || uri.Scheme != Uri.UriSchemeHttps || uri.Host.Length == 0 || input.Url!.Trim().Length > 2000)
                                errors[field + ".url"] = "Use an https URL.";
                            else resource.Url = uri.ToString();
                            break;
                        case LessonResourceKindEnum.Reference:
                            string text = MarkdownNormalizer.IsWellFormed(input.Text) ? MarkdownNormalizer.NormalizeSingleLine(input.Text ?? String.Empty) : String.Empty;
                            if (text.Length < 1 || text.Length > MaxReferenceLength) errors[field + ".text"] = "Use 1 to " + MaxReferenceLength + " characters.";
                            else resource.Text = text;
                            break;
                    }
                    lesson.Resources.Add(resource);
                }
            }
            else
            {
                wanted.AddRange(lesson.Resources.Where(r => r.MediaId != null).Select(r => r.MediaId!));
            }
            if (present.Contains("captions") && request.Captions != null)
            {
                if (request.Captions.Count > MaxCaptions) errors["captions"] = "At most " + MaxCaptions + " caption tracks.";
                lesson.Captions = new List<LessonCaption>();
                for (int i = 0; i < request.Captions.Count && i < MaxCaptions; i++)
                {
                    LessonCaptionInput input = request.Captions[i] ?? new LessonCaptionInput();
                    string field = "captions[" + i + "]";
                    string language = (input.Language ?? String.Empty).Trim();
                    if (!_Language.IsMatch(language)) errors[field + ".language"] = "Use a BCP 47 language tag, for example en or pt-BR.";
                    string label = (input.Label ?? String.Empty).Trim();
                    if (label.Length < 1 || label.Length > 40) errors[field + ".label"] = "Use 1 to 40 characters.";
                    if (String.IsNullOrWhiteSpace(input.MediaId)) errors[field + ".mediaId"] = "Required.";
                    else wanted.Add(input.MediaId.Trim());
                    lesson.Captions.Add(new LessonCaption { Id = IdGenerator.Generate(LibraryIdPrefixes.Caption), LessonId = lesson.Id, Language = language, Label = MarkdownNormalizer.NormalizeSingleLine(label), MediaId = (input.MediaId ?? String.Empty).Trim(), Position = i });
                }
            }
            else
            {
                wanted.AddRange(lesson.Captions.Select(c => c.MediaId));
            }
            if (creating && String.IsNullOrEmpty(lesson.Title)) errors.TryAdd("title", "Required (2 to " + LibraryText.MaxTitleLength + " characters).");
            if (errors.Count > 0) throw ApiErrorException.Validation(errors);

            List<MediaAttachment> media = await _Services.Database.Media.ReadManyAsync(wanted.Distinct(StringComparer.Ordinal), token).ConfigureAwait(false);
            Dictionary<string, MediaAttachment> byId = media.ToDictionary(m => m.Id, StringComparer.Ordinal);
            Dictionary<string, string> conflicts = new Dictionary<string, string>();
            if (lesson.VideoMediaId != null) CheckMedia(actor, lesson, lesson.VideoMediaId, MediaKindEnum.Video, null, "videoMediaId", byId, conflicts);
            for (int i = 0; i < lesson.Resources.Count; i++)
                if (lesson.Resources[i].MediaId != null) CheckMedia(actor, lesson, lesson.Resources[i].MediaId!, MediaKindEnum.Document, "application/pdf", "resources[" + i + "].mediaId", byId, conflicts);
            for (int i = 0; i < lesson.Captions.Count; i++)
                CheckMedia(actor, lesson, lesson.Captions[i].MediaId, MediaKindEnum.Document, "text/vtt", "captions[" + i + "].mediaId", byId, conflicts);
            if (conflicts.Count > 0) throw new ApiErrorException(409, "MediaNotAttachable", "Some media cannot be attached to this lesson.", conflicts);
            if (lesson.VideoMediaId != null && byId.TryGetValue(lesson.VideoMediaId, out MediaAttachment? video) && video.Status == MediaStatusEnum.Ready)
                lesson.DurationSeconds = (int)Math.Round(video.DurationSeconds ?? 0);
            else if (lesson.VideoMediaId == null) lesson.DurationSeconds = 0;
            if (present.Contains("markers") && request.Markers != null)
            {
                int max = _Services.Settings.Library.MaxMarkersPerLesson;
                if (request.Markers.Count > max) errors["markers"] = "At most " + max + " markers.";
                lesson.Markers = new List<LessonMarker>();
                for (int i = 0; i < request.Markers.Count && i < max; i++)
                {
                    LessonMarkerInput input = request.Markers[i] ?? new LessonMarkerInput();
                    string label = MarkdownNormalizer.IsWellFormed(input.Label) ? MarkdownNormalizer.NormalizeSingleLine(input.Label ?? String.Empty) : String.Empty;
                    if (label.Length < 1 || label.Length > MaxMarkerLabelLength) errors["markers[" + i + "].label"] = "Use 1 to " + MaxMarkerLabelLength + " characters.";
                    if (input.Seconds < 0 || (lesson.DurationSeconds > 0 && input.Seconds > lesson.DurationSeconds)) errors["markers[" + i + "].seconds"] = "Use 0 to the lesson duration.";
                    lesson.Markers.Add(new LessonMarker { Id = IdGenerator.Generate(LibraryIdPrefixes.Marker), LessonId = lesson.Id, Seconds = input.Seconds, Label = label });
                }
                lesson.Markers = lesson.Markers.OrderBy(m => m.Seconds).ToList();
            }
            if (errors.Count > 0) throw ApiErrorException.Validation(errors);
            if (present.Contains("levels")) lesson.Levels = await _Services.Library.Config.ValidateLevelsAsync(request.Levels, token).ConfigureAwait(false);
            return media;
        }

        private static void CheckMedia(ContentActor actor, Lesson lesson, string mediaId, MediaKindEnum kind, string? contentType, string field, Dictionary<string, MediaAttachment> byId, Dictionary<string, string> conflicts)
        {
            if (!byId.TryGetValue(mediaId, out MediaAttachment? media) || media.IsDeleted) { conflicts[field] = "Unknown media."; return; }
            if (media.Kind != kind || (contentType != null && !String.Equals(media.ContentType, contentType, StringComparison.OrdinalIgnoreCase)))
            {
                conflicts[field] = "Use " + (kind == MediaKindEnum.Video ? "a video" : contentType == "text/vtt" ? "a WebVTT document" : "a PDF document") + ".";
                return;
            }
            if (media.Status == MediaStatusEnum.Failed || media.Status == MediaStatusEnum.Uploading) { conflicts[field] = "The media is not uploaded or failed processing."; return; }
            if (kind == MediaKindEnum.Document && media.Status != MediaStatusEnum.Ready) { conflicts[field] = "The document is still processing."; return; }
            bool mine = String.Equals(media.OwnerUserId, actor.UserId, StringComparison.Ordinal) || actor.IsSystemAdmin;
            bool here = media.AttachedTargetType == TargetTypeEnum.Lesson && String.Equals(media.AttachedTargetId, lesson.Id, StringComparison.Ordinal);
            if (!mine && !here) { conflicts[field] = "Use media you uploaded."; return; }
            if (media.AttachedTargetId != null && !here) conflicts[field] = "The media is attached to something else.";
        }

        private static ApiErrorException SlugInUse()
        {
            return new ApiErrorException(409, "SlugInUse", "Another lesson in this series already uses this slug.", new Dictionary<string, string> { { "slug", "Choose another slug." } });
        }

        #endregion
    }
}
