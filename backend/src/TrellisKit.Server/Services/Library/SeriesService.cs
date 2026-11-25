namespace TrellisKit.Server.Services
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core.Enumeration;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Exceptions;
    using TrellisKit.Core.Helpers;
    using TrellisKit.Core.Models;
    using TrellisKit.Core.Requests;
    using TrellisKit.Core.Responses;

    /// <summary>
    /// Library series (docs/PHASE4_CONTRACT.md sections 5.1, 5.2, 5.7, and 5.10): member lists and details with locked
    /// stubs, follows, and the library staff routes (create, edit, order, publish, archive, delete).
    /// Thread safety: safe to call concurrently.
    /// </summary>
    public class SeriesService
    {
        #region Private-Members

        private readonly ApplicationServices _Services;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="services">Property services.</param>
        /// <exception cref="ArgumentNullException">The services are null.</exception>
        public SeriesService(ApplicationServices services)
        {
            _Services = services ?? throw new ArgumentNullException(nameof(services));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// POST /api/v1/series/enumerate: published series the caller may read, plus locked stubs.
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="query">Query.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Page of series.</returns>
        /// <exception cref="ApiErrorException">400 ValidationFailed, 429 RateLimited.</exception>
        public async Task<EnumerationResult<SeriesSummaryView>> ListAsync(ContentActor actor, EnumerationQuery? query, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            _Services.Media.EnforceRead(actor);
            EnumerationQuery q = query ?? new EnumerationQuery();
            SeriesFilter filter = ParseFilter(q, actor, false);
            filter.Audiences = LibraryAccess.Readable(actor);
            filter.StubAudiences = LibraryAccess.Stubs(actor);
            filter.LessonAudiences = filter.Audiences;
            EnumerationRequest request = _Services.Enumeration.Prepare(q, LibraryCollections.Series, actor.IsLibraryStaff, "series");
            EnumerationResult<LibrarySeries> page = await _Services.Database.Library.Catalog.EnumerateSeriesAsync(request, filter, token).ConfigureAwait(false);
            return page.With(await _Services.Library.Views.SeriesAsync<SeriesSummaryView>(page.Objects, actor, token).ConfigureAwait(false));
        }

        /// <summary>
        /// GET /api/v1/series/{idOrSlug}.
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="idOrSlug">Identifier or slug.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Series (a locked stub when the caller cannot read it).</returns>
        /// <exception cref="ApiErrorException">404 NotFound, 429 RateLimited.</exception>
        public async Task<SeriesView> GetAsync(ContentActor actor, string idOrSlug, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            _Services.Media.EnforceRead(actor);
            LibrarySeries series = await RequireVisibleAsync(actor, idOrSlug, token).ConfigureAwait(false);
            return (await _Services.Library.Views.SeriesAsync<SeriesView>(new[] { series }, actor, token).ConfigureAwait(false))[0];
        }

        /// <summary>
        /// PUT /api/v1/series/{id}/follow and DELETE (follow false).
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="seriesId">Series.</param>
        /// <param name="follow">Follow or unfollow.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Follow state.</returns>
        /// <exception cref="ApiErrorException">401, 402, 404 NotFound, 409 FollowLimitReached.</exception>
        public async Task<FollowStateView> FollowAsync(ContentActor actor, string seriesId, bool follow, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            string userId = actor.RequireUserId();
            LibrarySeries series = await RequireVisibleAsync(actor, seriesId, token).ConfigureAwait(false);
            if (!LibraryAccess.CanRead(actor, series.Audience)) throw LibraryAccess.Denied(actor, TargetTypeEnum.Series, series.Id);
            _Services.Limits.EnforceMember(actor, "library.follows", 120, RateLimitService.Minute);
            if (follow)
            {
                if (await _Services.Database.Library.Progress.CountFollowsAsync(userId, token).ConfigureAwait(false) >= _Services.Settings.Library.MaxSeries)
                    throw new ApiErrorException(409, "FollowLimitReached", "You follow the most series allowed (library.maxSeries).");
                await _Services.Database.Library.Progress.FollowAsync(new SeriesFollow { Id = IdGenerator.Generate(LibraryIdPrefixes.Follow), SeriesId = series.Id, UserId = userId, Source = SeriesFollowSourceEnum.Explicit, CreatedUtc = DateTime.UtcNow }, token).ConfigureAwait(false);
            }
            else
            {
                await _Services.Database.Library.Progress.UnfollowAsync(series.Id, userId, DateTime.UtcNow, token).ConfigureAwait(false);
            }
            return new FollowStateView { IsFollowed = follow };
        }

        /// <summary>
        /// Load a series the caller may at least see as a locked stub, by id or slug.
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="idOrSlug">Identifier or slug.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Series.</returns>
        /// <exception cref="ApiErrorException">404 NotFound.</exception>
        public async Task<LibrarySeries> RequireVisibleAsync(ContentActor actor, string idOrSlug, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            string key = (idOrSlug ?? String.Empty).Trim();
            LibrarySeries? series = key.Length == 0 ? null : key.StartsWith(LibraryIdPrefixes.Series, StringComparison.Ordinal)
                ? await _Services.Database.Library.Catalog.ReadSeriesAsync(key, token).ConfigureAwait(false)
                : await _Services.Database.Library.Catalog.ReadSeriesBySlugAsync(key, token).ConfigureAwait(false);
            if (series == null) throw ApiErrorException.NotFound("Series");
            if (actor.IsLibraryStaff) return series;
            if (series.Status == LibraryStatusEnum.Archived)
            {
                bool window = series.ArchivedUtc != null && series.ArchivedUtc >= DateTime.UtcNow.AddDays(-_Services.Settings.Library.ArchivedPlaybackDays);
                if (!window || String.IsNullOrEmpty(actor.UserId) || !await HasProgressAsync(actor.UserId, series.Id, token).ConfigureAwait(false)) throw ApiErrorException.NotFound("Series");
                return series;
            }
            if (series.Status != LibraryStatusEnum.Published) throw ApiErrorException.NotFound("Series");
            if (LibraryAccess.CanRead(actor, series.Audience) || LibraryAccess.ShowsLocked(actor, series.Audience)) return series;
            if (await HasReadableLessonAsync(actor, series.Id, token).ConfigureAwait(false)) return series;
            throw ApiErrorException.NotFound("Series");
        }

        #region Admin

        /// <summary>
        /// POST /api/v1/admin/series/enumerate (library staff).
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="query">Query.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Page.</returns>
        /// <exception cref="ApiErrorException">400 ValidationFailed, 403 Forbidden.</exception>
        public async Task<EnumerationResult<SeriesView>> ListAdminAsync(ContentActor actor, EnumerationQuery? query, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            LibraryAccess.RequireStaff(actor);
            EnumerationQuery q = query ?? new EnumerationQuery();
            SeriesFilter filter = ParseFilter(q, actor, true);
            EnumerationRequest request = _Services.Enumeration.Prepare(q, LibraryCollections.AdminSeries, true, "admin-series");
            EnumerationResult<LibrarySeries> page = await _Services.Database.Library.Catalog.EnumerateSeriesAsync(request, filter, token).ConfigureAwait(false);
            return page.With(await _Services.Library.Views.SeriesAsync<SeriesView>(page.Objects, actor, token).ConfigureAwait(false));
        }

        /// <summary>
        /// GET /api/v1/admin/series/{id} (library staff).
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="seriesId">Series.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Series.</returns>
        /// <exception cref="ApiErrorException">403 Forbidden, 404 NotFound.</exception>
        public async Task<SeriesView> GetAdminAsync(ContentActor actor, string seriesId, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            LibraryAccess.RequireStaff(actor);
            LibrarySeries series = await RequireAsync(seriesId, token).ConfigureAwait(false);
            return await ViewAsync(series, actor, token).ConfigureAwait(false);
        }

        /// <summary>
        /// POST /api/v1/admin/series (library staff).
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="info">Request information.</param>
        /// <param name="request">Body.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Series.</returns>
        /// <exception cref="ApiErrorException">400 ValidationFailed or UnknownOption, 403 Forbidden, 409 SlugInUse or SeriesLimitReached.</exception>
        public async Task<SeriesView> CreateAsync(ContentActor actor, RequestInfo info, SeriesRequest request, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            ArgumentNullException.ThrowIfNull(info);
            ArgumentNullException.ThrowIfNull(request);
            LibraryAccess.RequireStaff(actor);
            if (await _Services.Database.Library.Catalog.CountSeriesAsync(token).ConfigureAwait(false) >= _Services.Settings.Library.MaxSeries)
                throw new ApiErrorException(409, "SeriesLimitReached", "This community already has " + _Services.Settings.Library.MaxSeries + " series (library.maxSeries).");
            DateTime now = DateTime.UtcNow;
            LibrarySeries series = new LibrarySeries
            {
                Id = IdGenerator.Generate(LibraryIdPrefixes.Series),
                Audience = _Services.Access.Current.DefaultAudienceFor(FeatureKeyEnum.Library),
                Status = LibraryStatusEnum.Draft,
                CreatedByUserId = actor.UserId,
                CreatedUtc = now,
                UpdatedUtc = now
            };
            HashSet<string> present = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "title", "slug", "summary", "description", "coverMediaId", "levels", "audience" };
            await ApplyAsync(actor, series, request, present, true, token).ConfigureAwait(false);
            series.SortOrder = await _Services.Database.Library.Catalog.NextSeriesSortOrderAsync(token).ConfigureAwait(false);
            await _Services.Metadata.ValidateAsync(actor, request.Labels, request.Tags, true, token, TargetTypeEnum.Series).ConfigureAwait(false);
            try
            {
                series = await _Services.Database.Library.Catalog.CreateSeriesAsync(series, token).ConfigureAwait(false);
            }
            catch (DuplicateRecordException)
            {
                throw SlugInUse();
            }
            await AttachCoverAsync(series, token).ConfigureAwait(false);
            await SyncPublicCoverAsync(series.Id, token).ConfigureAwait(false);
            await _Services.Metadata.ApplyAsync(actor, TargetTypeEnum.Series, series.Id, request.Labels, request.Tags, token).ConfigureAwait(false);
            await _Services.Audit.WriteAsync(info, "series.create", TargetTypeEnum.Series.ToString(), series.Id, series.Title, token).ConfigureAwait(false);
            await _Services.Forums.Search.EnqueueAsync(TargetTypeEnum.Series, series.Id, token).ConfigureAwait(false);
            return await ViewAsync(series, actor, token).ConfigureAwait(false);
        }

        /// <summary>
        /// PUT /api/v1/admin/series/{id} (library staff): omitted fields stay; narrowing the audience narrows the
        /// lessons that had the old audience unless narrowLessons is false.
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="info">Request information.</param>
        /// <param name="seriesId">Series.</param>
        /// <param name="request">Body.</param>
        /// <param name="present">Fields present in the body.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Series.</returns>
        /// <exception cref="ApiErrorException">400 ValidationFailed or UnknownOption, 403 Forbidden, 404 NotFound, 409 SlugInUse.</exception>
        public async Task<SeriesView> UpdateAsync(ContentActor actor, RequestInfo info, string seriesId, SeriesRequest request, HashSet<string> present, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            ArgumentNullException.ThrowIfNull(info);
            ArgumentNullException.ThrowIfNull(request);
            ArgumentNullException.ThrowIfNull(present);
            LibraryAccess.RequireStaff(actor);
            LibrarySeries series = await RequireAsync(seriesId, token).ConfigureAwait(false);
            string previousSlug = series.Slug;
            ContentAudienceEnum before = series.Audience;
            await ApplyAsync(actor, series, request, present, false, token).ConfigureAwait(false);
            await _Services.Metadata.ValidateAsync(actor, request.Labels, request.Tags, true, token, TargetTypeEnum.Series).ConfigureAwait(false);
            series.UpdatedUtc = DateTime.UtcNow;
            bool narrowed = ForumAccessService.Rank(series.Audience) > ForumAccessService.Rank(before);
            ContentAudienceEnum? narrowFrom = narrowed && (request.NarrowLessons ?? true) ? before : null;
            try
            {
                series = await _Services.Database.Library.Catalog.UpdateSeriesAsync(series, previousSlug, narrowFrom, token).ConfigureAwait(false);
            }
            catch (DuplicateRecordException)
            {
                throw SlugInUse();
            }
            if (present.Contains("coverMediaId")) await AttachCoverAsync(series, token).ConfigureAwait(false);
            await SyncPublicCoverAsync(series.Id, token).ConfigureAwait(false);
            await _Services.Metadata.ApplyAsync(actor, TargetTypeEnum.Series, series.Id, present.Contains("labels") ? request.Labels : null, present.Contains("tags") ? request.Tags : null, token).ConfigureAwait(false);
            await _Services.Audit.WriteAsync(info, "series.update", TargetTypeEnum.Series.ToString(), series.Id, series.Title + (narrowFrom != null ? " (lessons narrowed to " + series.Audience + ")" : String.Empty), token).ConfigureAwait(false);
            await _Services.Forums.Search.EnqueueAsync(TargetTypeEnum.Series, series.Id, token).ConfigureAwait(false);
            if (narrowFrom != null) await _Services.Library.Lessons.ReindexSeriesAsync(series.Id, token).ConfigureAwait(false);
            return await ViewAsync(series, actor, token).ConfigureAwait(false);
        }

        /// <summary>
        /// POST /api/v1/admin/series/{id}/publish, /archive, /unarchive (library staff).
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="info">Request information.</param>
        /// <param name="seriesId">Series.</param>
        /// <param name="action">publish, archive, or unarchive.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Series.</returns>
        /// <exception cref="ApiErrorException">403 Forbidden, 404 NotFound.</exception>
        public async Task<SeriesView> SetStatusAsync(ContentActor actor, RequestInfo info, string seriesId, string action, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            ArgumentNullException.ThrowIfNull(info);
            LibraryAccess.RequireStaff(actor);
            LibrarySeries series = await RequireAsync(seriesId, token).ConfigureAwait(false);
            LibraryStatusEnum status = action == "archive" ? LibraryStatusEnum.Archived
                : action == "unarchive" ? (series.PublishedUtc != null ? LibraryStatusEnum.Published : LibraryStatusEnum.Draft)
                : LibraryStatusEnum.Published;
            await _Services.Database.Library.Catalog.SetSeriesStatusAsync(series.Id, status, DateTime.UtcNow, token).ConfigureAwait(false);
            await SyncPublicCoverAsync(series.Id, token).ConfigureAwait(false);
            await _Services.Audit.WriteAsync(info, "series." + action, TargetTypeEnum.Series.ToString(), series.Id, series.Title, token).ConfigureAwait(false);
            await _Services.Forums.Search.EnqueueAsync(TargetTypeEnum.Series, series.Id, token).ConfigureAwait(false);
            await _Services.Library.Lessons.ReindexSeriesAsync(series.Id, token).ConfigureAwait(false);
            return await ViewAsync(await RequireAsync(series.Id, token).ConfigureAwait(false), actor, token).ConfigureAwait(false);
        }

        /// <summary>
        /// DELETE /api/v1/admin/series/{id}: only when it has no lessons (409 SeriesNotEmpty; archive it instead).
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="info">Request information.</param>
        /// <param name="seriesId">Series.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        /// <exception cref="ApiErrorException">403 Forbidden, 404 NotFound, 409 SeriesNotEmpty.</exception>
        public async Task DeleteAsync(ContentActor actor, RequestInfo info, string seriesId, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            ArgumentNullException.ThrowIfNull(info);
            LibraryAccess.RequireStaff(actor);
            LibrarySeries series = await RequireAsync(seriesId, token).ConfigureAwait(false);
            if (!await _Services.Database.Library.Catalog.DeleteSeriesAsync(series.Id, DateTime.UtcNow, token).ConfigureAwait(false))
                throw new ApiErrorException(409, "SeriesNotEmpty", "This series still has lessons; archive it instead.");
            await _Services.Media.UnpublishTargetAsync(TargetTypeEnum.Series, series.Id, token).ConfigureAwait(false);
            await _Services.Media.DetachAsync(TargetTypeEnum.Series, series.Id, null, token).ConfigureAwait(false);
            await _Services.Favorites.TargetDeletedAsync(FavoriteTargetTypeEnum.Series, series.Id, token).ConfigureAwait(false);
            await _Services.Audit.WriteAsync(info, "series.delete", TargetTypeEnum.Series.ToString(), series.Id, series.Title, token).ConfigureAwait(false);
            await _Services.Forums.Search.EnqueueAsync(TargetTypeEnum.Series, series.Id, token).ConfigureAwait(false);
        }

        /// <summary>
        /// PUT /api/v1/admin/series/order: every undeleted, non-archived series exactly once.
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="info">Request information.</param>
        /// <param name="request">Body ({ seriesIds }).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Rows reordered.</returns>
        /// <exception cref="ApiErrorException">400 ValidationFailed, 403 Forbidden.</exception>
        public async Task<LibraryOrderResponse> OrderAsync(ContentActor actor, RequestInfo info, LibraryOrderRequest request, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            ArgumentNullException.ThrowIfNull(info);
            ArgumentNullException.ThrowIfNull(request);
            LibraryAccess.RequireStaff(actor);
            int max = _Services.Settings.Library.MaxSeries;
            List<string> ids = (request.SeriesIds ?? new List<string>()).Select(i => (i ?? String.Empty).Trim()).ToList();
            if (ids.Count > max) throw ApiErrorException.Field("seriesIds", "At most " + max + " series.");
            if (ids.Distinct(StringComparer.Ordinal).Count() != ids.Count) throw ApiErrorException.Field("seriesIds", "Each series may appear only once.");
            List<string> active = await _Services.Database.Library.Catalog.ActiveSeriesIdsAsync(max, token).ConfigureAwait(false);
            HashSet<string> activeSet = new HashSet<string>(active, StringComparer.Ordinal);
            List<string> unknown = ids.Where(i => !activeSet.Contains(i)).ToList();
            if (unknown.Count > 0) throw ApiErrorException.Field("seriesIds", "Unknown or archived series: " + String.Join(", ", unknown.Take(5)) + ".");
            if (ids.Count != active.Count) throw ApiErrorException.Field("seriesIds", "List every non-archived series exactly once (" + active.Count + " series).");
            int updated = await _Services.Database.Library.Catalog.ReorderSeriesAsync(ids, DateTime.UtcNow, token).ConfigureAwait(false);
            await _Services.Audit.WriteAsync(info, "series.order", TargetTypeEnum.Series.ToString(), null, updated + " series", token).ConfigureAwait(false);
            return new LibraryOrderResponse { Updated = updated };
        }

        /// <summary>
        /// Read an undeleted series for staff routes.
        /// </summary>
        /// <param name="seriesId">Series.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Series.</returns>
        /// <exception cref="ApiErrorException">404 NotFound.</exception>
        public async Task<LibrarySeries> RequireAsync(string seriesId, CancellationToken token = default)
        {
            LibrarySeries? series = String.IsNullOrWhiteSpace(seriesId) ? null : await _Services.Database.Library.Catalog.ReadSeriesAsync(seriesId.Trim(), token).ConfigureAwait(false);
            if (series == null) throw ApiErrorException.NotFound("Series");
            return series;
        }

        #endregion

        #endregion

        #region Private-Methods

        private async Task<SeriesView> ViewAsync(LibrarySeries series, ContentActor actor, CancellationToken token)
        {
            return (await _Services.Library.Views.SeriesAsync<SeriesView>(new[] { series }, actor, token).ConfigureAwait(false))[0];
        }

        private async Task<bool> HasProgressAsync(string userId, string seriesId, CancellationToken token)
        {
            EnumerationRequest request = _Services.Enumeration.Prepare(new EnumerationQuery { MaxResults = 1 }, LibraryCollections.MyProgress, false, "series-progress|" + userId);
            EnumerationResult<LessonProgress> page = await _Services.Database.Library.Progress.EnumerateAsync(request, new LessonProgressFilter { UserId = userId, SeriesId = seriesId }, token).ConfigureAwait(false);
            return page.Objects.Count > 0;
        }

        private async Task<bool> HasReadableLessonAsync(ContentActor actor, string seriesId, CancellationToken token)
        {
            EnumerationRequest request = _Services.Enumeration.Prepare(new EnumerationQuery { MaxResults = 1 }, LibraryCollections.SeriesLessons, false, "series-lessons|" + seriesId);
            LessonFilter filter = new LessonFilter { SeriesIds = new List<string> { seriesId }, Audiences = LibraryAccess.Readable(actor) };
            EnumerationResult<Lesson> page = await _Services.Database.Library.Catalog.EnumerateLessonsAsync(request, filter, token).ConfigureAwait(false);
            return page.Objects.Count > 0;
        }

        private SeriesFilter ParseFilter(EnumerationQuery q, ContentActor actor, bool admin)
        {
            Dictionary<string, string> errors = new Dictionary<string, string>();
            SeriesFilter filter = new SeriesFilter
            {
                Admin = admin,
                Levels = ForumFilters.List(q, "levels") ?? new List<string>(),
                Audience = q.FilterEnum<ContentAudienceEnum>("audience", errors)
            };
            if (admin)
            {
                filter.Statuses = ForumFilters.Enums<LibraryStatusEnum>(q, "status", errors);
            }
            else
            {
                if (q.FilterBool("followedOnly", errors) == true)
                {
                    if (String.IsNullOrEmpty(actor.UserId)) errors["filters.followedOnly"] = "Needs a member session.";
                    else filter.FollowedByUserId = actor.UserId;
                }
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

        private async Task ApplyAsync(ContentActor actor, LibrarySeries series, SeriesRequest request, HashSet<string> present, bool creating, CancellationToken token)
        {
            Dictionary<string, string> errors = new Dictionary<string, string>();
            if (present.Contains("title"))
            {
                string? title = LibraryText.Title(request.Title, "title", errors);
                if (title != null) series.Title = title;
            }
            if (present.Contains("slug") && !(creating && String.IsNullOrWhiteSpace(request.Slug)))
            {
                string slug = (request.Slug ?? String.Empty).Trim().ToLowerInvariant();
                if (!LibraryText.IsSlug(slug)) errors["slug"] = "Use 2 to 80 of a-z, 0-9, and -.";
                else series.Slug = slug;
            }
            if (creating && String.IsNullOrEmpty(series.Slug) && !String.IsNullOrEmpty(series.Title)) series.Slug = LibraryText.DeriveSlug(series.Title, "series");
            if (present.Contains("summary")) series.Summary = LibraryText.Optional(request.Summary, LibraryText.MaxSummaryLength, "summary", errors, false);
            if (present.Contains("description")) series.Description = LibraryText.Optional(request.Description, LibraryText.MaxSeriesDescriptionLength, "description", errors, true);
            if (present.Contains("coverMediaId"))
            {
                string? mediaId = String.IsNullOrWhiteSpace(request.CoverMediaId) ? null : request.CoverMediaId.Trim();
                if (mediaId != null)
                {
                    MediaAttachment? media = await _Services.Database.Media.ReadAsync(mediaId, token).ConfigureAwait(false);
                    if (media == null || media.IsDeleted || media.Kind != MediaKindEnum.Image || media.Status == MediaStatusEnum.Failed || media.Status == MediaStatusEnum.Uploading) errors["coverMediaId"] = "Use an uploaded image.";
                    else if (media.AttachedTargetId != null && !(media.AttachedTargetType == TargetTypeEnum.Series && media.AttachedTargetId == series.Id)) errors["coverMediaId"] = "The image is attached to something else.";
                }
                series.CoverMediaId = mediaId;
            }
            if (present.Contains("audience") && request.Audience != null)
            {
                if (request.Audience == ContentAudienceEnum.Public && !actor.IsSystemAdmin) errors["audience"] = "Only a SystemAdmin can make a series Public.";
                else series.Audience = request.Audience.Value;
            }
            if (creating && String.IsNullOrEmpty(series.Title)) errors.TryAdd("title", "Required (2 to " + LibraryText.MaxTitleLength + " characters).");
            if (errors.Count > 0) throw ApiErrorException.Validation(errors);
            if (present.Contains("levels")) series.Levels = await _Services.Library.Config.ValidateLevelsAsync(request.Levels, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Keep a series cover on the public media paths exactly while signed-out visitors may see the series (Public
        /// audience, Published, not deleted), so the catalog shows it to them; other covers are withdrawn.
        /// </summary>
        /// <param name="seriesId">Series.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        public async Task SyncPublicCoverAsync(string seriesId, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(seriesId)) return;
            LibrarySeries? series = await _Services.Database.Library.Catalog.ReadSeriesAsync(seriesId, token).ConfigureAwait(false);
            bool shouldBePublic = series != null && series.DeletedUtc == null && series.Audience == ContentAudienceEnum.Public && series.Status == LibraryStatusEnum.Published;
            await _Services.Media.SyncTargetPublicAsync(TargetTypeEnum.Series, seriesId, shouldBePublic, token).ConfigureAwait(false);
        }

        private async Task AttachCoverAsync(LibrarySeries series, CancellationToken token)
        {
            List<string> ids = series.CoverMediaId == null ? new List<string>() : new List<string> { series.CoverMediaId };
            await _Services.Media.AttachAsync(TargetTypeEnum.Series, series.Id, ids, token).ConfigureAwait(false);
        }

        private static ApiErrorException SlugInUse()
        {
            return new ApiErrorException(409, "SlugInUse", "Another series already uses this slug.", new Dictionary<string, string> { { "slug", "Choose another slug." } });
        }

        #endregion
    }
}
