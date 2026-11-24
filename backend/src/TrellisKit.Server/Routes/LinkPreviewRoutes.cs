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
    /// Link previews for composers (Phase 2 contract section 5.9). Members only.
    /// </summary>
    public class LinkPreviewRoutes
    {
        #region Private-Members

        private readonly PropertyDirectory _Directory;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the routes.
        /// </summary>
        /// <param name="directory">Property directory (resolves each request's property services).</param>
        /// <exception cref="ArgumentNullException">The directory is null.</exception>
        public LinkPreviewRoutes(PropertyDirectory directory)
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
            server.Routes.PostAuthentication.Static.Add(HttpMethod.POST, Constants.ApiPrefix + "/link-previews", PreviewAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Preview a link", "Link previews", "Fetched server side with SSRF protection and cached (linkPreviews.cacheHours; failures linkPreviews.failureCacheMinutes). Members only.")
                    .Body("{ url }").Returns("LinkPreview").Resp(400, "ValidationFailed or LinkPreviewUnavailable")
                    .Resp(429, "RateLimited (linkPreviews.perHour per member, linkPreviews.perHostPerMinute per host)"));
        }

        #endregion

        #region Private-Methods

        private async Task PreviewAsync(HttpContextBase ctx)
        {
            LinkPreviewRequest request = RouteHelper.ReadJson<LinkPreviewRequest>(ctx);
            ContentActor actor = await _Directory.For(ctx).Actors.ResolveAsync(ctx, ctx.Token).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 200, await _Directory.For(ctx).LinkPreviews.PreviewAsync(actor, request.Url, ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        #endregion
    }
}
