namespace TrellisKit.Server.Routes
{
    using System;
    using System.Collections.Generic;
    using System.Threading.Tasks;
    using TrellisKit.Core;
    using TrellisKit.Core.Enumeration;
    using TrellisKit.Core.Requests;
    using TrellisKit.Server.Services;
    using TrellisKit.Server.Services.Metrics;
    using WatsonWebserver;
    using WatsonWebserver.Core;
    using static TrellisKit.Server.Routes.OpenApiExtensions;

    /// <summary>
    /// Library staff routes (docs/PHASE4_CONTRACT.md section 5.10 and resolved decisions 3 and 17): series and lesson
    /// management for SystemAdmin and LibraryEditor holders, staff viewing history (audited), metrics, and the
    /// SystemAdmin-only library settings and levels. The route group needs a session; the service checks the role.
    /// </summary>
    public class AdminLibraryRoutes
    {
        #region Private-Members

        private const string _Tag = "Admin library";
        private const string _Series = Constants.ApiPrefix + "/admin/series";
        private const string _Lessons = Constants.ApiPrefix + "/admin/lessons";
        private const string _Library = Constants.ApiPrefix + "/admin/library";
        private const string _SeriesView = "Series";
        private const string _LessonView = "Lesson (with mediaStatus and moderationState for staff)";
        private const string _Staff = " Library staff (SystemAdmin or LibraryEditor); 403 Forbidden otherwise.";
        private const string _Admin = " SystemAdmin only; 403 Forbidden otherwise.";
        private readonly PropertyDirectory _Directory;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="directory">Property directory.</param>
        /// <exception cref="ArgumentNullException">The directory is null.</exception>
        public AdminLibraryRoutes(PropertyDirectory directory)
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
            RegisterSeries(post);
            RegisterLessons(post);
            RegisterLibrary(post);
        }

        #endregion

        #region Private-Methods

        private void RegisterSeries(WatsonWebserver.Core.Routing.RoutingGroup post)
        {
            EnumerationRoutes.Add(post, _Series, LibraryCollections.AdminSeries, _SeriesView,
                () => Doc("List series (staff)", _Tag, "Every undeleted series in any status. Filters levels, status, audience." + _Staff),
                (ctx, query) => RunAsync(ctx, actor => _Directory.For(ctx).Library.Series.ListAdminAsync(actor, query, ctx.Token)));
            post.Static.Add(HttpMethod.POST, _Series, async ctx =>
            {
                SeriesRequest request = RouteHelper.ReadJson<SeriesRequest>(ctx);
                ContentActor actor = await ActorAsync(ctx).ConfigureAwait(false);
                await RouteHelper.SendJsonAsync(ctx, 201, await _Directory.For(ctx).Library.Series.CreateAsync(actor, Info(ctx), request, ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
            }, RouteHelper.HandleExceptionAsync, openApiMetadata: Doc("Create a series", _Tag, "Body { title, slug?, summary, description, coverMediaId, levels, audience (Public only for SystemAdmin; default Members when the library is gated, else access.defaultAudience), labels, tags }. Created as Draft at the end of the order. Audited series.create." + _Staff)
                .Body("SeriesRequest").Returns(_SeriesView, 201).Resp(400, "ValidationFailed or UnknownOption").Resp(403, "Forbidden").Resp(409, "SlugInUse or SeriesLimitReached"));
            post.Static.Add(HttpMethod.PUT, _Series + "/order", async ctx =>
            {
                LibraryOrderRequest request = RouteHelper.ReadJson<LibraryOrderRequest>(ctx);
                ContentActor actor = await ActorAsync(ctx).ConfigureAwait(false);
                await RouteHelper.SendJsonAsync(ctx, 200, await _Directory.For(ctx).Library.Series.OrderAsync(actor, Info(ctx), request, ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
            }, RouteHelper.HandleExceptionAsync, openApiMetadata: Doc("Order series", _Tag, "Body { seriesIds }: every non-archived series exactly once; sort orders become 10, 20, 30. Audited series.order." + _Staff)
                .Body("{ seriesIds }").Returns("{ updated }").Resp(400, "ValidationFailed").Resp(403, "Forbidden"));
            post.Parameter.Add(HttpMethod.GET, _Series + "/{id}", async ctx =>
            {
                ContentActor actor = await ActorAsync(ctx).ConfigureAwait(false);
                await RouteHelper.SendJsonAsync(ctx, 200, await _Directory.For(ctx).Library.Series.GetAdminAsync(actor, RouteHelper.Param(ctx, "id"), ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
            }, RouteHelper.HandleExceptionAsync, openApiMetadata: Doc("Read a series (staff)", _Tag, "Any status." + _Staff).PathParam("id", "Series identifier").Returns(_SeriesView).Resp(403, "Forbidden").Resp(404, "NotFound"));
            post.Parameter.Add(HttpMethod.PUT, _Series + "/{id}", async ctx =>
            {
                SeriesRequest request = RouteHelper.ReadJson<SeriesRequest>(ctx, out HashSet<string> present);
                ContentActor actor = await ActorAsync(ctx).ConfigureAwait(false);
                await RouteHelper.SendJsonAsync(ctx, 200, await _Directory.For(ctx).Library.Series.UpdateAsync(actor, Info(ctx), RouteHelper.Param(ctx, "id"), request, present, ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
            }, RouteHelper.HandleExceptionAsync, openApiMetadata: Doc("Edit a series", _Tag, "Any create field (omitted fields stay). Narrowing the audience narrows the lessons that had the old audience unless narrowLessons is false. A changed slug keeps redirecting. Audited series.update." + _Staff)
                .PathParam("id", "Series identifier").Body("SeriesRequest").Returns(_SeriesView).Resp(400, "ValidationFailed or UnknownOption").Resp(403, "Forbidden").Resp(404, "NotFound").Resp(409, "SlugInUse"));
            post.Parameter.Add(HttpMethod.DELETE, _Series + "/{id}", async ctx =>
            {
                ContentActor actor = await ActorAsync(ctx).ConfigureAwait(false);
                await _Directory.For(ctx).Library.Series.DeleteAsync(actor, Info(ctx), RouteHelper.Param(ctx, "id"), ctx.Token).ConfigureAwait(false);
                await RouteHelper.SendEmptyAsync(ctx, 204).ConfigureAwait(false);
            }, RouteHelper.HandleExceptionAsync, openApiMetadata: Doc("Delete a series", _Tag, "Only when it has no lessons; archive it instead. Audited series.delete." + _Staff)
                .PathParam("id", "Series identifier").Resp(204, "Deleted").Resp(403, "Forbidden").Resp(404, "NotFound").Resp(409, "SeriesNotEmpty"));
            foreach (string action in new[] { "publish", "archive", "unarchive" })
            {
                string a = action;
                post.Parameter.Add(HttpMethod.POST, _Series + "/{id}/" + a, async ctx =>
                {
                    ContentActor actor = await ActorAsync(ctx).ConfigureAwait(false);
                    await RouteHelper.SendJsonAsync(ctx, 200, await _Directory.For(ctx).Library.Series.SetStatusAsync(actor, Info(ctx), RouteHelper.Param(ctx, "id"), a, ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
                }, RouteHelper.HandleExceptionAsync, openApiMetadata: Doc(Title(a) + " a series", _Tag, "Audited series." + a + "." + _Staff).PathParam("id", "Series identifier").Returns(_SeriesView).Resp(403, "Forbidden").Resp(404, "NotFound"));
            }
            post.Parameter.Add(HttpMethod.POST, _Series + "/{id}/lessons", async ctx =>
            {
                LessonRequest request = RouteHelper.ReadJson<LessonRequest>(ctx);
                ContentActor actor = await ActorAsync(ctx).ConfigureAwait(false);
                await RouteHelper.SendJsonAsync(ctx, 201, await _Directory.For(ctx).Library.Lessons.CreateAsync(actor, Info(ctx), RouteHelper.Param(ctx, "id"), request, ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
            }, RouteHelper.HandleExceptionAsync, openApiMetadata: Doc("Create a lesson", _Tag, "Body { title, slug?, summary, description, kind: Video | Article, videoMediaId, levels, audience (default the series audience), downloadable, commentsEnabled, notifyStaffOnComment, markers [{ seconds, label }], resources [{ kind: Document | Link | Reference, title, mediaId, url, text }], captions [{ language, label, mediaId }], labels, tags }. Created as Draft at the end of the series. Media must be uploaded by the caller (any for SystemAdmin); documents must be Ready. Audited lesson.create." + _Staff)
                .PathParam("id", "Series identifier").Body("LessonRequest").Returns(_LessonView, 201).Resp(400, "ValidationFailed or UnknownOption").Resp(403, "Forbidden").Resp(404, "NotFound").Resp(409, "SlugInUse, LessonLimitReached, or MediaNotAttachable"));
            post.Parameter.Add(HttpMethod.PUT, _Series + "/{id}/lessons/order", async ctx =>
            {
                LibraryOrderRequest request = RouteHelper.ReadJson<LibraryOrderRequest>(ctx);
                ContentActor actor = await ActorAsync(ctx).ConfigureAwait(false);
                await RouteHelper.SendJsonAsync(ctx, 200, await _Directory.For(ctx).Library.Lessons.OrderAsync(actor, Info(ctx), RouteHelper.Param(ctx, "id"), request, ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
            }, RouteHelper.HandleExceptionAsync, openApiMetadata: Doc("Order a series' lessons", _Tag, "Body { lessonIds }: every lesson of the series exactly once (bounded by library.maxLessonsPerSeries); positions become 1, 2, 3. Audited lesson.order." + _Staff)
                .PathParam("id", "Series identifier").Body("{ lessonIds }").Returns("{ updated }").Resp(400, "ValidationFailed").Resp(403, "Forbidden").Resp(404, "NotFound"));
        }

        private void RegisterLessons(WatsonWebserver.Core.Routing.RoutingGroup post)
        {
            EnumerationRoutes.Add(post, _Lessons, LibraryCollections.AdminLessons, "LessonSummary",
                () => Doc("List lessons (staff)", _Tag, "Every undeleted lesson. Filters seriesId, status (Draft, Scheduled, Processing, Published, Archived), hasVideo, mediaStatus, kind, levels, audience. Orderings CreatedDescending (default), PositionAscending, PublishedDescending, TitleAscending, DurationAscending." + _Staff),
                (ctx, query) => RunAsync(ctx, actor => _Directory.For(ctx).Library.Lessons.ListAdminAsync(actor, query, ctx.Token)));
            post.Parameter.Add(HttpMethod.GET, _Lessons + "/{id}", async ctx =>
            {
                ContentActor actor = await ActorAsync(ctx).ConfigureAwait(false);
                await RouteHelper.SendJsonAsync(ctx, 200, await _Directory.For(ctx).Library.Lessons.GetAdminAsync(actor, RouteHelper.Param(ctx, "id"), ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
            }, RouteHelper.HandleExceptionAsync, openApiMetadata: Doc("Read a lesson (staff)", _Tag, "Any status, with mediaStatus." + _Staff).PathParam("id", "Lesson identifier").Returns(_LessonView).Resp(403, "Forbidden").Resp(404, "NotFound"));
            post.Parameter.Add(HttpMethod.PUT, _Lessons + "/{id}", async ctx =>
            {
                LessonRequest request = RouteHelper.ReadJson<LessonRequest>(ctx, out HashSet<string> present);
                ContentActor actor = await ActorAsync(ctx).ConfigureAwait(false);
                await RouteHelper.SendJsonAsync(ctx, 200, await _Directory.For(ctx).Library.Lessons.UpdateAsync(actor, Info(ctx), RouteHelper.Param(ctx, "id"), request, present, ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
            }, RouteHelper.HandleExceptionAsync, openApiMetadata: Doc("Edit a lesson", _Tag, "Any create field; markers, resources, and captions replace their lists when present. Attaching a video that is not Ready makes a published lesson Processing until it is. Replacing the video resets nobody's progress and clamps positions past the new duration. Audited lesson.update." + _Staff)
                .PathParam("id", "Lesson identifier").Body("LessonRequest").Returns(_LessonView).Resp(400, "ValidationFailed or UnknownOption").Resp(403, "Forbidden").Resp(404, "NotFound").Resp(409, "SlugInUse or MediaNotAttachable"));
            post.Parameter.Add(HttpMethod.DELETE, _Lessons + "/{id}", async ctx =>
            {
                ContentActor actor = await ActorAsync(ctx).ConfigureAwait(false);
                await _Directory.For(ctx).Library.Lessons.DeleteAsync(actor, Info(ctx), RouteHelper.Param(ctx, "id"), ctx.Token).ConfigureAwait(false);
                await RouteHelper.SendEmptyAsync(ctx, 204).ConfigureAwait(false);
            }, RouteHelper.HandleExceptionAsync, openApiMetadata: Doc("Delete a lesson", _Tag, "Soft delete; progress is kept for export until purge; download licenses are revoked. Audited lesson.delete." + _Staff)
                .PathParam("id", "Lesson identifier").Resp(204, "Deleted").Resp(403, "Forbidden").Resp(404, "NotFound"));
            AddLessonAction(post, "/publish", "Publish a lesson", "Published at once for articles and Ready videos; Processing (published automatically when the video is Ready) otherwise. Publishing notifies the series' followers. Audited lesson.publish.", "LessonVideoRequired",
                (ctx, actor) => _Directory.For(ctx).Library.Lessons.PublishAsync(actor, Info(ctx), RouteHelper.Param(ctx, "id"), ctx.Token));
            AddLessonAction(post, "/schedule", "Schedule a lesson", "Body { publishAtUtc }. LibraryPublishWorker publishes it when due and its video is Ready. Audited lesson.schedule.", "ValidationFailed or LessonVideoRequired",
                async (ctx, actor) => await _Directory.For(ctx).Library.Lessons.ScheduleAsync(actor, Info(ctx), RouteHelper.Param(ctx, "id"), RouteHelper.ReadJson<LessonScheduleRequest>(ctx), ctx.Token).ConfigureAwait(false), "LessonScheduleRequest");
            AddLessonAction(post, "/archive", "Archive a lesson", "Leaves every list and search; members with progress keep it for library.archivedPlaybackDays. Audited lesson.archive.", null,
                (ctx, actor) => _Directory.For(ctx).Library.Lessons.SetArchivedAsync(actor, Info(ctx), RouteHelper.Param(ctx, "id"), true, ctx.Token));
            AddLessonAction(post, "/unarchive", "Unarchive a lesson", "Back to Published (or Draft when it was never published). Audited lesson.unarchive.", null,
                (ctx, actor) => _Directory.For(ctx).Library.Lessons.SetArchivedAsync(actor, Info(ctx), RouteHelper.Param(ctx, "id"), false, ctx.Token));
            AddLessonAction(post, "/move", "Move a lesson", "Body { seriesId, position } (position 1-based, default last). Audited lesson.move.", "ValidationFailed",
                async (ctx, actor) => await _Directory.For(ctx).Library.Lessons.MoveAsync(actor, Info(ctx), RouteHelper.Param(ctx, "id"), RouteHelper.ReadJson<LessonMoveRequest>(ctx), ctx.Token).ConfigureAwait(false), "LessonMoveRequest");
            EnumerationRoutes.Add(post, _Lessons + "/{id}/viewers", LibraryCollections.LessonViewers, "{ user: UserSummary, progress: LessonProgress, plays, labels, tags }",
                () => Doc("List a lesson's viewers", _Tag, "Members with progress on the lesson, LastWatchedDescending. Filter completed. Every call is audited as library.viewingHistory.read (resolved decision 17; disclose in the privacy policy)." + _Staff),
                (ctx, query) => RunAsync(ctx, actor => _Directory.For(ctx).Library.Progress.LessonViewersAsync(actor, Info(ctx), RouteHelper.Param(ctx, "id"), query, ctx.Token)), "id");
        }

        private async Task MetricsAsync(HttpContextBase ctx)
        {
            ContentActor actor = await ActorAsync(ctx).ConfigureAwait(false);
            EnumerationQuery query = ctx.Request.Method == HttpMethod.GET ? RouteHelper.ReadEnumerationQuery<EnumerationQuery>(ctx) : RouteHelper.ReadEnumerationBody<EnumerationQuery>(ctx);
            int days = AggregateEnumeration.Days(query);
            ApplicationServices services = _Directory.For(ctx);
            TrellisKit.Core.Responses.LibraryMetrics summary = await services.Library.Progress.MetricsAsync(actor, days, ctx.Token).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 200, AggregateEnumeration.Daily(services, "library-daily", query, summary)).ConfigureAwait(false);
        }

        private void RegisterLibrary(WatsonWebserver.Core.Routing.RoutingGroup post)
        {
            EnumerationRoutes.Add(post, _Library + "/members/{userId}/progress", LibraryCollections.MemberProgress, "{ lesson: LessonSummary, progress: LessonProgress, plays, playedSeconds }",
                () => Doc("List a member's viewing history", _Tag, "The member's lesson progress with playback sessions, LastWatchedDescending. Filters completed, seriesId. Every call is audited as library.viewingHistory.read (resolved decision 17; disclose in the privacy policy)." + _Staff),
                (ctx, query) => RunAsync(ctx, actor => _Directory.For(ctx).Library.Progress.MemberHistoryAsync(actor, Info(ctx), RouteHelper.Param(ctx, "userId"), query, ctx.Token)), "userId");
            post.Static.Add(HttpMethod.GET, Constants.ApiPrefix + "/admin/metrics/library", MetricsAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Library metrics", _Tag, "Enumerate daily play and completion observations. Filters days (7 to 365, default 30), series. Active learners and bounded lesson/series previews accompany the page." + _Staff)
                    .Enumeration("LegacyMetricRow", "CreatedDescending, CreatedAscending", "days, series").Resp(400, "ValidationFailed").Resp(403, "Forbidden"));
            post.Static.Add(HttpMethod.POST, Constants.ApiPrefix + "/admin/metrics/library/enumerate", MetricsAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Enumerate library metrics", _Tag, "The same daily observation collection as GET." + _Staff)
                    .Body("EnumerationQuery", false).Enumeration("LegacyMetricRow", "CreatedDescending, CreatedAscending", "days, series", false).Resp(400, "ValidationFailed").Resp(403, "Forbidden"));
            post.Static.Add(HttpMethod.GET, _Library + "/settings", async ctx =>
            {
                ContentActor actor = await ActorAsync(ctx).ConfigureAwait(false);
                await RouteHelper.SendJsonAsync(ctx, 200, await _Directory.For(ctx).Library.Config.GetSettingsAsync(actor, ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
            }, RouteHelper.HandleExceptionAsync, openApiMetadata: Doc("Read library settings", _Tag, "{ levelSource, levelFieldKey, downloadsEnabled, completionThresholdPercent, publicSamplesEnabled, autoFollowOnProgress, commentsEnabledByDefault, downloadsNotice, updatedUtc } (effective values)." + _Staff)
                .Returns("LibrarySettings").Resp(403, "Forbidden"));
            post.Static.Add(HttpMethod.PUT, _Library + "/settings", async ctx =>
            {
                LibrarySettingsRequest request = RouteHelper.ReadJson<LibrarySettingsRequest>(ctx, out HashSet<string> present);
                ContentActor actor = await ActorAsync(ctx).ConfigureAwait(false);
                await RouteHelper.SendJsonAsync(ctx, 200, await _Directory.For(ctx).Library.Config.UpdateSettingsAsync(actor, Info(ctx), request, present, ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
            }, RouteHelper.HandleExceptionAsync, openApiMetadata: Doc("Edit library settings", _Tag, "Body { levelSource, levelFieldKey, downloadsEnabled, completionThresholdPercent, publicSamplesEnabled, autoFollowOnProgress, commentsEnabledByDefault } (omitted fields stay; null clears a property override). Audited library.settings.update." + _Admin)
                .Body("LibrarySettingsRequest").Returns("LibrarySettings").Resp(400, "ValidationFailed").Resp(403, "Forbidden"));
            EnumerationRoutes.Add(post, _Library + "/levels", LibraryCollections.AdminLevels, "LibraryLevel { id, key, label, color, sortOrder, isRetired, labels, tags }",
                () => Doc("List library levels (admin)", _Tag, "The configured levels; filter includeRetired." + _Admin),
                (ctx, query) => RunAsync(ctx, actor => _Directory.For(ctx).Library.Config.EnumerateLevelsAsync(actor, query, true, ctx.Token)));
            post.Static.Add(HttpMethod.POST, _Library + "/levels", async ctx =>
            {
                LibraryLevelRequest request = RouteHelper.ReadJson<LibraryLevelRequest>(ctx);
                ContentActor actor = await ActorAsync(ctx).ConfigureAwait(false);
                await RouteHelper.SendJsonAsync(ctx, 201, await _Directory.For(ctx).Library.Config.CreateLevelAsync(actor, Info(ctx), request, ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
            }, RouteHelper.HandleExceptionAsync, openApiMetadata: Doc("Create a library level", _Tag, "Body { key, label, color, sortOrder }. Only when library.levelSource is Custom (409 LevelSourceNotCustom otherwise); at most 50 levels." + _Admin)
                .Body("LibraryLevelRequest").Returns("LibraryLevel", 201).Resp(400, "ValidationFailed").Resp(403, "Forbidden").Resp(409, "Conflict, LevelLimitReached, or LevelSourceNotCustom"));
            post.Static.Add(HttpMethod.PUT, _Library + "/levels/order", async ctx =>
            {
                LibraryOrderRequest request = RouteHelper.ReadJson<LibraryOrderRequest>(ctx);
                ContentActor actor = await ActorAsync(ctx).ConfigureAwait(false);
                await RouteHelper.SendJsonAsync(ctx, 200, await _Directory.For(ctx).Library.Config.OrderLevelsAsync(actor, Info(ctx), request, ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
            }, RouteHelper.HandleExceptionAsync, openApiMetadata: Doc("Order library levels", _Tag, "Body { levelIds }: every custom level exactly once; sort orders become 10, 20, 30. Audited library.level.order." + _Admin)
                .Body("{ levelIds }").Returns("{ updated }").Resp(400, "ValidationFailed").Resp(403, "Forbidden").Resp(409, "LevelSourceNotCustom"));
            post.Parameter.Add(HttpMethod.PUT, _Library + "/levels/{id}", async ctx =>
            {
                LibraryLevelRequest request = RouteHelper.ReadJson<LibraryLevelRequest>(ctx);
                ContentActor actor = await ActorAsync(ctx).ConfigureAwait(false);
                await RouteHelper.SendJsonAsync(ctx, 200, await _Directory.For(ctx).Library.Config.UpdateLevelAsync(actor, Info(ctx), RouteHelper.Param(ctx, "id"), request, ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
            }, RouteHelper.HandleExceptionAsync, openApiMetadata: Doc("Edit a library level", _Tag, "Body { label, color, sortOrder, isRetired } (the key is fixed)." + _Admin)
                .PathParam("id", "Level identifier").Body("LibraryLevelRequest").Returns("LibraryLevel").Resp(400, "ValidationFailed").Resp(403, "Forbidden").Resp(404, "NotFound").Resp(409, "LevelSourceNotCustom"));
            post.Parameter.Add(HttpMethod.DELETE, _Library + "/levels/{id}", async ctx =>
            {
                ContentActor actor = await ActorAsync(ctx).ConfigureAwait(false);
                await _Directory.For(ctx).Library.Config.DeleteLevelAsync(actor, Info(ctx), RouteHelper.Param(ctx, "id"), ctx.Token).ConfigureAwait(false);
                await RouteHelper.SendEmptyAsync(ctx, 204).ConfigureAwait(false);
            }, RouteHelper.HandleExceptionAsync, openApiMetadata: Doc("Delete a library level", _Tag, "Objects keep the key (it renders as the key)." + _Admin)
                .PathParam("id", "Level identifier").Resp(204, "Deleted").Resp(403, "Forbidden").Resp(404, "NotFound").Resp(409, "LevelSourceNotCustom"));
        }

        private void AddLessonAction<T>(WatsonWebserver.Core.Routing.RoutingGroup post, string suffix, string summary, string description, string? badRequest, Func<HttpContextBase, ContentActor, Task<T>> action, string? body = null) where T : class
        {
            WatsonWebserver.Core.OpenApi.OpenApiRouteMetadata doc = Doc(summary, _Tag, description + _Staff).PathParam("id", "Lesson identifier");
            if (body != null) doc = doc.Body(body);
            doc = doc.Returns(_LessonView).Resp(403, "Forbidden").Resp(404, "NotFound");
            if (badRequest != null) doc = doc.Resp(400, badRequest);
            post.Parameter.Add(HttpMethod.POST, _Lessons + "/{id}" + suffix, async ctx =>
            {
                ContentActor actor = await ActorAsync(ctx).ConfigureAwait(false);
                await RouteHelper.SendJsonAsync(ctx, 200, await action(ctx, actor).ConfigureAwait(false)).ConfigureAwait(false);
            }, RouteHelper.HandleExceptionAsync, openApiMetadata: doc);
        }

        private static string Title(string action)
        {
            return Char.ToUpperInvariant(action[0]) + action.Substring(1);
        }

        private RequestInfo Info(HttpContextBase ctx)
        {
            return RequestInfo.From(ctx, _Directory.For(ctx).Settings.Rest);
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
