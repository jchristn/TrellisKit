namespace TrellisKit.Server.Routes
{
    using System;
    using System.Threading.Tasks;
    using TrellisKit.Core;
    using TrellisKit.Core.Enumeration;
    using TrellisKit.Core.Exceptions;
    using TrellisKit.Core.Requests;
    using TrellisKit.Server.Services;
    using TrellisKit.Server.Services.Usage;
    using WatsonWebserver;
    using WatsonWebserver.Core;
    using WatsonWebserver.Core.Routing;
    using static TrellisKit.Server.Routes.OpenApiExtensions;

    /// <summary>
    /// Platform usage routes (docs/USAGE_METERING_CONTRACT.md section 7.2): any property's usage summary, records, and
    /// export, and the bounded cross-property comparison. Platform administrator token or the break-glass token;
    /// platform host only; MultiProperty only.
    /// </summary>
    public class PlatformUsageRoutes
    {
        #region Private-Members

        private const string _Base = Constants.ApiPrefix + "/platform";
        private const string _Tag = "Platform";
        private const string _Auth = " Platform administrator token (Authorization: Bearer tkp_...) or the break-glass admin token; platform host only; MultiProperty only.";
        private const string _RowItem = "PropertyUsageRow { propertyId, slug, name, status, period, fromUtc, toUtc, usersTotal, usersNew, activeUsers, posts, comments, interactions, images, imageBytes, videos, videoBytes, storageBytes, apiRequests, pushSent, emailSent, aiTokens, aiCostMicros, dataThroughUtc, labels, tags }";
        private readonly PlatformService _Platform;
        private readonly PlatformUsageService _Usage;
        private readonly SettingsService _Settings;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the routes.
        /// </summary>
        /// <param name="platform">Platform service (authentication).</param>
        /// <param name="usage">Platform usage service.</param>
        /// <param name="settings">Settings service (client address resolution).</param>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        public PlatformUsageRoutes(PlatformService platform, PlatformUsageService usage, SettingsService settings)
        {
            _Platform = platform ?? throw new ArgumentNullException(nameof(platform));
            _Usage = usage ?? throw new ArgumentNullException(nameof(usage));
            _Settings = settings ?? throw new ArgumentNullException(nameof(settings));
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
            RoutingGroup g = server.Routes.PreAuthentication;
            g.Parameter.Add(HttpMethod.GET, _Base + "/properties/{propertyId}/usage/summary", SummaryAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: AdminUsageRoutes.SummaryDoc(Doc("A property's usage summary", _Tag, "Same body as GET /api/v1/admin/usage/summary, for any property, in that property's time zone." + _Auth)
                    .PathParam("propertyId", "Property id")).Resp(401, "Unauthorized").Resp(404, "NotFound"));
            g.Parameter.Add(HttpMethod.POST, _Base + "/properties/{propertyId}/usage/summary/enumerate", SummaryAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: AdminUsageRoutes.SummaryDoc(Doc("A property's usage summary", _Tag, "Same body as GET /api/v1/admin/usage/summary, for any property, in that property's time zone." + _Auth)
                    .PathParam("propertyId", "Property id")).Resp(401, "Unauthorized").Resp(404, "NotFound"));
            EnumerationRoutes.Add(g, _Base + "/properties/{propertyId}/usage", UsageCollections.Records, AdminUsageRoutes.RecordItem,
                () => Doc("List a property's daily usage records", _Tag, "Same as POST /api/v1/admin/usage/enumerate, for any property." + _Auth),
                async (ctx, query) =>
                {
                    await CallerAsync(ctx).ConfigureAwait(false);
                    PlatformUsageTarget target = await _Usage.ForAsync(RouteHelper.Param(ctx, "propertyId"), ctx.Token).ConfigureAwait(false);
                    return await target.Usage.EnumerateAsync(query, ctx.Token).ConfigureAwait(false);
                },
                "propertyId");
            g.Parameter.Add(HttpMethod.GET, _Base + "/properties/{propertyId}/usage/export", ExportAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: AdminUsageRoutes.ExportDoc(Doc("Export a property's usage", _Tag, "Same file as GET /api/v1/admin/usage/export, for any property: the stable usage export a billing engine reads (bill a range only when complete is true)." + _Auth)
                    .PathParam("propertyId", "Property id")).Resp(401, "Unauthorized").Resp(404, "NotFound"));
            EnumerationRoutes.Add(g, _Base + "/usage/properties", UsageCollections.PropertyUsage, _RowItem,
                () => Doc("Compare usage across properties", _Tag, "One row per registered property (at most deployment.maxProperties) with its period totals in its own time zone. Filters period (default LastMonth), from, to (Range), status. Search over id, slug, name. The computed orderings (UsersDescending, ActiveUsersDescending, PostsDescending, CommentsDescending, InteractionsDescending, ImagesDescending, VideosDescending, StorageDescending, ApiRequestsDescending, PushDescending, EmailDescending, AiTokensDescending, AiCostDescending) sort at most 500 registry rows and page with skip only (no continuation token)." + _Auth),
                async (ctx, query) =>
                {
                    await CallerAsync(ctx).ConfigureAwait(false);
                    return await _Usage.EnumerateAsync(query, DateTime.UtcNow, ctx.Token).ConfigureAwait(false);
                });
        }

        #endregion

        #region Private-Methods

        private async Task CallerAsync(HttpContextBase ctx)
        {
            PlatformCaller? caller = await _Platform.AuthenticateAsync(ctx.Request.RetrieveHeaderValue("Authorization"), ClientAddressResolver.Resolve(ctx, _Settings.Rest), ctx.Token).ConfigureAwait(false);
            if (caller == null) throw new ApiErrorException(401, "Unauthorized", "A valid platform administrator token is required.");
        }

        private async Task SummaryAsync(HttpContextBase ctx)
        {
            await CallerAsync(ctx).ConfigureAwait(false);
            PlatformUsageTarget target = await _Usage.ForAsync(RouteHelper.Param(ctx, "propertyId"), ctx.Token).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 200, await target.Usage.SummaryPageAsync(ctx.Request.Method==HttpMethod.GET?RouteHelper.ReadEnumerationQuery<EnumerationQuery>(ctx):RouteHelper.ReadEnumerationBody<EnumerationQuery>(ctx), DateTime.UtcNow, ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task ExportAsync(HttpContextBase ctx)
        {
            await CallerAsync(ctx).ConfigureAwait(false);
            PlatformUsageTarget target = await _Usage.ForAsync(RouteHelper.Param(ctx, "propertyId"), ctx.Token).ConfigureAwait(false);
            await AdminUsageRoutes.SendFileAsync(ctx, await target.Usage.ExportAsync(AdminUsageRoutes.ReadQuery(ctx), DateTime.UtcNow, target.Record.Name, ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        #endregion
    }
}
