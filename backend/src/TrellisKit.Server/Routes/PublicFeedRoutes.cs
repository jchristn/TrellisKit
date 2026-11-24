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
    /// Anonymous public sample posts (Phase 2 contract section 5.10): registered before authentication, rate limited per
    /// IP (rateLimits.publicFeedPerMinute), cacheable for 60 seconds, and 404 when the property's features.publicSamples
    /// is false.
    /// </summary>
    public class PublicFeedRoutes
    {
        #region Public-Members

        /// <summary>
        /// Cache-Control of every successful public feed response.
        /// </summary>
        public const string CacheControl = "public, max-age=60";

        #endregion

        #region Private-Members

        private const string _Tag = "Public feed";
        private const string _Base = Constants.ApiPrefix + "/public/feed";
        private const string _Orderings = "PublishedDescending, Feed, CreatedDescending, CreatedAscending";
        private readonly PropertyDirectory _Directory;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the routes.
        /// </summary>
        /// <param name="directory">Property directory (resolves each request's property services).</param>
        /// <exception cref="ArgumentNullException">The directory is null.</exception>
        public PublicFeedRoutes(PropertyDirectory directory)
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
            string description = "Published, unmoderated Public posts, newest publishedUtc first (maxResults default 10, max 20). Items are PublicFeedPost (no myReactions, viewer, moderation, or mentions; media from the public paths). " +
                "Cache-Control: public, max-age=60. 404 when the property disables public samples. Anonymous.";
            pre.Static.Add(HttpMethod.GET, _Base, ListAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("List public sample posts", _Tag, description)
                    .Enumeration("PublicFeedPost", _Orderings, "publishedAfterUtc, publishedBeforeUtc, audience").Resp(400, "ValidationFailed or InvalidContinuationToken").Resp(404, "NotFound").Resp(429, "RateLimited"));
            pre.Static.Add(HttpMethod.POST, _Base + "/enumerate", EnumerateAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Enumerate public sample posts", _Tag, "EnumerationQuery body; same result as the GET form. " + description)
                    .Body("EnumerationQuery", false).Enumeration("PublicFeedPost", _Orderings, "publishedAfterUtc, publishedBeforeUtc, audience", false)
                    .Resp(400, "ValidationFailed or InvalidContinuationToken").Resp(404, "NotFound").Resp(429, "RateLimited"));
            pre.Parameter.Add(HttpMethod.GET, _Base + "/{id}", GetAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Read a public sample post", _Tag, "One PublicFeedPost. Cache-Control: public, max-age=60. Anonymous.")
                    .PathParam("id", "Post identifier").Returns("PublicFeedPost").Resp(404, "NotFound").Resp(429, "RateLimited"));
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
            object page = await _Directory.For(ctx).Feed.ListPublicAsync(query, info.SourceIp, ctx.Token).ConfigureAwait(false);
            ctx.Response.Headers["Cache-Control"] = CacheControl;
            await RouteHelper.SendJsonAsync(ctx, 200, page).ConfigureAwait(false);
        }

        private async Task GetAsync(HttpContextBase ctx)
        {
            RequestInfo info = RequestInfo.From(ctx, _Directory.For(ctx).Settings.Rest);
            object post = await _Directory.For(ctx).Feed.GetPublicAsync(RouteHelper.Param(ctx, "id"), info.SourceIp, ctx.Token).ConfigureAwait(false);
            ctx.Response.Headers["Cache-Control"] = CacheControl;
            await RouteHelper.SendJsonAsync(ctx, 200, post).ConfigureAwait(false);
        }

        #endregion
    }
}
