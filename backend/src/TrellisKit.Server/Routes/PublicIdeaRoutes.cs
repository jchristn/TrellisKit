namespace TrellisKit.Server.Routes
{
    using System;
    using System.Threading.Tasks;
    using TrellisKit.Core;
    using TrellisKit.Core.Requests;
    using TrellisKit.Server.Services;
    using WatsonWebserver;
    using WatsonWebserver.Core;
    using static TrellisKit.Server.Routes.OpenApiExtensions;

    /// <summary>
    /// The anonymous public ideas board (docs/PHASE5_CONTRACT.md section 7.3): Public ideas when ideas.publicBoardEnabled.
    /// </summary>
    public class PublicIdeaRoutes
    {
        #region Public-Members

        /// <summary>
        /// Cache-Control header value of the public ideas responses.
        /// </summary>
        public const string CacheControl = "public, max-age=60";

        #endregion

        #region Private-Members

        private const string _Tag = "Public";
        private const string _Item = "Idea (Public ideas; viewer allows nothing; no votes by viewer, no comments)";
        private const string _Base = Constants.ApiPrefix + "/public/ideas";
        private readonly PropertyDirectory _Directory;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the routes.
        /// </summary>
        /// <param name="directory">Property directory (resolves each request's property services).</param>
        /// <exception cref="ArgumentNullException">The directory is null.</exception>
        public PublicIdeaRoutes(PropertyDirectory directory)
        {
            _Directory = directory ?? throw new ArgumentNullException(nameof(directory));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Register routes.
        /// </summary>
        /// <param name="server">Watson server.</param>
        /// <exception cref="ArgumentNullException">The server is null.</exception>
        public void Register(Webserver server)
        {
            ArgumentNullException.ThrowIfNull(server);
            WatsonWebserver.Core.Routing.RoutingGroup pre = server.Routes.PreAuthentication;
            string description = "Public, unmoderated, unmerged ideas (orderings Top, the default, Trending, CreatedDescending, CreatedAscending, UpdatedDescending, Relevance; filters status, open; maxResults at most 50). " +
                "Cache-Control: public, max-age=60. Rate limited per IP (rateLimits.publicFeedPerMinute). 404 NotAvailable unless ideas.publicBoardEnabled and features.ideas. Anonymous.";
            pre.Static.Add(HttpMethod.GET, _Base, ListAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("List public ideas", _Tag, description)
                    .Enumeration(_Item, "Top, Trending, CreatedDescending, CreatedAscending, UpdatedDescending, Relevance", "status, open").Resp(400, "ValidationFailed or InvalidContinuationToken").Resp(404, "NotAvailable").Resp(429, "RateLimited"));
            pre.Static.Add(HttpMethod.POST, _Base + "/enumerate", EnumerateAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Enumerate public ideas", _Tag, "EnumerationQuery body; same result as the GET form. " + description)
                    .Body("EnumerationQuery", false).Enumeration(_Item, "Top, Trending, CreatedDescending, CreatedAscending, UpdatedDescending, Relevance", "status, open", false)
                    .Resp(400, "ValidationFailed or InvalidContinuationToken").Resp(404, "NotAvailable").Resp(429, "RateLimited"));
            pre.Parameter.Add(HttpMethod.GET, _Base + "/{id}", GetAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Read a public idea", _Tag, "One Public idea. Cache-Control: public, max-age=60. Anonymous.")
                    .PathParam("id", "Idea identifier").Returns(_Item).Resp(404, "NotFound or NotAvailable").Resp(429, "RateLimited"));
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
            object page = await _Directory.For(ctx).Ideas.Ideas.PublicListAsync(query, info.SourceIp, ctx.Token).ConfigureAwait(false);
            ctx.Response.Headers["Cache-Control"] = CacheControl;
            await RouteHelper.SendJsonAsync(ctx, 200, page).ConfigureAwait(false);
        }

        private async Task GetAsync(HttpContextBase ctx)
        {
            RequestInfo info = RequestInfo.From(ctx, _Directory.For(ctx).Settings.Rest);
            object idea = await _Directory.For(ctx).Ideas.Ideas.PublicGetAsync(RouteHelper.Param(ctx, "id"), info.SourceIp, ctx.Token).ConfigureAwait(false);
            ctx.Response.Headers["Cache-Control"] = CacheControl;
            await RouteHelper.SendJsonAsync(ctx, 200, idea).ConfigureAwait(false);
        }

        #endregion
    }
}
