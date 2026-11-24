namespace TrellisKit.Server.Routes
{
    using System;
    using System.Threading.Tasks;
    using TrellisKit.Core;
    using TrellisKit.Core.Enumeration;
    using TrellisKit.Server.Services;
    using WatsonWebserver;
    using WatsonWebserver.Core;
    using static TrellisKit.Server.Routes.OpenApiExtensions;

    /// <summary>
    /// Member announcement routes (docs/PHASE5_CONTRACT.md section 8.2): the broadcasts a signed-in account received.
    /// SignedIn route group (every access model).
    /// </summary>
    public class AnnouncementRoutes
    {
        #region Private-Members

        private const string _Tag = "Announcements";
        private const string _Base = Constants.ApiPrefix + "/announcements";
        private const string _Announcement = "Announcement { id, title, body, sentUtc, labels, tags }";
        private readonly PropertyDirectory _Directory;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the routes.
        /// </summary>
        /// <param name="directory">Property directory (resolves each request's property services).</param>
        /// <exception cref="ArgumentNullException">The directory is null.</exception>
        public AnnouncementRoutes(PropertyDirectory directory)
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
            EnumerationRoutes.Add(post, _Base, BroadcastCollections.Announcements, _Announcement,
                () => Doc("List my announcements", _Tag, "The broadcasts the caller received (a delivery exists), newest first; search over the title; label and tag filters. Any signed-in account."),
                async (ctx, query) =>
                {
                    ContentActor actor = await _Directory.For(ctx).Actors.ResolveAsync(ctx, ctx.Token).ConfigureAwait(false);
                    return await _Directory.For(ctx).Broadcasts.Announcements.EnumerateAsync(actor, query, ctx.Token).ConfigureAwait(false);
                });
            post.Parameter.Add(HttpMethod.GET, _Base + "/{id}", GetAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Read an announcement", _Tag, "A broadcast the caller received; 404 for anyone else. Any signed-in account.")
                    .PathParam("id", "Broadcast identifier").Returns(_Announcement).Resp(404, "NotFound"));
        }

        #endregion

        #region Private-Methods

        private async Task GetAsync(HttpContextBase ctx)
        {
            ContentActor actor = await _Directory.For(ctx).Actors.ResolveAsync(ctx, ctx.Token).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 200, await _Directory.For(ctx).Broadcasts.Announcements.GetAsync(actor, RouteHelper.Param(ctx, "id"), ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        #endregion
    }
}
