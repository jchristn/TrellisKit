namespace TrellisKit.Server.Routes
{
    using System;
    using System.Threading.Tasks;
    using TrellisKit.Core;
    using TrellisKit.Core.Enumeration;
    using TrellisKit.Core.Requests;
    using TrellisKit.Server.Services;
    using WatsonWebserver;
    using WatsonWebserver.Core;
    using WatsonWebserver.Core.Routing;
    using static TrellisKit.Server.Routes.OpenApiExtensions;

    /// <summary>
    /// Training hub routes (docs/TRELLISKIT.md section 15; REST_API.md, Training hub, Library search, and Lesson
    /// techniques): the session-only overview, unified library search, and lesson and catalog links (staff write, member
    /// and public reads).
    /// </summary>
    public sealed class TrainingRoutes
    {
        #region Private-Members

        private const string _Tag = "Training";
        private const string _LinksTag = "Lesson techniques";
        private const string _Technique = "LessonCatalogEntryView (a CatalogEntry view plus linkId, lessonId, position, startSeconds, source, warning, linkLabels, linkTags)";
        private const string _Lesson = "LessonSummary";
        private const string _Catalog = " 404 NotAvailable when features.catalog or features.library is false; 503 FeatureDisabled under the catalog or library kill switch.";
        private readonly PropertyDirectory _Directory;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="directory">Property directory.</param>
        /// <exception cref="ArgumentNullException">The directory is null.</exception>
        public TrainingRoutes(PropertyDirectory directory)
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
            RoutingGroup post = server.Routes.PostAuthentication;
            RoutingGroup pre = server.Routes.PreAuthentication;
            RegisterOverview(post);
            RegisterSearch(post);
            RegisterLinks(post, pre);
        }

        #endregion

        #region Private-Methods

        private void RegisterOverview(RoutingGroup post)
        {
            post.Static.Add(HttpMethod.GET, Constants.ApiPrefix + "/me/training", async ctx =>
            {
                ContentActor actor = await ActorAsync(ctx).ConfigureAwait(false);
                await RouteHelper.SendJsonAsync(ctx, 200, await _Directory.For(ctx).Training.Hub.OverviewAsync(actor, ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
            }, RouteHelper.HandleExceptionAsync, openApiMetadata: Doc("Training overview", _Tag, "Session only; signed-out clients do not call it and show the public Library instead. One round trip for the Training hub: sections [{ key (Overview, Library, Schedule, Programs, Progress), available, reason (FeatureOff, NoAccess, NoData, SignedOut) when unavailable, segments (Library only: Courses, Techniques, Saved) }] in that fixed order, and bounded cards: upNext (EnumerationResult of upcoming published events you RSVPed Going to, by start, at most training.upNextLimit, default 3, maximum 10), continue (lessons in progress, at most training.continueLimit, default 10, maximum 25), recentSaves (at most training.recentSavesLimit, default 6, maximum 20), newForYou (lessons published within training.newForYouDays in series you follow and have not started, at most training.newForYouLimit, default 6, maximum 20), rank { rank: EffectiveRank, next: RankNext | null, curriculum: Curriculum | null, showVerificationLabels } for the first Rank profile field (null while ranks.enabled is off or the property has no Rank field), coach { available, placement }, firstRun (true when you have no lesson progress, saves, or RSVPs; the response then carries no sample content), labels, tags. Library is available when any segment is: Courses (features.library), Techniques (features.catalog and at least one published entry), Saved (features.saves). Schedule is available when you have an upcoming RSVP or a check-in; Programs when a published curriculum is visible to you; Progress when you have lesson progress, a rank or promotion, or a check-in. A section provider that fails is logged, counted (trelliskit.training.provider.errors), and its sections are reported unavailable (NoData) without failing the request. 402 MembershipRequired in FullyGated properties without a membership (and in PartiallyGated when training is a gated feature). 404 NotAvailable when features.training is false or no section is available; 503 FeatureDisabled under the training kill switch.")
                .Bounded("TrainingOverview", "sections exactly 5; segments at most 3; upNext at most 10, continue at most 25, recentSaves at most 20, newForYou at most 20 (training.* settings), each an EnumerationResult; rank.curriculum.items at most 500; labels and tags empty")
                .Resp(401, "Unauthorized").Resp(402, "MembershipRequired").Resp(404, "NotAvailable").Resp(429, "RateLimited").Resp(503, "FeatureDisabled"));
        }

        private void RegisterSearch(RoutingGroup post)
        {
            post.Static.Add(HttpMethod.POST, Constants.ApiPrefix + "/library/search", async ctx =>
            {
                LibrarySearchRequest request = RouteHelper.ReadJson<LibrarySearchRequest>(ctx);
                ContentActor actor = await ActorAsync(ctx).ConfigureAwait(false);
                await RouteHelper.SendJsonAsync(ctx, 200, await _Directory.For(ctx).Training.Search.SearchAsync(actor, request, ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
            }, RouteHelper.HandleExceptionAsync, openApiMetadata: Doc("Search the library", "Library search", "One search across courses, lessons, and techniques. Body { query (2 to 100 characters), kinds (Series, Lesson, CatalogEntry; default all), maxResultsPerKind (1 to 25, default 10), labels, tags }. Returns { series: EnumerationResult<SeriesSummary>, lessons: EnumerationResult<LessonSummary>, techniques: EnumerationResult<CatalogEntry> }; kinds not requested are empty pages, and techniques is always empty while features.catalog is false (published entries only otherwise). Each kind runs under its own access rules (locked stubs for PartiallyGated free accounts) and continues through its own enumeration route with the same search. Signed in, or signed out where the library is browsable anonymously. Rate limited with the shared search limiter (search.perMinute). 404 NotAvailable when features.library is false; 503 FeatureDisabled under the library kill switch.")
                .Bounded("LibrarySearchResponse", "three EnumerationResult pages of at most maxResultsPerKind (25) each")
                .Body("LibrarySearchRequest").Returns("LibrarySearchResponse").Resp(400, "ValidationFailed").Resp(401, "Unauthorized").Resp(402, "MembershipRequired").Resp(404, "NotAvailable").Resp(429, "RateLimited").Resp(503, "FeatureDisabled"));
        }

        private void RegisterLinks(RoutingGroup post, RoutingGroup pre)
        {
            post.Parameter.Add(HttpMethod.PUT, Constants.ApiPrefix + "/admin/lessons/{id}/catalog-entries", async ctx =>
            {
                ApplicationServices services = _Directory.For(ctx);
                LessonCatalogEntriesRequest request = RouteHelper.ReadJson<LessonCatalogEntriesRequest>(ctx);
                ContentActor actor = await ActorAsync(ctx).ConfigureAwait(false);
                await RouteHelper.SendJsonAsync(ctx, 200, await services.Training.Links.SetAsync(actor, RequestInfo.From(ctx, services.Settings.Rest), RouteHelper.Param(ctx, "id"), request, ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
            }, RouteHelper.HandleExceptionAsync, openApiMetadata: Doc("Replace a lesson's techniques", _LinksTag, "SystemAdmin or LibraryEditor. Body { items: [{ catalogEntryId, startSeconds (0 to 86400, optional), position (1 to 50, optional; ties keep list order), source (Editor or Suggested, default Editor), labels, tags }] } with at most 50 items, each entry once; [] removes every link. Only published catalog entries can be linked. Returns the stored links in position order (positions renumbered 1 to n) as one EnumerationResult page. Audited as lesson.catalogEntries." + _Catalog)
                .PathParam("id", "Lesson identifier").Body("LessonCatalogEntriesRequest").Returns("EnumerationResult<" + _Technique + ">").Bounded("EnumerationResult<LessonCatalogEntryView>", "at most 50 links per lesson")
                .Resp(400, "ValidationFailed").Resp(403, "Forbidden").Resp(404, "NotFound (lesson or catalog entry) or NotAvailable").Resp(409, "CatalogEntryNotPublished"));
            EnumerationRoutes.Add(post, Constants.ApiPrefix + "/lessons/{id}/catalog-entries", TrainingCollections.LessonCatalogEntries, _Technique,
                () => Doc("List the techniques in a lesson", _LinksTag, "Catalog entries linked to a lesson you can see (a locked stub included), PositionAscending default (NameAscending, CreatedDescending, CreatedAscending), at most 50 per page. Members see published entries only; library staff also see unpublished ones with warning EntryNotPublished. Search covers entry name and summary; labels and tags filter the links." + _Catalog),
                async (ctx, query) => await _Directory.For(ctx).Training.Links.EntriesForLessonAsync(await ActorAsync(ctx).ConfigureAwait(false), RouteHelper.Param(ctx, "id"), query, ctx.Token).ConfigureAwait(false), "id");
            EnumerationRoutes.Add(post, Constants.ApiPrefix + "/catalog/entries/{id}/lessons", TrainingCollections.CatalogEntryLessons, _Lesson,
                () => Doc("List the lessons that teach a technique", _LinksTag, "Lessons linked to a published catalog entry, under your lesson access: lessons you cannot read in full appear as locked stubs (PartiallyGated free accounts) or not at all; archived and unpublished lessons never appear. PublishedDescending default (TitleAscending, CreatedDescending, CreatedAscending), at most 50 per page. Search covers lesson title and summary; labels and tags filter the lessons." + _Catalog),
                async (ctx, query) => await _Directory.For(ctx).Training.Links.LessonsForEntryAsync(await ActorAsync(ctx).ConfigureAwait(false), RouteHelper.Param(ctx, "id"), query, false, ctx.Token).ConfigureAwait(false), "id");
            EnumerationRoutes.Add(pre, Constants.ApiPrefix + "/public/catalog/entries/{id}/lessons", TrainingCollections.CatalogEntryLessons, _Lesson,
                () => Doc("List the public lessons that teach a technique", _LinksTag, "Anonymous. Public, published lessons linked to a published catalog entry, with public media paths and no viewer state, at most 50 per page. Rate limited per IP (public catalog limit)." + _Catalog),
                async (ctx, query) => await _Directory.For(ctx).Training.Links.LessonsForEntryAsync(Anonymous(ctx), RouteHelper.Param(ctx, "id"), query, true, ctx.Token).ConfigureAwait(false), "id");
        }

        private Task<ContentActor> ActorAsync(HttpContextBase ctx)
        {
            return _Directory.For(ctx).Actors.ResolveAsync(ctx, ctx.Token);
        }

        private ContentActor Anonymous(HttpContextBase ctx)
        {
            ApplicationServices services = _Directory.For(ctx);
            ContentActor actor = services.Actors.Anonymous(RequestInfo.From(ctx, services.Settings.Rest).SourceIp);
            services.Limits.EnforceIp(actor.SourceIp, "public-catalog", services.Settings.RateLimits.PublicFeedPerMinute, RateLimitService.Minute);
            ctx.Response.Headers["Cache-Control"] = "public, max-age=0, must-revalidate";
            return actor;
        }

        #endregion
    }
}
