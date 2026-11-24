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
    /// Anonymous public thread samples (Phase 3 contract section 14).
    /// </summary>
    public class PublicThreadRoutes
    {
        #region Public-Members

        /// <summary>
        /// Cache-Control header value of the public thread responses.
        /// </summary>
        public const string CacheControl = "public, max-age=60";

        #endregion

        #region Private-Members

        private const string _Tag = "Public";
        private const string _Item = "PublicThread";
        private const string _Orderings = "CreatedDescending";
        private const string _Base = Constants.ApiPrefix + "/public/threads";
        private readonly PropertyDirectory _Directory;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the routes.
        /// </summary>
        /// <param name="directory">Property directory (resolves each request's property services).</param>
        /// <exception cref="ArgumentNullException">The directory is null.</exception>
        public PublicThreadRoutes(PropertyDirectory directory)
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
            string description = "Published, unmoderated Public threads in Public forums, newest first (maxResults default 10, max 20). Items are PublicThread (title, excerpt, forum, author, counts, poll summary without voters; no viewer). " +
                "Cache-Control: public, max-age=60. 404 when forums are off or the property disables public samples. Anonymous.";
            pre.Static.Add(HttpMethod.GET, _Base, ListAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("List public sample threads", _Tag, description)
                    .Enumeration(_Item, _Orderings, String.Empty).Resp(400, "ValidationFailed or InvalidContinuationToken").Resp(404, "NotFound").Resp(429, "RateLimited"));
            pre.Static.Add(HttpMethod.POST, _Base + "/enumerate", EnumerateAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Enumerate public sample threads", _Tag, "EnumerationQuery body; same result as the GET form. " + description)
                    .Body("EnumerationQuery", false).Enumeration(_Item, _Orderings, String.Empty, false)
                    .Resp(400, "ValidationFailed or InvalidContinuationToken").Resp(404, "NotFound").Resp(429, "RateLimited"));
            pre.Parameter.Add(HttpMethod.GET, _Base + "/{id}", GetAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Read a public sample thread", _Tag, "One PublicThread. Cache-Control: public, max-age=60. Anonymous.")
                    .PathParam("id", "Thread identifier").Returns(_Item).Resp(404, "NotFound").Resp(429, "RateLimited"));
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
            object page = await _Directory.For(ctx).Forums.Threads.ListPublicAsync(query, info.SourceIp, ctx.Token).ConfigureAwait(false);
            ctx.Response.Headers["Cache-Control"] = CacheControl;
            await RouteHelper.SendJsonAsync(ctx, 200, page).ConfigureAwait(false);
        }

        private async Task GetAsync(HttpContextBase ctx)
        {
            RequestInfo info = RequestInfo.From(ctx, _Directory.For(ctx).Settings.Rest);
            object thread = await _Directory.For(ctx).Forums.Threads.GetPublicAsync(RouteHelper.Param(ctx, "id"), info.SourceIp, ctx.Token).ConfigureAwait(false);
            ctx.Response.Headers["Cache-Control"] = CacheControl;
            await RouteHelper.SendJsonAsync(ctx, 200, thread).ConfigureAwait(false);
        }

        #endregion
    }
}
