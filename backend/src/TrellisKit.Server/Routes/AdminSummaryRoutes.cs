namespace TrellisKit.Server.Routes
{
    using System;
    using System.Threading.Tasks;
    using TrellisKit.Core;
    using TrellisKit.Core.Exceptions;
    using TrellisKit.Core.Requests;
    using TrellisKit.Core.Responses;
    using TrellisKit.Server.Services;
    using TrellisKit.Server.Services.AdminSummary;
    using WatsonWebserver;
    using WatsonWebserver.Core;
    using WatsonWebserver.Core.Routing;
    using static TrellisKit.Server.Routes.OpenApiExtensions;

    /// <summary>
    /// Daily administrator summary routes (docs/ADMIN_SUMMARY.md): read the report of a day and send it now.
    /// </summary>
    public sealed class AdminSummaryRoutes
    {
        #region Private-Members

        private const string _Base = Constants.ApiPrefix + "/admin/summary";
        private const string _Tag = "Admin: summary";
        private readonly PropertyDirectory _Directory;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the routes.
        /// </summary>
        /// <param name="directory">Property directory.</param>
        /// <exception cref="ArgumentNullException">directory is null.</exception>
        public AdminSummaryRoutes(PropertyDirectory directory)
        {
            _Directory = directory ?? throw new ArgumentNullException(nameof(directory));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Register routes.
        /// </summary>
        /// <param name="server">Watson server.</param>
        /// <exception cref="ArgumentNullException">server is null.</exception>
        public void Register(Webserver server)
        {
            ArgumentNullException.ThrowIfNull(server);
            RoutingGroup routes = server.Routes.PostAuthentication;
            routes.Static.Add(HttpMethod.GET, _Base, ReadAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Read the administrator summary of a day", _Tag, "The report the daily summary email contains: sections of metric lines for one local day (usage.timeZone; operational lines use UTC days), each with the day before and the seven-day average, plus skipped configured items. Query date=yyyy-MM-dd (default yesterday). SystemAdmin or the admin token.")
                    .QueryParam("date", "Local date yyyy-MM-dd (default yesterday)").Returns("AdminSummaryReport")
                    .Bounded("AdminSummaryReport", "the engine default items plus at most 60 configured items, each with at most 21 group lines").Resp(400, "ValidationFailed").Resp(403, "Forbidden"));
            routes.Static.Add(HttpMethod.POST, _Base + "/send", SendAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Send the administrator summary now", _Tag, "Body { date?, to? }: with to, one copy of the day's summary goes to that address; without it every configured adminSummary.recipients address that has not received that date's summary gets one (each date once per recipient, shared with the daily worker). Audited admin_summary.send. SystemAdmin or the admin token.")
                    .Body("AdminSummarySendRequest", false).Returns("AdminSummarySendResult").Bounded("AdminSummarySendResult", "at most 20 recipients").Resp(400, "ValidationFailed").Resp(403, "Forbidden"));
        }

        #endregion

        #region Private-Methods

        private async Task<ApplicationServices> AdminAsync(HttpContextBase ctx)
        {
            ApplicationServices services = _Directory.For(ctx);
            ContentActor actor = await services.Actors.ResolveAsync(ctx, ctx.Token).ConfigureAwait(false);
            if (!actor.IsSystemAdmin) throw ApiErrorException.Forbidden("Administrator access is required for the summary.");
            return services;
        }

        private async Task ReadAsync(HttpContextBase ctx)
        {
            ApplicationServices services = await AdminAsync(ctx).ConfigureAwait(false);
            DateOnly? date = AdminSummaryService.ParseDate(RouteHelper.Query(ctx, "date"), "date");
            await RouteHelper.SendJsonAsync(ctx, 200, await services.AdminSummary.BuildAsync(date, ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task SendAsync(HttpContextBase ctx)
        {
            ApplicationServices services = await AdminAsync(ctx).ConfigureAwait(false);
            AdminSummarySendRequest request = RouteHelper.ReadJson<AdminSummarySendRequest>(ctx);
            DateOnly? date = AdminSummaryService.ParseDate(request.Date, "date");
            AdminSummarySendResult result = await services.AdminSummary.SendAsync(date, request.To, ctx.Token).ConfigureAwait(false);
            await services.Audit.WriteAsync(RequestInfo.From(ctx, services.Settings.Rest), "admin_summary.send", "AdminSummary", result.Date,
                result.Queued.Count + " queued, " + result.AlreadySent.Count + " already sent", ctx.Token).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 200, result).ConfigureAwait(false);
        }

        #endregion
    }
}
