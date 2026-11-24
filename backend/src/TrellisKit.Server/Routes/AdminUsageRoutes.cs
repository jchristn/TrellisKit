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
    using WatsonWebserver.Core.OpenApi;
    using WatsonWebserver.Core.Routing;
    using static TrellisKit.Server.Routes.OpenApiExtensions;

    /// <summary>
    /// Property usage metering routes (docs/USAGE_METERING_CONTRACT.md section 7.1): the summary with series, the daily
    /// usage record enumeration, and the export. Admin group (SystemAdmin, a delegated platform session, or the
    /// break-glass token).
    /// </summary>
    public class AdminUsageRoutes
    {
        #region Public-Members

        /// <summary>
        /// The summary query parameters as documented.
        /// </summary>
        public const string SummaryQueryDoc = "Query period (LastDay, LastWeek, LastMonth (default), Last90Days, PreviousQuarter, ThisQuarter, LastYear, ThisWeek, ThisMonth, ThisYear, Range; weeks start on the property usage.weekStartsOn, Monday by default), from and to (Range: local dates yyyy-MM-dd with to inclusive, or ISO 8601 date-times with an offset; at most 1830 days), bucket (Hour, Day, Week, Month; default by length), meters (comma-separated meter ids or groups users, content, interactions, media, storage, platform, ai).";

        /// <summary>
        /// The usage record item shape as documented.
        /// </summary>
        public const string RecordItem = "UsageRecord { id, propertyId, meter, group, kind, unit, day, timeZone, periodStartUtc, periodEndUtc, value, closed, computedUtc, createdUtc, labels, tags }";

        /// <summary>
        /// The summary bound as documented.
        /// </summary>
        public const string SummaryBound = "buckets at most 400, meters at most the usage meter catalog (35), values parallel to buckets";

        #endregion

        #region Private-Members

        private const string _Tag = "Admin usage";
        private const string _Base = Constants.ApiPrefix + "/admin/usage";
        private readonly PropertyDirectory _Directory;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the routes.
        /// </summary>
        /// <param name="directory">Property directory.</param>
        /// <exception cref="ArgumentNullException">The directory is null.</exception>
        public AdminUsageRoutes(PropertyDirectory directory)
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
            RoutingGroup post = server.Routes.PostAuthentication;
            post.Static.Add(HttpMethod.GET, _Base + "/summary", SummaryAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: SummaryDoc(Doc("Usage summary", _Tag, "Per-meter period totals, the previous equivalent period, the change, and a series with one value per bucket, in the property time zone (usage.timeZone). Admin.")));
            post.Static.Add(HttpMethod.POST, _Base + "/summary/enumerate", SummaryAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: SummaryDoc(Doc("Usage summary", _Tag, "Per-meter period totals, the previous equivalent period, the change, and a series with one value per bucket, in the property time zone (usage.timeZone). Admin.")));
            EnumerationRoutes.Add(post, _Base, UsageCollections.Records, RecordItem,
                () => Doc("List daily usage records", _Tag, "One record per meter per local day. The standard created window applies to periodStartUtc. Filters meter (an id or a group), fromDay and toDay (local dates, inclusive), closed. Search over meter. Labels and tags (UsageRecord). Admin."),
                async (ctx, query) =>
                {
                    ApplicationServices s = await AdminAsync(ctx).ConfigureAwait(false);
                    return await s.Usage.EnumerateAsync(query, ctx.Token).ConfigureAwait(false);
                });
            post.Static.Add(HttpMethod.GET, _Base + "/export", ExportAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: ExportDoc(Doc("Export usage", _Tag, "A stable usage export of a period: daily rows (granularity Day, default) or hourly rows (granularity Hour, at most 31 days inside the hourly retention) and period totals. JSON (format json, default; schemaVersion 1 with complete and closedThroughDay) or CSV (format csv). Admin.")));
        }

        /// <summary>
        /// Document the summary query parameters and response.
        /// </summary>
        /// <param name="metadata">Metadata.</param>
        /// <returns>Metadata.</returns>
        public static OpenApiRouteMetadata SummaryDoc(OpenApiRouteMetadata metadata)
        {
            ArgumentNullException.ThrowIfNull(metadata);
            return metadata.QueryParam("period", SummaryQueryDoc).QueryParam("from", "Range start").QueryParam("to", "Range end").QueryParam("bucket", "Hour, Day, Week, or Month")
                .QueryParam("meters", "Meter ids or groups").Enumeration("UsageObservationRow", "CreatedDescending, CreatedAscending", "period, from, to, bucket, meters", true).Resp(400, "ValidationFailed");
        }

        /// <summary>
        /// Document the export query parameters and response.
        /// </summary>
        /// <param name="metadata">Metadata.</param>
        /// <returns>Metadata.</returns>
        public static OpenApiRouteMetadata ExportDoc(OpenApiRouteMetadata metadata)
        {
            ArgumentNullException.ThrowIfNull(metadata);
            return metadata.QueryParam("period", "As the summary").QueryParam("from", "Range start").QueryParam("to", "Range end").QueryParam("meters", "Meter ids or groups")
                .QueryParam("format", "json (default) or csv").QueryParam("granularity", "Day (default) or Hour")
                .File("application/json", "Usage export (JSON, or text/csv for format csv) as an attachment; at most 1830 days of daily rows").Resp(400, "ValidationFailed");
        }

        /// <summary>
        /// Read the usage query from the query string.
        /// </summary>
        /// <param name="ctx">Context.</param>
        /// <returns>Query.</returns>
        public static UsageQuery ReadQuery(HttpContextBase ctx)
        {
            ArgumentNullException.ThrowIfNull(ctx);
            return new UsageQuery
            {
                Period = RouteHelper.Query(ctx, "period"),
                From = RouteHelper.Query(ctx, "from"),
                To = RouteHelper.Query(ctx, "to"),
                Bucket = RouteHelper.Query(ctx, "bucket"),
                Meters = RouteHelper.Query(ctx, "meters"),
                Format = RouteHelper.Query(ctx, "format"),
                Granularity = RouteHelper.Query(ctx, "granularity")
            };
        }

        /// <summary>
        /// Send an export file as an attachment.
        /// </summary>
        /// <param name="ctx">Context.</param>
        /// <param name="file">File.</param>
        /// <returns>Task.</returns>
        public static async Task SendFileAsync(HttpContextBase ctx, UsageExportFile file)
        {
            ArgumentNullException.ThrowIfNull(ctx);
            ArgumentNullException.ThrowIfNull(file);
            ctx.Response.StatusCode = 200;
            ctx.Response.ContentType = file.ContentType;
            ctx.Response.Headers["Content-Disposition"] = "attachment; filename=\"" + file.FileName + "\"";
            await ctx.Response.Send(file.Body, ctx.Token).ConfigureAwait(false);
        }

        #endregion

        #region Private-Methods

        private async Task<ApplicationServices> AdminAsync(HttpContextBase ctx)
        {
            ApplicationServices s = _Directory.For(ctx);
            ContentActor actor = await s.Actors.ResolveAsync(ctx, ctx.Token).ConfigureAwait(false);
            if (!actor.IsSystemAdmin) throw ApiErrorException.Forbidden("Admin access is required.");
            return s;
        }

        private async Task SummaryAsync(HttpContextBase ctx)
        {
            ApplicationServices s = await AdminAsync(ctx).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 200, await s.Usage.SummaryPageAsync(ctx.Request.Method==HttpMethod.GET?RouteHelper.ReadEnumerationQuery<EnumerationQuery>(ctx):RouteHelper.ReadEnumerationBody<EnumerationQuery>(ctx), DateTime.UtcNow, ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task ExportAsync(HttpContextBase ctx)
        {
            ApplicationServices s = await AdminAsync(ctx).ConfigureAwait(false);
            await SendFileAsync(ctx, await s.Usage.ExportAsync(ReadQuery(ctx), DateTime.UtcNow, s.Property.Name, ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        #endregion
    }
}
