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
    using static TrellisKit.Server.Routes.OpenApiExtensions;

    /// <summary>
    /// Public directory routes (docs/PHASE4_CONTRACT.md section 3.11): Public, Active venues and their map for anonymous
    /// callers when directory.publicListingsEnabled is on, in every access model, cached for 300 seconds and rate
    /// limited per IP.
    /// </summary>
    public class PublicDirectoryRoutes
    {
        #region Public-Members

        /// <summary>
        /// Cache-Control of every successful public directory response.
        /// </summary>
        public const string CacheControl = "public, max-age=300";

        #endregion

        #region Private-Members

        private const string _Tag = "Public directory";
        private const string _Base = Constants.ApiPrefix + "/public/venues";
        private const string _Note = " Only Public, Active venues; email, instructors' linked users, and viewer are omitted, phone too unless directory.publicShowsContact. 404 NotAvailable when directory.publicListingsEnabled is off or features.directory is off. Cache-Control: public, max-age=300. Rate limited per IP (rateLimits.publicFeedPerMinute). Anonymous.";
        private readonly PropertyDirectory _Directory;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="directory">Property directory.</param>
        /// <exception cref="ArgumentNullException">The directory is null.</exception>
        public PublicDirectoryRoutes(PropertyDirectory directory)
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
            WatsonWebserver.Core.Routing.RoutingGroup pre = server.Routes.PreAuthentication;
            string orderings = DirectoryCollections.PublicVenues.OrderingNames();
            pre.Static.Add(HttpMethod.GET, _Base, ListAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("List public venues", _Tag, "Filters near, bbox, programs, countryCode, region, locality, visitorPolicy, verifiedOnly." + _Note)
                    .Enumeration("VenueSummary", orderings, "near, bbox, programs, countryCode, region, locality, visitorPolicy, verifiedOnly").Resp(400, "ValidationFailed or InvalidGeo").Resp(404, "NotAvailable").Resp(429, "RateLimited"));
            pre.Static.Add(HttpMethod.POST, _Base + "/enumerate", EnumerateAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Enumerate public venues", _Tag, "EnumerationQuery body; same result as the GET form." + _Note)
                    .Body("EnumerationQuery", false).Enumeration("VenueSummary", orderings, "near, bbox, programs, countryCode, region, locality, visitorPolicy, verifiedOnly", false)
                    .Resp(400, "ValidationFailed or InvalidGeo").Resp(404, "NotAvailable").Resp(429, "RateLimited"));
            pre.Parameter.Add(HttpMethod.GET, _Base + "/{id}", GetAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Read a public venue", _Tag, "One Public, Active venue by identifier or slug." + _Note).PathParam("id", "Venue identifier or slug").Returns("Venue").Resp(404, "NotFound or NotAvailable").Resp(429, "RateLimited"));
            pre.Static.Add(HttpMethod.POST, Constants.ApiPrefix + "/public/directory/map/enumerate", MapAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Enumerate public map features", _Tag, "The map enumeration of Public, Active venues (body as POST /api/v1/directory/map/enumerate)." + _Note)
                    .Body("MapEnumerationQuery").Enumeration("MapFeatureView", "NameAscending, CreatedAscending, CreatedDescending", "programs, countryCode, region, locality, visitorPolicy, verifiedOnly, instructorId, audience", false).Resp(400, "ValidationFailed or InvalidGeo").Resp(404, "NotAvailable").Resp(429, "RateLimited"));
        }

        #endregion

        #region Private-Methods

        private Task ListAsync(HttpContextBase ctx)
        {
            return SendListAsync(ctx, RouteHelper.ReadEnumerationQuery<EnumerationQuery>(ctx));
        }

        private Task EnumerateAsync(HttpContextBase ctx)
        {
            return SendListAsync(ctx, RouteHelper.ReadEnumerationBody<EnumerationQuery>(ctx));
        }

        private async Task SendListAsync(HttpContextBase ctx, EnumerationQuery query)
        {
            RequestInfo info = RequestInfo.From(ctx, _Directory.For(ctx).Settings.Rest);
            object page = await _Directory.For(ctx).Directory.Venues.PublicListAsync(query, info.SourceIp, ctx.Token).ConfigureAwait(false);
            ctx.Response.Headers["Cache-Control"] = CacheControl;
            await RouteHelper.SendJsonAsync(ctx, 200, page).ConfigureAwait(false);
        }

        private async Task GetAsync(HttpContextBase ctx)
        {
            RequestInfo info = RequestInfo.From(ctx, _Directory.For(ctx).Settings.Rest);
            object venue = await _Directory.For(ctx).Directory.Venues.PublicGetAsync(RouteHelper.Param(ctx, "id"), info.SourceIp, ctx.Token).ConfigureAwait(false);
            ctx.Response.Headers["Cache-Control"] = CacheControl;
            await RouteHelper.SendJsonAsync(ctx, 200, venue).ConfigureAwait(false);
        }

        private async Task MapAsync(HttpContextBase ctx)
        {
            RequestInfo info = RequestInfo.From(ctx, _Directory.For(ctx).Settings.Rest);
            MapEnumerationQuery query = RouteHelper.ReadEnumerationBody<MapEnumerationQuery>(ctx);
            ApplicationServices services = _Directory.For(ctx);
            await services.Directory.Venues.PublicGateAsync(info.SourceIp, ctx.Token).ConfigureAwait(false);
            object page = await services.Directory.Map.EnumerateAsync(services.Actors.Anonymous(info.SourceIp), query, true, ctx.Token).ConfigureAwait(false);
            ctx.Response.Headers["Cache-Control"] = CacheControl;
            await RouteHelper.SendJsonAsync(ctx, 200, page).ConfigureAwait(false);
        }

        #endregion
    }
}
