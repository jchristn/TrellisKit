namespace TrellisKit.Server.Routes
{
    using System;
    using System.Globalization;
    using System.IO;
    using System.Threading.Tasks;
    using TrellisKit.Core;
    using TrellisKit.Core.Enumeration;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Helpers;
    using TrellisKit.Core.Integrations.Implementations;
    using TrellisKit.Core.Integrations.Models;
    using TrellisKit.Core.Requests;
    using TrellisKit.Server.Services;
    using WatsonWebserver;
    using WatsonWebserver.Core;
    using static TrellisKit.Server.Routes.OpenApiExtensions;

    /// <summary>
    /// Routes of the library (series, lessons, progress, downloads, document text) (docs/PHASE4_CONTRACT.md section 5):
    /// member series and lesson reads, progress, Continue watching, follows, downloads, levels, public sample lessons,
    /// and the signed download file route. Registers <see cref="AdminLibraryRoutes"/> too.
    /// </summary>
    public class LibraryRoutes
    {
        #region Private-Members

        private const string _Tag = "Library";
        private const string _PublicTag = "Public library";
        private const string _Series = Constants.ApiPrefix + "/series";
        private const string _Lessons = Constants.ApiPrefix + "/lessons";
        private const string _Me = Constants.ApiPrefix + "/me/library";
        private const string _Public = Constants.ApiPrefix + "/public/lessons";
        private const string _SeriesItem = "SeriesSummary { id, slug, title, summary, cover, levels [{ key, label, color }], status, sortOrder, lessonCount, totalDurationSeconds, myProgress { completedLessons, percent, lastLessonId, lastWatchedUtc } | null, isFollowed, audience, locked, labels, tags, viewer { canFollow, canEdit, canOpen, requires }, publishedUtc, createdUtc, updatedUtc }";
        private const string _LessonItem = "LessonSummary { id, series { id, slug, title }, position, slug, title, summary, kind, video { durationSeconds, posterThumbUrl, blurhash } | null, durationSeconds, hasResources, levels, status, publishAtUtc, audience, downloadable, commentsEnabled, commentCount, reactionTotal, myProgress, locked, labels, tags, viewer { canPlay, canDownload, canComment, canReact, canReport, canEdit, canPromote, canOpen, requires } }";
        private const string _Lesson = "Lesson (LessonSummary plus description, video Media, markers [{ id, seconds, label }], resources [{ id, kind, title, media, url, text }], captions [{ language, label, media }], commentTarget, reactionCounts, myReactions, previous, next, promotion)";
        private const string _Progress = "LessonProgress { id, lessonId, positionSeconds, maxPositionSeconds, watchedSeconds, percent, completed, completedUtc, lastWatchedUtc, labels, tags }";
        private const string _Download = "LessonDownload { id, lessonId, deviceId, platform, url, urlExpiresUtc, licenseExpiresUtc, sizeBytes, sha256, revokedUtc, revokeReason, lastRenewedUtc, createdUtc, labels, tags }";
        private const string _Gate = " The route needs only a session (or none for anonymous-eligible reads); each object decides: callers who cannot read an object in full get it as a locked stub (PartiallyGated free accounts) or not at all. 404 NotAvailable when features.library is false; 503 FeatureDisabled under the library kill switch.";
        private readonly PropertyDirectory _Directory;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="directory">Property directory.</param>
        /// <exception cref="ArgumentNullException">The directory is null.</exception>
        public LibraryRoutes(PropertyDirectory directory)
        {
            _Directory = directory ?? throw new ArgumentNullException(nameof(directory));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Register the routes.
        /// </summary>
        /// <param name="server">Webserver.</param>
        /// <exception cref="ArgumentNullException">The server is null.</exception>
        public void Register(Webserver server)
        {
            ArgumentNullException.ThrowIfNull(server);
            WatsonWebserver.Core.Routing.RoutingGroup post = server.Routes.PostAuthentication;
            WatsonWebserver.Core.Routing.RoutingGroup pre = server.Routes.PreAuthentication;
            RegisterSeries(post);
            RegisterLessons(post);
            RegisterMe(post);
            RegisterPublic(pre);
            TargetConversationRoutes.Register(post, _Directory, _Lessons, TargetTypeEnum.Lesson, _Tag, "a lesson");
            TargetPromotionRoutes.Register(post, _Directory, _Lessons, TargetTypeEnum.Lesson, _Tag);
            new AdminLibraryRoutes(_Directory).Register(server);
        }

        /// <summary>
        /// The playback platform of a request: ios or android from X-Client-Platform, otherwise web.
        /// </summary>
        /// <param name="ctx">HTTP context.</param>
        /// <returns>Platform label.</returns>
        public static string PlatformOf(HttpContextBase ctx)
        {
            ArgumentNullException.ThrowIfNull(ctx);
            string platform = (ctx.Request.RetrieveHeaderValue("X-Client-Platform") ?? String.Empty).Trim().ToLowerInvariant();
            if (platform == "ios" || platform == "android") return platform;
            string app = (ctx.Request.RetrieveHeaderValue("X-TK-App") ?? String.Empty).ToLowerInvariant();
            if (app.Contains("ios", StringComparison.Ordinal)) return "ios";
            if (app.Contains("android", StringComparison.Ordinal)) return "android";
            return "web";
        }

        #endregion

        #region Private-Methods

        private void RegisterSeries(WatsonWebserver.Core.Routing.RoutingGroup post)
        {
            EnumerationRoutes.Add(post, _Series, LibraryCollections.Series, _SeriesItem,
                () => Doc("List series", _Tag, "Published series the caller may read, plus locked stubs (and series the caller cannot read that hold sample lessons the caller may open). Orderings SortOrderAscending (default), TitleAscending, PublishedDescending, Relevance. Filters levels, followedOnly, progress (NotStarted, InProgress, Completed), audience. Search covers title, summary, description." + _Gate),
                (ctx, query) => RunAsync(ctx, actor => _Directory.For(ctx).Library.Series.ListAsync(actor, query, ctx.Token)));
            post.Parameter.Add(HttpMethod.GET, _Series + "/{idOrSlug}", async ctx =>
            {
                ContentActor actor = await ActorAsync(ctx).ConfigureAwait(false);
                await RouteHelper.SendJsonAsync(ctx, 200, await _Directory.For(ctx).Library.Series.GetAsync(actor, RouteHelper.Param(ctx, "idOrSlug"), ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
            }, RouteHelper.HandleExceptionAsync, openApiMetadata: Doc("Read a series", _Tag, "By identifier or slug (old slugs redirect). A locked stub keeps the summary fields; description is null." + _Gate)
                .PathParam("idOrSlug", "Series identifier or slug").Returns("Series (SeriesSummary plus description)").Resp(404, "NotFound"));
            EnumerationRoutes.Add(post, _Series + "/{id}/lessons", LibraryCollections.SeriesLessons, _LessonItem,
                () => Doc("List a series' lessons", _Tag, "Published lessons in position order (PositionAscending). A series the caller sees as a locked stub lists only the lessons the caller may open; otherwise Members lessons appear as locked stubs to free accounts." + _Gate),
                (ctx, query) => RunAsync(ctx, actor => _Directory.For(ctx).Library.Lessons.ListSeriesAsync(actor, RouteHelper.Param(ctx, "id"), query, ctx.Token)), "id");
            post.Parameter.Add(HttpMethod.PUT, _Series + "/{id}/follow", ctx => FollowAsync(ctx, true), RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Follow a series", _Tag, "Idempotent. Followers receive NewLessonInSeries (aggregated per series while unread). Signed-in callers who can read the series.")
                    .PathParam("id", "Series identifier").Returns("{ isFollowed }").Resp(401, "Unauthorized").Resp(402, "MembershipRequired").Resp(404, "NotFound").Resp(409, "FollowLimitReached"));
            post.Parameter.Add(HttpMethod.DELETE, _Series + "/{id}/follow", ctx => FollowAsync(ctx, false), RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Unfollow a series", _Tag, "Idempotent; automatic follows (library.autoFollowOnProgress) do not return after an unfollow.")
                    .PathParam("id", "Series identifier").Returns("{ isFollowed }").Resp(404, "NotFound"));
            EnumerationRoutes.Add(post, Constants.ApiPrefix + "/library/levels", LibraryCollections.Levels, "{ key, label, color, labels, tags }",
                () => Doc("List library levels", _Tag, "Levels from the configured source (library.levelSource: None, ProfileField, Custom), in order." + _Gate),
                (ctx, query) => RunAsync(ctx, actor => _Directory.For(ctx).Library.Config.EnumerateLevelsAsync(actor, query, false, ctx.Token)));
        }

        private void RegisterLessons(WatsonWebserver.Core.Routing.RoutingGroup post)
        {
            EnumerationRoutes.Add(post, _Lessons, LibraryCollections.Lessons, _LessonItem,
                () => Doc("List lessons", _Tag, "Published lessons across series, plus locked stubs. Orderings PublishedDescending (default), TitleAscending, DurationAscending, Relevance. Filters seriesIds (max 50), levels, kind, hasResources, minDurationSeconds, maxDurationSeconds, progress, audience." + _Gate),
                (ctx, query) => RunAsync(ctx, actor => _Directory.For(ctx).Library.Lessons.ListAsync(actor, query, ctx.Token)));
            post.Parameter.Add(HttpMethod.GET, _Lessons + "/{idOrSlug}", async ctx =>
            {
                ContentActor actor = await ActorAsync(ctx).ConfigureAwait(false);
                await RouteHelper.SendJsonAsync(ctx, 200, await _Directory.For(ctx).Library.Lessons.GetAsync(actor, RouteHelper.Param(ctx, "idOrSlug"), ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
            }, RouteHelper.HandleExceptionAsync, openApiMetadata: Doc("Read a lesson", _Tag, "By identifier or slug. A locked stub has no description, video, markers, resources, or captions. Archived lessons stay readable for members with progress for library.archivedPlaybackDays." + _Gate)
                .PathParam("idOrSlug", "Lesson identifier or slug").Returns(_Lesson).Resp(404, "NotFound"));
            post.Parameter.Add(HttpMethod.PUT, _Lessons + "/{id}/progress", async ctx =>
            {
                LessonProgressRequest request = RouteHelper.ReadJson<LessonProgressRequest>(ctx);
                ContentActor actor = await ActorAsync(ctx).ConfigureAwait(false);
                await RouteHelper.SendJsonAsync(ctx, 200, await _Directory.For(ctx).Library.Progress.UpdateAsync(actor, RouteHelper.Param(ctx, "id"), request, PlatformOf(ctx), ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
            }, RouteHelper.HandleExceptionAsync, openApiMetadata: Doc("Record lesson progress", _Tag, "Body { positionSeconds, playedSeconds, clientUpdatedUtc, sessionId }. positionSeconds is clamped to the duration; playedSeconds to 2 times library.progressIntervalSeconds (skipping does not count). Updates older than the stored clientUpdatedUtc keep the resume point; furthest position, watched seconds, and completion merge monotonically. Offline entries older than library.offlineReplayDays are dropped. Completion (sticky) at library.completionThresholdPercent of min(furthest position, watched). A position in the last library.resumeTailSeconds resumes from 0.")
                .PathParam("id", "Lesson identifier").Body("LessonProgressRequest").Returns(_Progress).Resp(400, "ValidationFailed").Resp(401, "Unauthorized").Resp(402, "MembershipRequired").Resp(404, "NotFound").Resp(429, "RateLimited (library.progressPerMinute, library.progressPerHour)"));
            post.Parameter.Add(HttpMethod.DELETE, _Lessons + "/{id}/progress", async ctx =>
            {
                ContentActor actor = await ActorAsync(ctx).ConfigureAwait(false);
                await _Directory.For(ctx).Library.Progress.ResetAsync(actor, RouteHelper.Param(ctx, "id"), ctx.Token).ConfigureAwait(false);
                await RouteHelper.SendEmptyAsync(ctx, 204).ConfigureAwait(false);
            }, RouteHelper.HandleExceptionAsync, openApiMetadata: Doc("Reset lesson progress", _Tag, "Deletes the caller's progress on the lesson.").PathParam("id", "Lesson identifier").Resp(204, "Reset").Resp(404, "NotFound"));
            AddComplete(post, "/complete", true, "Mark a lesson complete", "Completion is sticky: later progress never clears it.");
            AddComplete(post, "/uncomplete", false, "Mark a lesson not complete", "Clears completion; progress continues from the stored values.");
            post.Parameter.Add(HttpMethod.POST, _Lessons + "/{id}/downloads", async ctx =>
            {
                LessonDownloadRequest request = RouteHelper.ReadJson<LessonDownloadRequest>(ctx);
                ContentActor actor = await ActorAsync(ctx).ConfigureAwait(false);
                await RouteHelper.SendJsonAsync(ctx, 201, await _Directory.For(ctx).Library.Downloads.IssueAsync(actor, RouteHelper.Param(ctx, "id"), request, ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
            }, RouteHelper.HandleExceptionAsync, openApiMetadata: Doc("Download a lesson for offline viewing", _Tag, "Body { deviceId, platform: Ios | Android }. Needs library.downloads.enabled, a downloadable Video lesson with its download_720p rendition, and (in gated models) a membership. Returns a license with a short-lived signed url (library.downloads.urlMinutes) valid for library.downloads.licenseDays. Files are not DRM protected. 503 FeatureDisabled under the libraryDownloads kill switch.")
                .PathParam("id", "Lesson identifier").Body("LessonDownloadRequest").Returns(_Download, 201).Resp(400, "ValidationFailed").Resp(403, "DownloadsNotAllowed").Resp(404, "NotFound").Resp(409, "DownloadLimitReached").Resp(429, "RateLimited (library.downloads.perDay)"));
        }

        private void RegisterMe(WatsonWebserver.Core.Routing.RoutingGroup post)
        {
            EnumerationRoutes.Add(post, _Me + "/continue", LibraryCollections.Continue, "{ lesson: LessonSummary, progress: LessonProgress }",
                () => Doc("Continue watching", _Tag, "Lessons in progress, not completed, still openable (archived lessons for library.archivedPlaybackDays), LastWatchedDescending; default maxResults 10. Signed in."),
                (ctx, query) => RunAsync(ctx, actor => _Directory.For(ctx).Library.Progress.ContinueAsync(actor, query, ctx.Token)));
            EnumerationRoutes.Add(post, _Me + "/progress", LibraryCollections.MyProgress, _Progress,
                () => Doc("List your lesson progress", _Tag, "Your progress rows, LastWatchedDescending. Filters completed, seriesId. Signed in."),
                (ctx, query) => RunAsync(ctx, actor => _Directory.For(ctx).Library.Progress.MineAsync(actor, query, ctx.Token)));
            EnumerationRoutes.Add(post, _Me + "/downloads", LibraryCollections.Downloads, _Download,
                () => Doc("List your download licenses", _Tag, "Newest first. Filters activeOnly, deviceId. url is null here; renew a license for a fresh URL. Signed in."),
                (ctx, query) => RunAsync(ctx, actor => _Directory.For(ctx).Library.Downloads.ListAsync(actor, query, ctx.Token)));
            post.Parameter.Add(HttpMethod.POST, _Me + "/downloads/{id}/renew", async ctx =>
            {
                ContentActor actor = await ActorAsync(ctx).ConfigureAwait(false);
                await RouteHelper.SendJsonAsync(ctx, 200, await _Directory.For(ctx).Library.Downloads.RenewAsync(actor, RouteHelper.Param(ctx, "id"), ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
            }, RouteHelper.HandleExceptionAsync, openApiMetadata: Doc("Renew a download license", _Tag, "Extends the license by library.downloads.licenseDays with a fresh signed url while the member still qualifies; otherwise the license is revoked and 403 DownloadsNotAllowed tells the app to delete the file.")
                .PathParam("id", "License identifier").Returns(_Download).Resp(403, "DownloadsNotAllowed").Resp(404, "NotFound"));
            post.Parameter.Add(HttpMethod.DELETE, _Me + "/downloads/{id}", async ctx =>
            {
                ContentActor actor = await ActorAsync(ctx).ConfigureAwait(false);
                await _Directory.For(ctx).Library.Downloads.RevokeAsync(actor, RouteHelper.Param(ctx, "id"), ctx.Token).ConfigureAwait(false);
                await RouteHelper.SendEmptyAsync(ctx, 204).ConfigureAwait(false);
            }, RouteHelper.HandleExceptionAsync, openApiMetadata: Doc("Delete a download license", _Tag, "Revokes the license (the app deletes the file).").PathParam("id", "License identifier").Resp(204, "Revoked").Resp(404, "NotFound"));
        }

        private void RegisterPublic(WatsonWebserver.Core.Routing.RoutingGroup pre)
        {
            string description = "Public, published lessons of published series (PublishedDescending, maxResults at most 20) with description, markers, and public media URLs; no viewer state, progress, or comments. Cache-Control: public, max-age=60. 404 NotAvailable when features.library or library.publicSamplesEnabled is off. Rate limited per IP. Anonymous.";
            pre.Static.Add(HttpMethod.GET, _Public, ctx => SendPublicListAsync(ctx, RouteHelper.ReadEnumerationQuery<EnumerationQuery>(ctx)), RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("List public sample lessons", _PublicTag, description).Enumeration("PublicLesson", "PublishedDescending").Resp(400, "ValidationFailed").Resp(404, "NotAvailable").Resp(429, "RateLimited"));
            pre.Static.Add(HttpMethod.POST, _Public + "/enumerate", ctx => SendPublicListAsync(ctx, RouteHelper.ReadEnumerationBody<EnumerationQuery>(ctx)), RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Enumerate public sample lessons", _PublicTag, "EnumerationQuery body. " + description).Body("EnumerationQuery", false).Enumeration("PublicLesson", "PublishedDescending", String.Empty, false)
                    .Resp(400, "ValidationFailed").Resp(404, "NotAvailable").Resp(429, "RateLimited"));
            pre.Parameter.Add(HttpMethod.GET, _Public + "/{id}", async ctx =>
            {
                ContentActor actor = Anonymous(ctx);
                object lesson = await _Directory.For(ctx).Library.Lessons.GetPublicAsync(actor, RouteHelper.Param(ctx, "id"), ctx.Token).ConfigureAwait(false);
                ctx.Response.Headers["Cache-Control"] = PublicThreadRoutes.CacheControl;
                await RouteHelper.SendJsonAsync(ctx, 200, lesson).ConfigureAwait(false);
            }, RouteHelper.HandleExceptionAsync, openApiMetadata: Doc("Read a public sample lesson", _PublicTag, "One PublicLesson. Cache-Control: public, max-age=60. Anonymous.")
                .PathParam("id", "Lesson identifier or slug").Returns("PublicLesson").Resp(404, "NotFound or NotAvailable").Resp(429, "RateLimited"));
            pre.Parameter.Add(HttpMethod.GET, _Public + "/downloads/{id}", GetDownloadFileAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Download a lesson rendition", _PublicTag, "Signed URL from a download license (exp, sig). Streams download_720p.mp4 (Range supported). No Authorization header. 503 FeatureDisabled under the libraryDownloads kill switch.")
                    .PathParam("id", "License identifier").QueryParam("exp", "Unix expiry").QueryParam("sig", "Signature").File("video/mp4", "Rendition").Resp(206, "Partial content")
                    .Resp(403, "InvalidSignature or DownloadsNotAllowed").Resp(404, "NotFound").Resp(416, "RangeNotSatisfiable"));
        }

        private void AddComplete(WatsonWebserver.Core.Routing.RoutingGroup post, string suffix, bool completed, string summary, string description)
        {
            post.Parameter.Add(HttpMethod.POST, _Lessons + "/{id}" + suffix, async ctx =>
            {
                ContentActor actor = await ActorAsync(ctx).ConfigureAwait(false);
                await RouteHelper.SendJsonAsync(ctx, 200, await _Directory.For(ctx).Library.Progress.SetCompletedAsync(actor, RouteHelper.Param(ctx, "id"), completed, ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
            }, RouteHelper.HandleExceptionAsync, openApiMetadata: Doc(summary, _Tag, description).PathParam("id", "Lesson identifier").Returns(_Progress).Resp(401, "Unauthorized").Resp(402, "MembershipRequired").Resp(404, "NotFound"));
        }

        private async Task FollowAsync(HttpContextBase ctx, bool follow)
        {
            ContentActor actor = await ActorAsync(ctx).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 200, await _Directory.For(ctx).Library.Series.FollowAsync(actor, RouteHelper.Param(ctx, "id"), follow, ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task SendPublicListAsync(HttpContextBase ctx, EnumerationQuery query)
        {
            object page = await _Directory.For(ctx).Library.Lessons.ListPublicAsync(Anonymous(ctx), query, ctx.Token).ConfigureAwait(false);
            ctx.Response.Headers["Cache-Control"] = PublicThreadRoutes.CacheControl;
            await RouteHelper.SendJsonAsync(ctx, 200, page).ConfigureAwait(false);
        }

        private async Task GetDownloadFileAsync(HttpContextBase ctx)
        {
            ApplicationServices services = _Directory.For(ctx);
            LessonDownloadFile file = await services.Library.Downloads.ResolveFileAsync(RouteHelper.Param(ctx, "id"), RouteHelper.Query(ctx, "exp"), RouteHelper.Query(ctx, "sig"), ctx.Token).ConfigureAwait(false);
            string key = file.Key;
            if (services.Integrations.ObjectStore is LocalObjectStore local)
            {
                string? path = local.ResolveExisting(file.Location, key);
                if (path == null)
                {
                    await RouteHelper.SendErrorAsync(ctx, 404, "NotFound", "Download not found.").ConfigureAwait(false);
                    return;
                }
                if (file.IsOriginal) ctx.Response.Headers["Content-Disposition"] = "attachment; filename=\"" + file.FileName + "\"";
                await MediaRoutes.ServeFileAsync(ctx, path, file.FileName).ConfigureAwait(false);
                return;
            }
            ObjectHead? head = await services.Integrations.ObjectStore.HeadAsync(file.Location, key, ctx.Token).ConfigureAwait(false);
            if (head == null)
            {
                await RouteHelper.SendErrorAsync(ctx, 404, "NotFound", "Download not found.").ConfigureAwait(false);
                return;
            }
            ctx.Response.StatusCode = 200;
            ctx.Response.ContentType = file.ContentType;
            ctx.Response.Headers["Content-Disposition"] = "attachment; filename=\"" + file.FileName + "\"";
            ctx.Response.Headers["X-Content-Type-Options"] = "nosniff";
            ctx.Response.ChunkedTransfer = true;
            const int chunk = 8 * 1024 * 1024;
            for (long offset = 0; offset < head.SizeBytes; offset += chunk)
            {
                byte[] data = await services.Integrations.ObjectStore.ReadRangeAsync(file.Location, key, offset, (int)Math.Min(chunk, head.SizeBytes - offset), ctx.Token).ConfigureAwait(false);
                bool last = offset + data.Length >= head.SizeBytes;
                await ctx.Response.SendChunk(data, last, ctx.Token).ConfigureAwait(false);
            }
        }

        private ContentActor Anonymous(HttpContextBase ctx)
        {
            ApplicationServices services = _Directory.For(ctx);
            return new ContentActor { IsAnonymous = true, Access = services.Access.Current, SourceIp = RequestInfo.From(ctx, services.Settings.Rest).SourceIp };
        }

        private Task<ContentActor> ActorAsync(HttpContextBase ctx)
        {
            return _Directory.For(ctx).Actors.ResolveAsync(ctx, ctx.Token);
        }

        private async Task<object> RunAsync<T>(HttpContextBase ctx, Func<ContentActor, Task<T>> body) where T : class
        {
            ContentActor actor = await ActorAsync(ctx).ConfigureAwait(false);
            return await body(actor).ConfigureAwait(false);
        }

        #endregion
    }
}
