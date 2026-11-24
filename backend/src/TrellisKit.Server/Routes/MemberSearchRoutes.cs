namespace TrellisKit.Server.Routes
{
    using System;
    using System.Globalization;
    using System.Threading.Tasks;
    using TrellisKit.Core;
    using TrellisKit.Core.Exceptions;
    using TrellisKit.Core.Requests;
    using TrellisKit.Server.Services;
    using WatsonWebserver;
    using WatsonWebserver.Core;
    using static TrellisKit.Server.Routes.OpenApiExtensions;

    /// <summary>
    /// Member search for mention autocomplete (Phase 2 contract section 3.4). Members only.
    /// </summary>
    public class MemberSearchRoutes
    {
        #region Private-Members

        private const string _Tag = "Members";
        private const string _Description = "search (alias q in the GET form) matches a prefix of the handle or of any display name word (1 to 30 characters after an optional @). " +
            "Ranked: exact handle, then the context post's author and commenters (filters contextType=FeedPost and contextId=fpo_...), then handle prefix, then display name. " +
            "Standard keyset pages: maxResults defaults to 8 and is capped at 20. Relevance (default), CreatedAscending and CreatedDescending are honored, with skip and scoped continuation tokens. Staff totals are exact; member totals follow enumeration.countMemberTotals. Created windows and labels/tags filter the source members. Members only.";
        private readonly PropertyDirectory _Directory;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the routes.
        /// </summary>
        /// <param name="directory">Property directory (resolves each request's property services).</param>
        /// <exception cref="ArgumentNullException">The directory is null.</exception>
        public MemberSearchRoutes(PropertyDirectory directory)
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
            WatsonWebserver.Core.Routing.RoutingGroup post = server.Routes.PostAuthentication;
            post.Static.Add(HttpMethod.GET, Constants.ApiPrefix + "/members", SearchAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Search members", _Tag, _Description)
                    .QueryParam("q", "Alias of search").QueryParam("limit", "Alias of maxResults")
                    .Enumeration("UserSummary", "Relevance, CreatedAscending, CreatedDescending", "contextType, contextId").Resp(400, "ValidationFailed").Resp(429, "RateLimited (rateLimits.memberSearchPerMinute)"));
            post.Static.Add(HttpMethod.GET, Constants.ApiPrefix + "/members/search", SearchAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Search members (alias of GET /api/v1/members)", _Tag, _Description)
                    .QueryParam("q", "Alias of search").QueryParam("limit", "Alias of maxResults")
                    .Enumeration("UserSummary", "Relevance, CreatedAscending, CreatedDescending", "contextType, contextId").Resp(400, "ValidationFailed").Resp(429, "RateLimited"));
            post.Static.Add(HttpMethod.POST, Constants.ApiPrefix + "/members/enumerate", EnumerateAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Search members (enumeration body)", _Tag, _Description)
                    .Body("EnumerationQuery: search, filters { contextType?, contextId? }").Enumeration("UserSummary", "Relevance, CreatedAscending, CreatedDescending", "contextType, contextId", false)
                    .Resp(400, "ValidationFailed").Resp(429, "RateLimited"));
        }

        #endregion

        #region Private-Methods

        private Task SearchAsync(HttpContextBase ctx)
        {
            MemberSearchQuery query = RouteHelper.ReadEnumerationQuery<MemberSearchQuery>(ctx, "limit");
            string? limit = RouteHelper.Query(ctx, "limit");
            if (limit != null && query.MaxResults == null)
            {
                if (!Int32.TryParse(limit, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value)) throw ApiErrorException.Field("limit", "Must be a whole number.");
                query.MaxResults = value;
            }
            return SendAsync(ctx, query);
        }

        private Task EnumerateAsync(HttpContextBase ctx)
        {
            return SendAsync(ctx, RouteHelper.ReadEnumerationBody<MemberSearchQuery>(ctx));
        }

        private async Task SendAsync(HttpContextBase ctx, MemberSearchQuery query)
        {
            ContentActor actor = await _Directory.For(ctx).Actors.ResolveAsync(ctx, ctx.Token).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 200, await _Directory.For(ctx).MemberSearch.SearchAsync(actor, query, ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        #endregion
    }
}
