namespace TrellisKit.Server.Routes
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;
    using System.Threading.Tasks;
    using TrellisKit.Core;
    using TrellisKit.Core.Enumeration;
    using TrellisKit.Core.Requests;
    using TrellisKit.Core.Responses;
    using TrellisKit.Server.Services;
    using WatsonWebserver;
    using WatsonWebserver.Core;
    using WatsonWebserver.Core.OpenApi;
    using static TrellisKit.Server.Routes.OpenApiExtensions;

    /// <summary>
    /// Routes of the venue and instructor directory (venues, instructors, programs, submissions, geocoding) (docs/PHASE4_CONTRACT.md section 3).
    /// Registers the member routes here and the submission, admin, and public route classes.
    /// </summary>
    public class DirectoryRoutes
    {
        #region Private-Members

        private const string _Tag = "Directory";
        private const string _Venues = Constants.ApiPrefix + "/venues";
        private const string _Instructors = Constants.ApiPrefix + "/instructors";
        private const string _Base = Constants.ApiPrefix + "/directory";
        private const string _VenueSummary = "VenueSummary { id, name, slug, affiliation, summary, address, addressVisibility, geo { lat, lng, precision }, distanceKm, timeZone, phone, email, websiteUrl, programs [{ key, label }], visitorPolicy, visitorPolicyNote, coverPhoto, instructorCount, ratingAverage, ratingCount, mediaCount, isVerified, verifiedUtc, status, audience, locked, labels, tags, viewer }";
        private const string _InstructorSummary = "InstructorSummary { id, displayName, slug, photo, rank, rankLabel, rankColor, rankLevel, affiliation, linkedUser, venueCount, websiteUrl, role, status, audience, locked, labels, tags, viewer }";
        private const string _VenueReview = "VenueReview { id, venueId, author, rating, body, editedUtc, reply { body, author, instructorId, instructorName, createdUtc, editedUtc }, moderationState, labels, tags, viewer { isAuthor, canEdit, canDelete, canReply, canDeleteReply, canReport, canModerate }, createdUtc, updatedUtc }";
        private const string _VenueMediaItem = "VenueMediaItem { id, venueId, media, uploader, moderationState, labels, tags, viewer { isUploader, canRemove, canReport }, createdUtc, updatedUtc }";
        private const string _NotAvailable = "NotAvailable (features.directory is off)";
        private const string _Gate = " Object gated: signed-in callers (and anonymous callers for Public entries under anonymous browsing); in PartiallyGated a gated directory shows Members entries to free accounts as locked stubs (locked true, viewer { canOpen false, requires }). 503 FeatureDisabled under the directory kill switch.";
        private readonly PropertyDirectory _Directory;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="directory">Property directory.</param>
        /// <exception cref="ArgumentNullException">The directory is null.</exception>
        public DirectoryRoutes(PropertyDirectory directory)
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
            RegisterVenues(post);
            RegisterInstructors(post);
            RegisterOther(post);
            new DirectorySubmissionRoutes(_Directory).Register(server);
            new AdminDirectoryRoutes(_Directory).Register(server);
            new PublicDirectoryRoutes(_Directory).Register(server);
        }

        #endregion

        #region Private-Methods

        private void RegisterVenues(WatsonWebserver.Core.Routing.RoutingGroup post)
        {
            EnumerationRoutes.Add(post, _Venues, DirectoryCollections.Venues, _VenueSummary,
                () => Doc("List venues", _Tag, "Venues the caller may list. Orderings NameAscending (default without near), DistanceAscending (default with near; requires near), CreatedDescending, CreatedAscending, UpdatedDescending, Relevance (with search). " +
                    "Filters near { lat, lng, radiusKm } (radius 0.1 to directory.maxRadiusKm, default directory.defaultRadiusKm; GET form lat,lng,radiusKm), bbox { south, west, north, east } (west greater than east crosses the antimeridian; not with near), programs, countryCode, region, locality, visitorPolicy, verifiedOnly, includeClosed, instructorId, audience. " +
                    "Search covers name, affiliation, summary, locality, region, and linked instructor names. LocalityOnly venues match near and bbox by their stored position but return no geo and whole-kilometer distances. Request history redacts near.lat and near.lng." + _Gate)
                    .Resp(400, "InvalidGeo").Resp(404, _NotAvailable),
                (ctx, query) => RunAsync(ctx, actor => _Directory.For(ctx).Directory.Venues.ListAsync(actor, query, ctx.Token)));
            post.Parameter.Add(HttpMethod.GET, _Venues + "/{idOrSlug}", GetVenueAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Read a venue", _Tag, "The venue by identifier, slug, or a previous slug (the payload carries the current slug). Adds description, socialLinks, scheduleText, photos, and a bounded instructors preview (at most 10, with the enumerate path; instructors the caller may not read are omitted). Address and pin follow addressVisibility: Exact (full address, 5 decimals), Approximate (locality, region, country, 2 decimals), LocalityOnly (no pin); staff see the exact values." + _Gate)
                    .PathParam("idOrSlug", "Venue identifier or slug").Returns("Venue").Resp(404, "NotFound or " + _NotAvailable));
            EnumerationRoutes.Add(post, _Venues + "/{id}/instructors", DirectoryCollections.VenueInstructors, _InstructorSummary,
                () => Doc("List a venue's instructors", _Tag, "Instructors linked to the venue with their role (Head, Regular, Guest). NameAscending default. Instructors whose audience the caller does not meet are omitted." + _Gate).Resp(404, "NotFound"),
                (ctx, query) => RunAsync(ctx, actor => _Directory.For(ctx).Directory.Venues.InstructorsAsync(actor, RouteHelper.Param(ctx, "id"), query, ctx.Token)), "id");
            RegisterReviews(post);
            RegisterMedia(post);
        }

        private void RegisterReviews(WatsonWebserver.Core.Routing.RoutingGroup post)
        {
            string reviews = _Venues + "/{id}/reviews";
            string review = reviews + "/{reviewId}";
            const string disabled = "ReviewsDisabled (directory.reviewsEnabled is off)";
            EnumerationRoutes.Add(post, reviews, DirectoryCollections.VenueReviews, _VenueReview,
                () => Doc("List a venue's reviews", _Tag, "Reviews of a venue the caller may read. Orderings CreatedDescending (default, newest), CreatedAscending, RatingDescending (highest first, newest first within a rating), RatingAscending (lowest first, newest first within a rating). " +
                    "Filters rating (1 to 5, one or a list), authorUserId, hasReply. Hidden and removed reviews are listed only for directory staff; reviews by members the caller blocked or who blocked the caller are left out. Signed in only." + _Gate)
                    .Resp(401, "Unauthorized").Resp(403, disabled).Resp(404, "NotFound"),
                (ctx, query) => RunAsync(ctx, actor => _Directory.For(ctx).Directory.Contributions.ListReviewsAsync(actor, RouteHelper.Param(ctx, "id"), query, ctx.Token)), "id");
            post.Parameter.Add(HttpMethod.POST, reviews, CreateReviewAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Review a venue", _Tag, "Body { rating (1 to 5), body (directory.reviewMinLength to directory.reviewMaxLength characters), labels?, tags? }. One live review per member per venue. Word filtered; the venue's linked instructors get VenueReview. " +
                    "Rate limited (directory.reviewsPerDay; directory.newAccountReviewsPerDay for new accounts). Needs a caller who may make directory writes (not muted, canPost).")
                    .PathParam("id", "Venue identifier or slug").Body("VenueReviewRequest").Returns(_VenueReview, 201).Resp(400, "ValidationFailed or ContentFiltered").Resp(403, disabled + ", Muted, PostingNotAllowed, or Forbidden")
                    .Resp(404, "NotFound").Resp(409, "AlreadyReviewed").Resp(429, "RateLimited"));
            post.Parameter.Add(HttpMethod.GET, review, GetReviewAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Read a venue review", _Tag, "One review (the target of VenueReview and VenueReviewReply notifications). Signed in only.")
                    .PathParam("id", "Venue identifier or slug").PathParam("reviewId", "Review identifier").Returns(_VenueReview).Resp(403, disabled).Resp(404, "NotFound"));
            post.Parameter.Add(HttpMethod.PUT, review, UpdateReviewAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Edit your venue review", _Tag, "Body { rating?, body?, labels?, tags? }; omitted fields stay. The author only. Word filtered; edits share rateLimits.editsPerHour.")
                    .PathParam("id", "Venue identifier or slug").PathParam("reviewId", "Review identifier").Body("VenueReviewRequest").Returns(_VenueReview).Resp(400, "ValidationFailed or ContentFiltered")
                    .Resp(403, "Forbidden, Muted, or " + disabled).Resp(404, "NotFound").Resp(429, "RateLimited"));
            post.Parameter.Add(HttpMethod.DELETE, review, DeleteReviewAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Delete your venue review", _Tag, "The author deletes their review (the rating counters drop it; the member may review again). Staff use the moderation actions instead.")
                    .PathParam("id", "Venue identifier or slug").PathParam("reviewId", "Review identifier").Resp(204, "Deleted").Resp(403, "Forbidden or " + disabled).Resp(404, "NotFound"));
            post.Parameter.Add(HttpMethod.PUT, review + "/reply", ReplyAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Reply to a venue review", _Tag, "Body { body } (1 to directory.reviewReplyMaxLength characters). A member linked to an instructor entry of the venue writes or edits the one public reply (not on their own review). The first reply notifies the reviewer (VenueReviewReply). Word filtered; shares rateLimits.editsPerHour.")
                    .PathParam("id", "Venue identifier or slug").PathParam("reviewId", "Review identifier").Body("VenueReviewReplyRequest").Returns(_VenueReview).Resp(400, "ValidationFailed or ContentFiltered")
                    .Resp(403, "Forbidden, InteractionBlocked, Muted, or " + disabled).Resp(404, "NotFound").Resp(429, "RateLimited"));
            post.Parameter.Add(HttpMethod.DELETE, review + "/reply", DeleteReplyAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Delete a review reply", _Tag, "The reply's author, or directory staff (audited as venue.review.reply.remove).")
                    .PathParam("id", "Venue identifier or slug").PathParam("reviewId", "Review identifier").Returns(_VenueReview).Resp(403, "Forbidden or " + disabled).Resp(404, "NotFound"));
        }

        private void RegisterMedia(WatsonWebserver.Core.Routing.RoutingGroup post)
        {
            string media = _Venues + "/{id}/media";
            EnumerationRoutes.Add(post, media, DirectoryCollections.VenueMedia, _VenueMediaItem,
                () => Doc("List a venue's member media", _Tag, "Photos and videos members added to the venue (staff-curated venue.photos are separate and shown first by clients). Orderings CreatedDescending (default), CreatedAscending. Filters kind (Image, Video), uploaderUserId. " +
                    "Removed and hidden items are listed only for directory staff; uploads by blocked members are left out. Signed in only." + _Gate)
                    .Resp(401, "Unauthorized").Resp(404, "NotFound"),
                (ctx, query) => RunAsync(ctx, actor => _Directory.For(ctx).Directory.Contributions.ListMediaAsync(actor, RouteHelper.Param(ctx, "id"), query, ctx.Token)), "id");
            post.Parameter.Add(HttpMethod.POST, media, AddMediaAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Add photos or videos to a venue", _Tag, "Body { mediaIds (1 to 10 of the caller's unattached uploads from POST /api/v1/media/uploads, purpose Content), labels?, tags? }. Each becomes a VenueMediaItem shown at once. " +
                    "Uploads keep the member upload limits; video needs canUploadVideo. Needs directory.memberMediaEnabled and a caller who may make directory writes.")
                    .PathParam("id", "Venue identifier or slug").Body("VenueMediaAddRequest").Returns("{ objects: [" + _VenueMediaItem + "] }", 201).Resp(400, "ValidationFailed or MediaLimitExceeded")
                    .Resp(403, "MemberMediaDisabled, MediaNotAllowed, VideoUploadNotAllowed, Muted, or Forbidden").Resp(404, "NotFound").Resp(409, "MediaNotAttachable"));
            post.Parameter.Add(HttpMethod.DELETE, media + "/{itemId}", RemoveMediaAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Remove a venue media item", _Tag, "Optional body { reason } (at most 500). The uploader deletes their item (the media is purged). Directory staff and the venue's linked instructors remove any item " +
                    "(served objects move to the retained bucket; audited as venue.media.remove; open reports resolve); a non-admin never removes a SystemAdmin's upload.")
                    .PathParam("id", "Venue identifier or slug").PathParam("itemId", "Media item identifier").Body("VenueMediaRemoveRequest", false).Resp(204, "Removed").Resp(403, "Forbidden").Resp(404, "NotFound"));
        }

        private void RegisterInstructors(WatsonWebserver.Core.Routing.RoutingGroup post)
        {
            EnumerationRoutes.Add(post, _Instructors, DirectoryCollections.Instructors, _InstructorSummary,
                () => Doc("List instructors", _Tag, "Instructors the caller may list. Filters venueId, rank (option keys of the configured Rank field), claimed, linkedUserId, audience. Orderings NameAscending (default), UpdatedDescending, and the Created orderings." + _Gate).Resp(404, _NotAvailable),
                (ctx, query) => RunAsync(ctx, actor => _Directory.For(ctx).Directory.Instructors.ListAsync(actor, query, ctx.Token)));
            post.Parameter.Add(HttpMethod.GET, _Instructors + "/{idOrSlug}", GetInstructorAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Read an instructor", _Tag, "By identifier, slug, or a previous slug. Adds bio, socialLinks, and a bounded venues preview (at most 10). rank, rankLabel, rankColor, and rankLevel exist only when directory.instructorRankFieldKey names a Rank profile field; staff see rankDiffersFromProfile for linked entries. Instructors never carry an address." + _Gate)
                    .PathParam("idOrSlug", "Instructor identifier or slug").Returns("Instructor").Resp(404, "NotFound or " + _NotAvailable));
            EnumerationRoutes.Add(post, _Instructors + "/{id}/venues", DirectoryCollections.InstructorVenues, _VenueSummary,
                () => Doc("List an instructor's venues", _Tag, "Venues the instructor is linked to. Filter includeClosed." + _Gate).Resp(404, "NotFound"),
                (ctx, query) => RunAsync(ctx, actor => _Directory.For(ctx).Directory.Instructors.VenuesAsync(actor, RouteHelper.Param(ctx, "id"), query, ctx.Token)), "id");
        }

        private void RegisterOther(WatsonWebserver.Core.Routing.RoutingGroup post)
        {
            post.Static.Add(HttpMethod.POST, _Base + "/map/enumerate", MapAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Enumerate map features", _Tag, "Body { bbox { south, west, north, east } (required), zoom (0 to 22, default 2), maxResults (transport page size, default 25), continuationToken, ordering, search, createdAfterUtc, createdBeforeUtc, labels, tags, filters (programs, countryCode, region, locality, visitorPolicy, verifiedOnly, instructorId, audience) }. " +
                    "While the viewport holds at most directory.map.maxFeatures visible venues every one is a Point { kind, venue { id, name, slug, geo, programs, isVerified, coverThumbUrl } }; otherwise the server clusters on a grid sized from the zoom and returns Cluster { kind, count, lat, lng, bbox } per non-empty cell (one-venue cells stay points). Standard keyset pages enumerate a generated feature set whose configured budget is independent of page size. totalRecords counts features; matchedRecords counts source records. IDs and cursors are scoped to viewport, zoom, budget and visibility. Created ordering uses point creation time and Unix epoch for generated cells. LocalityOnly venues are never on the map; Approximate venues sit at their rounded position. Rate limited (directory.mapPerMinute)." + _Gate)
                    .Body("MapEnumerationQuery").Enumeration("MapFeatureView", "NameAscending, CreatedAscending, CreatedDescending", "programs, countryCode, region, locality, visitorPolicy, verifiedOnly, instructorId, audience", false).Resp(400, "ValidationFailed or InvalidGeo").Resp(404, _NotAvailable).Resp(429, "RateLimited"));
            EnumerationRoutes.Add(post, _Base + "/programs", DirectoryCollections.Programs, "DirectoryProgram { id, key, label, sortOrder, isRetired, labels, tags, createdUtc, updatedUtc }",
                () => Doc("List directory programs", _Tag, "The program options venues offer, in admin order (SortOrderAscending, the default; also LabelAscending). Retired programs are hidden (directory staff may set filters.includeRetired)." + _Gate).Resp(404, _NotAvailable),
                (ctx, query) => RunAsync(ctx, actor => _Directory.For(ctx).Directory.Config.EnumerateProgramsAsync(actor, query, ctx.Token)));
            EnumerationRoutes.Add(post, _Base + "/geocode", AdministrativeCatalogs.GeocodeCandidates, "GeocodeCandidateView",
                () => Doc("Enumerate place candidates", _Tag, "filters.query is place text (2 to 200 characters). Enumerates the provider's finite top-five candidate set, not a global place catalog; totalRecords is the filtered candidate count. Provider relevance rank is the default ordering. Immutable candidates have Unix epoch timestamps and empty metadata. Query text, request and response bodies are not stored in request history. Signed in; provider rate limits apply per request."),
                async (ctx, query) =>
                {
                    ApplicationServices services = _Directory.For(ctx);
                    ContentActor actor = await services.Actors.ResolveAsync(ctx, ctx.Token).ConfigureAwait(false);
                    EnumerationRequest request = services.Enumeration.Prepare(query, AdministrativeCatalogs.GeocodeCandidates, false, actor.RequireUserId(), 5);
                    GeocodeSearchResponse result = await services.Directory.Geocoding.SearchAsync(actor, new GeocodeRequest { Query = request.Query.FilterString("query") }, ctx.Token).ConfigureAwait(false);
                    List<GeocodeCandidateView> candidates = result.Candidates.GroupBy(item => item.Id, StringComparer.Ordinal).Select(group => group.First()).ToList();
                    return CatalogEnumeration.Page(request, candidates, item => item.Id, search: item => item.Label, orderKey: item => request.Ordering.Name == "RankAscending" ? item.Rank.ToString(CultureInfo.InvariantCulture) : item.Id);
                });
            string tiles = _Base + "/tiles";
            string path = String.Empty;
            for (int depth = 1; depth <= 6; depth++)
            {
                path += "/{p" + depth + "}";
                int segments = depth;
                OpenApiRouteMetadata doc = Doc("Proxy a map tile (" + depth + " path segment" + (depth == 1 ? "" : "s") + ")", _Tag, "GET /api/v1/directory/tiles/{*path}: when directory.map.tileProxy is on, fetches the path (with the query string) from the one configured tile origin (directory.map.tileBaseUrl, else the origin of directory.map.styleUrl), adds directory.map.proxyKey server-side, rewrites style and TileJSON documents so their URLs come back through the proxy, and caches answers in memory with Cache-Control: public, max-age=86400. Rate limited (directory.tilesPerMinute per account; half per IP signed out)." + _Gate)
                    .File("application/octet-stream", "Tile, style, or TileJSON document").Resp(400, "ValidationFailed").Resp(404, "NotFound (proxy off) or " + _NotAvailable).Resp(429, "RateLimited").Resp(502, "TileUnavailable").Resp(503, "IntegrationNotConfigured");
                for (int i = 1; i <= depth; i++) doc = doc.PathParam("p" + i, "Tile path segment " + i);
                post.Parameter.Add(HttpMethod.GET, tiles + path, ctx => TileAsync(ctx, segments), RouteHelper.HandleExceptionAsync, openApiMetadata: doc);
            }
        }

        private async Task<object> RunAsync<T>(HttpContextBase ctx, Func<ContentActor, Task<T>> body) where T : class
        {
            ContentActor actor = await _Directory.For(ctx).Actors.ResolveAsync(ctx, ctx.Token).ConfigureAwait(false);
            return await body(actor).ConfigureAwait(false);
        }

        private async Task GetVenueAsync(HttpContextBase ctx)
        {
            ContentActor actor = await _Directory.For(ctx).Actors.ResolveAsync(ctx, ctx.Token).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 200, await _Directory.For(ctx).Directory.Venues.GetAsync(actor, RouteHelper.Param(ctx, "idOrSlug"), ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task CreateReviewAsync(HttpContextBase ctx)
        {
            VenueReviewRequest request = RouteHelper.ReadJson<VenueReviewRequest>(ctx);
            ContentActor actor = await _Directory.For(ctx).Actors.ResolveAsync(ctx, ctx.Token).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 201, await _Directory.For(ctx).Directory.Contributions.CreateReviewAsync(actor, RouteHelper.Param(ctx, "id"), request, ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task GetReviewAsync(HttpContextBase ctx)
        {
            ContentActor actor = await _Directory.For(ctx).Actors.ResolveAsync(ctx, ctx.Token).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 200, await _Directory.For(ctx).Directory.Contributions.GetReviewAsync(actor, RouteHelper.Param(ctx, "id"), RouteHelper.Param(ctx, "reviewId"), ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task UpdateReviewAsync(HttpContextBase ctx)
        {
            VenueReviewRequest request = RouteHelper.ReadJson<VenueReviewRequest>(ctx, out HashSet<string> present);
            ContentActor actor = await _Directory.For(ctx).Actors.ResolveAsync(ctx, ctx.Token).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 200, await _Directory.For(ctx).Directory.Contributions.UpdateReviewAsync(actor, RouteHelper.Param(ctx, "id"), RouteHelper.Param(ctx, "reviewId"), request, present, ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task DeleteReviewAsync(HttpContextBase ctx)
        {
            ContentActor actor = await _Directory.For(ctx).Actors.ResolveAsync(ctx, ctx.Token).ConfigureAwait(false);
            await _Directory.For(ctx).Directory.Contributions.DeleteReviewAsync(actor, RouteHelper.Param(ctx, "id"), RouteHelper.Param(ctx, "reviewId"), ctx.Token).ConfigureAwait(false);
            await RouteHelper.SendEmptyAsync(ctx, 204).ConfigureAwait(false);
        }

        private async Task ReplyAsync(HttpContextBase ctx)
        {
            VenueReviewReplyRequest request = RouteHelper.ReadJson<VenueReviewReplyRequest>(ctx);
            ContentActor actor = await _Directory.For(ctx).Actors.ResolveAsync(ctx, ctx.Token).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 200, await _Directory.For(ctx).Directory.Contributions.ReplyAsync(actor, RouteHelper.Param(ctx, "id"), RouteHelper.Param(ctx, "reviewId"), request, ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task DeleteReplyAsync(HttpContextBase ctx)
        {
            ContentActor actor = await _Directory.For(ctx).Actors.ResolveAsync(ctx, ctx.Token).ConfigureAwait(false);
            RequestInfo info = RequestInfo.From(ctx, _Directory.For(ctx).Settings.Rest);
            await RouteHelper.SendJsonAsync(ctx, 200, await _Directory.For(ctx).Directory.Contributions.DeleteReplyAsync(actor, info, RouteHelper.Param(ctx, "id"), RouteHelper.Param(ctx, "reviewId"), ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task AddMediaAsync(HttpContextBase ctx)
        {
            VenueMediaAddRequest request = RouteHelper.ReadJson<VenueMediaAddRequest>(ctx);
            ContentActor actor = await _Directory.For(ctx).Actors.ResolveAsync(ctx, ctx.Token).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 201, await _Directory.For(ctx).Directory.Contributions.AddMediaAsync(actor, RouteHelper.Param(ctx, "id"), request, ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task RemoveMediaAsync(HttpContextBase ctx)
        {
            VenueMediaRemoveRequest request = RouteHelper.ReadJson<VenueMediaRemoveRequest>(ctx);
            ContentActor actor = await _Directory.For(ctx).Actors.ResolveAsync(ctx, ctx.Token).ConfigureAwait(false);
            RequestInfo info = RequestInfo.From(ctx, _Directory.For(ctx).Settings.Rest);
            await _Directory.For(ctx).Directory.Contributions.RemoveMediaAsync(actor, info, RouteHelper.Param(ctx, "id"), RouteHelper.Param(ctx, "itemId"), request.Reason, ctx.Token).ConfigureAwait(false);
            await RouteHelper.SendEmptyAsync(ctx, 204).ConfigureAwait(false);
        }

        private async Task GetInstructorAsync(HttpContextBase ctx)
        {
            ContentActor actor = await _Directory.For(ctx).Actors.ResolveAsync(ctx, ctx.Token).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 200, await _Directory.For(ctx).Directory.Instructors.GetAsync(actor, RouteHelper.Param(ctx, "idOrSlug"), ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task MapAsync(HttpContextBase ctx)
        {
            MapEnumerationQuery query = RouteHelper.ReadEnumerationBody<MapEnumerationQuery>(ctx);
            ContentActor actor = await _Directory.For(ctx).Actors.ResolveAsync(ctx, ctx.Token).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 200, await _Directory.For(ctx).Directory.Map.EnumerateAsync(actor, query, false, ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task TileAsync(HttpContextBase ctx, int segments)
        {
            ContentActor actor = await _Directory.For(ctx).Actors.ResolveAsync(ctx, ctx.Token).ConfigureAwait(false);
            string path = String.Join("/", Enumerable.Range(1, segments).Select(i => RouteHelper.Param(ctx, "p" + i)));
            string raw = ctx.Request.Url.RawWithQuery ?? String.Empty;
            int q = raw.IndexOf('?');
            string? query = q < 0 ? null : raw.Substring(q + 1);
            string host = ctx.Request.Headers["Host"] ?? ("127.0.0.1:" + ctx.Request.Destination.Port);
            string scheme = ctx.Request.Headers["X-Forwarded-Proto"] ?? (ctx.Request.Url.Full?.StartsWith("https", StringComparison.OrdinalIgnoreCase) == true ? "https" : "http");
            TileProxyResponse answer = await _Directory.For(ctx).Directory.Tiles.FetchAsync(actor, path, query, scheme + "://" + host + _Base + "/tiles", ctx.Token).ConfigureAwait(false);
            ctx.Response.StatusCode = answer.StatusCode;
            ctx.Response.ContentType = answer.ContentType;
            ctx.Response.Headers["Cache-Control"] = MapTileProxyService.CacheControl;
            await ctx.Response.Send(answer.Body, ctx.Token).ConfigureAwait(false);
        }

        #endregion
    }
}
