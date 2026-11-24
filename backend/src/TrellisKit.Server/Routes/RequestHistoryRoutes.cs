namespace TrellisKit.Server.Routes
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core;
    using TrellisKit.Core.Database;
    using TrellisKit.Core.Enumeration;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Exceptions;
    using TrellisKit.Core.Models;
    using TrellisKit.Core.Requests;
    using TrellisKit.Core.Responses;
    using TrellisKit.Core.Settings;
    using TrellisKit.Server.Services;
    using TrellisKit.Server.Services.Metrics;
    using WatsonWebserver;
    using WatsonWebserver.Core;
    using WatsonWebserver.Core.OpenApi;

    /// <summary>
    /// Admin request history routes backing the console's Home and Request History views.
    /// When capture is disabled the routes still answer, with empty results.
    /// </summary>
    public class RequestHistoryRoutes
    {
        #region Private-Members

        private const string _Base = Constants.ApiPrefix + "/request-history";
        private const string _Tag = "Request history";
        private readonly RequestHistorySettings _Settings;
        private readonly PropertyDirectory _Directory;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate request history routes.
        /// </summary>
        /// <param name="settings">Request history settings (the running instance).</param>
        /// <param name="directory">Property directory (each request reads only its property's history).</param>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        public RequestHistoryRoutes(RequestHistorySettings settings, PropertyDirectory directory)
        {
            _Settings = settings ?? throw new ArgumentNullException(nameof(settings));
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

            EnumerationRoutes.Add(server.Routes.PostAuthentication, _Base, PhaseOneCollections.RequestHistory, "RequestHistoryListItem",
                () => OpenApiExtensions.Doc("List request history", _Tag, "Newest first by default. Items omit headers and bodies. Search matches path and principal name. Filters: method, statusCode, pathContains, userId, routeTemplate, statusClass, errorCode, clientApp, clientPlatform, clientVersion; the time window is createdAfterUtc and createdBeforeUtc. Empty when capture is disabled. Admin only.")
                    .WithResponse(401, OpenApiResponseMetadata.Create("Missing or invalid token")),
                async (ctx, query) => await ListAsync(ctx, query, ctx.Token).ConfigureAwait(false));

            server.Routes.PostAuthentication.Static.Add(HttpMethod.GET, _Base + "/summary", SummaryAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: WithFilterParameters(OpenApiRouteMetadata.Create("Summarize request history", _Tag)
                    .WithDescription("Time-bucketed counts for charts. Every bucket in the range is returned, including empty ones. Admin only.")
                    .WithParameter(OpenApiParameterMetadata.Query("bucketMinutes", "Bucket width in minutes, 1 to 1440 (default 15).", false, OpenApiSchemaMetadata.Integer()))
                    .Enumeration("RequestHistoryBucket", "CreatedDescending, CreatedAscending", "fromUtc, toUtc, bucketMinutes, method, statusCode, pathContains, userId, routeTemplate, statusClass, errorCode, clientApp, clientPlatform, clientVersion", true)
                    .WithResponse(400, OpenApiResponseMetadata.Create("Missing range or too many buckets"))
                    .WithResponse(401, OpenApiResponseMetadata.Create("Missing or invalid token"))));

            server.Routes.PostAuthentication.Static.Add(HttpMethod.POST, _Base + "/summary/enumerate", SummaryAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: WithFilterParameters(OpenApiRouteMetadata.Create("Summarize request history", _Tag)
                    .WithDescription("Time-bucketed counts for charts. Every bucket in the range is returned, including empty ones. Admin only.")
                    .WithParameter(OpenApiParameterMetadata.Query("bucketMinutes", "Bucket width in minutes, 1 to 1440 (default 15).", false, OpenApiSchemaMetadata.Integer()))
                    .Body("EnumerationQuery",false).Enumeration("RequestHistoryBucket", "CreatedDescending, CreatedAscending", "fromUtc, toUtc, bucketMinutes, method, statusCode, pathContains, userId, routeTemplate, statusClass, errorCode, clientApp, clientPlatform, clientVersion", true)
                    .WithResponse(400, OpenApiResponseMetadata.Create("Missing range or too many buckets"))
                    .WithResponse(401, OpenApiResponseMetadata.Create("Missing or invalid token"))));

            server.Routes.PostAuthentication.Parameter.Add(HttpMethod.GET, _Base + "/{id}", ReadAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: OpenApiRouteMetadata.Create("Read a request history entry", _Tag)
                    .WithDescription("Full entry including redacted headers, possibly truncated bodies, labels, and tags. Admin only.")
                    .Returns("RequestHistoryEntry")
                    .WithResponse(404, OpenApiResponseMetadata.Create("Not found")));

            server.Routes.PostAuthentication.Parameter.Add(HttpMethod.DELETE, _Base + "/{id}", DeleteAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: OpenApiRouteMetadata.Create("Delete a request history entry", _Tag)
                    .WithResponse(204, OpenApiResponseMetadata.NoContent())
                    .WithResponse(404, OpenApiResponseMetadata.Create("Not found")));

            server.Routes.PostAuthentication.Static.Add(HttpMethod.DELETE, _Base, DeleteManyAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: WithFilterParameters(OpenApiRouteMetadata.Create("Bulk delete request history", _Tag)
                    .WithDescription("Deletes every entry matching the filter (and their labels and tags). With no filter, deletes all entries. Admin only.")
                    .WithResponse(200, OpenApiResponseMetadata.Create("Deleted count"))));
        }

        #endregion

        #region Private-Methods

        private static OpenApiRouteMetadata WithFilterParameters(OpenApiRouteMetadata metadata)
        {
            return metadata
                .WithParameter(OpenApiParameterMetadata.Query("method", "HTTP method, for example GET.", false, OpenApiSchemaMetadata.String()))
                .WithParameter(OpenApiParameterMetadata.Query("statusCode", "Exact status code.", false, OpenApiSchemaMetadata.Integer()))
                .WithParameter(OpenApiParameterMetadata.Query("pathContains", "Case-sensitive path substring.", false, OpenApiSchemaMetadata.String()))
                .WithParameter(OpenApiParameterMetadata.Query("routeTemplate", "Exact retained metric dimension.", false, OpenApiSchemaMetadata.String()))
                .WithParameter(OpenApiParameterMetadata.Query("statusClass", "Exact retained metric dimension (1xx through 5xx).", false, OpenApiSchemaMetadata.String()))
                .WithParameter(OpenApiParameterMetadata.Query("errorCode", "Exact retained metric dimension.", false, OpenApiSchemaMetadata.String()))
                .WithParameter(OpenApiParameterMetadata.Query("clientApp", "Exact retained metric dimension.", false, OpenApiSchemaMetadata.String()))
                .WithParameter(OpenApiParameterMetadata.Query("clientPlatform", "Exact retained metric dimension.", false, OpenApiSchemaMetadata.String()))
                .WithParameter(OpenApiParameterMetadata.Query("clientVersion", "Exact retained metric dimension.", false, OpenApiSchemaMetadata.String()))
                .WithParameter(OpenApiParameterMetadata.Query("userId", "User identifier.", false, OpenApiSchemaMetadata.String()))
                .WithParameter(OpenApiParameterMetadata.Query("fromUtc", "Inclusive start, ISO 8601 UTC.", false, OpenApiSchemaMetadata.String("date-time")))
                .WithParameter(OpenApiParameterMetadata.Query("toUtc", "Exclusive end, ISO 8601 UTC.", false, OpenApiSchemaMetadata.String("date-time")));
        }

        private static bool TryParseFilter(HttpContextBase ctx, out RequestHistoryFilter filter, out Dictionary<string, string> errors)
        {
            errors = new Dictionary<string, string>();
            filter = new RequestHistoryFilter
            {
                RouteTemplate = RouteHelper.Query(ctx, "routeTemplate"),
                StatusClass = RouteHelper.Query(ctx, "statusClass"),
                ErrorCode = RouteHelper.Query(ctx, "errorCode"),
                ClientApp = RouteHelper.Query(ctx, "clientApp"),
                ClientPlatform = RouteHelper.Query(ctx, "clientPlatform"),
                ClientVersion = RouteHelper.Query(ctx, "clientVersion"),
                Method = RouteHelper.Query(ctx, "method"),
                PathContains = RouteHelper.Query(ctx, "pathContains"),
                UserId = RouteHelper.Query(ctx, "userId"),
                StatusCode = RouteHelper.QueryInt(ctx, "statusCode", errors),
                FromUtc = RouteHelper.QueryUtc(ctx, "fromUtc", errors),
                ToUtc = RouteHelper.QueryUtc(ctx, "toUtc", errors)
            };

            int? bucketMinutes = RouteHelper.QueryInt(ctx, "bucketMinutes", errors);
            if (bucketMinutes.HasValue && (bucketMinutes.Value < 1 || bucketMinutes.Value > 1440)) errors["bucketMinutes"] = "Must be between 1 and 1440.";
            else if (bucketMinutes.HasValue) filter.BucketMinutes = bucketMinutes.Value;

            if (filter.FromUtc.HasValue && filter.ToUtc.HasValue && filter.ToUtc.Value <= filter.FromUtc.Value)
                errors["toUtc"] = "Must be later than fromUtc.";

            ValidateMetricFilters(filter, errors);
            return errors.Count == 0;
        }

        private static void ValidateMetricFilters(RequestHistoryFilter filter, Dictionary<string, string> errors)
        {
            if (filter.StatusClass != null && !System.Text.RegularExpressions.Regex.IsMatch(filter.StatusClass, "^[1-5]xx$")) errors["statusClass"] = "Use 1xx through 5xx.";
            if (filter.RouteTemplate?.Length > 300) errors["routeTemplate"] = "The dimension value is too long.";
            if (filter.StatusClass?.Length > 100) errors["statusClass"] = "The dimension value is too long.";
            if (filter.ErrorCode?.Length > 100) errors["errorCode"] = "The dimension value is too long.";
            if (filter.ClientApp?.Length > 100) errors["clientApp"] = "The dimension value is too long.";
            if (filter.ClientPlatform?.Length > 100) errors["clientPlatform"] = "The dimension value is too long.";
            if (filter.ClientVersion?.Length > 100) errors["clientVersion"] = "The dimension value is too long.";
        }

        private async Task<EnumerationResult<RequestHistoryListItem>> ListAsync(HttpContextBase ctx, EnumerationQuery query, CancellationToken token)
        {
            EnumerationRequest request = _Directory.For(ctx).Enumeration.Prepare(query, PhaseOneCollections.RequestHistory, true);
            Dictionary<string, string> errors = new Dictionary<string, string>();
            RequestHistoryFilter filter = new RequestHistoryFilter
            {
                RouteTemplate = request.Query.FilterString("routeTemplate"),
                StatusClass = request.Query.FilterString("statusClass"),
                ErrorCode = request.Query.FilterString("errorCode"),
                ClientApp = request.Query.FilterString("clientApp"),
                ClientPlatform = request.Query.FilterString("clientPlatform"),
                ClientVersion = request.Query.FilterString("clientVersion"),
                Method = request.Query.FilterString("method"),
                PathContains = request.Query.FilterString("pathContains"),
                UserId = request.Query.FilterString("userId")
            };
            long? status = request.Query.FilterLong("statusCode", errors);
            if (status != null) filter.StatusCode = (int)Math.Clamp(status.Value, 0, 999);
            ValidateMetricFilters(filter, errors);
            if (errors.Count > 0) throw ApiErrorException.Validation(errors, "One or more filters are invalid.");
            if (!_Settings.Enabled)
                return new EnumerationResult<RequestHistoryListItem> { MaxResults = request.MaxResults, Skip = request.Skip, EndOfResults = true, TotalRecords = 0, RecordsRemaining = 0 };
            EnumerationResult<RequestHistoryEntry> page = await _Directory.For(ctx).Database.RequestHistory.EnumerateAsync(request, filter, token).ConfigureAwait(false);
            List<RequestHistoryListItem> items = page.Objects.Select(RequestHistoryListItem.FromModel).ToList();
            await _Directory.For(ctx).Metadata.AttachAsync(TargetTypeEnum.RequestHistory, items, i => i.Id, true, token).ConfigureAwait(false);
            return page.With(items);
        }

        private async Task SummaryAsync(HttpContextBase ctx)
        {
            EnumerationQuery query=ctx.Request.Method==HttpMethod.GET?RouteHelper.ReadEnumerationQuery<EnumerationQuery>(ctx):RouteHelper.ReadEnumerationBody<EnumerationQuery>(ctx);
            Dictionary<string,string> errors=new Dictionary<string,string>();
            long? status=query.FilterLong("statusCode",errors),minutes=query.FilterLong("bucketMinutes",errors);
            if(minutes.HasValue&&(minutes<1||minutes>1440))errors["filters.bucketMinutes"]="Use 1 to 1440.";
            if(status.HasValue&&(status<100||status>599))errors["filters.statusCode"]="Use 100 to 599.";
            RequestHistoryFilter filter=new RequestHistoryFilter{Method=query.FilterString("method"),StatusCode=(int?)status,PathContains=query.FilterString("pathContains"),UserId=query.FilterString("userId"),RouteTemplate=query.FilterString("routeTemplate"),StatusClass=query.FilterString("statusClass"),ErrorCode=query.FilterString("errorCode"),ClientApp=query.FilterString("clientApp"),ClientPlatform=query.FilterString("clientPlatform"),ClientVersion=query.FilterString("clientVersion"),FromUtc=query.FilterUtc("fromUtc",errors),ToUtc=query.FilterUtc("toUtc",errors)};
            if(!filter.FromUtc.HasValue&&!filter.ToUtc.HasValue) { filter.ToUtc=TrellisKit.Core.Database.MetricsStore.Hour(DateTime.UtcNow);filter.FromUtc=filter.ToUtc.Value.AddDays(-1); }
            if(minutes.HasValue)filter.BucketMinutes=(int)minutes.Value;
            ValidateMetricFilters(filter,errors);RouteHelper.ThrowIfErrors(errors);
            try
            {
                RequestHistorySummary summary;
                if(_Settings.Enabled)summary=await _Directory.For(ctx).Database.RequestHistory.SummarizeAsync(filter,ctx.Token).ConfigureAwait(false);
                else { RequestHistorySummaryBuilder.ValidateRange(filter,out DateTime fromUtc,out DateTime toUtc);summary=RequestHistorySummaryBuilder.Build(fromUtc,toUtc,filter.BucketMinutes,new System.Data.DataTable()); }
                await RouteHelper.SendJsonAsync(ctx,200,AggregateEnumeration.Requests(_Directory.For(ctx),query,summary)).ConfigureAwait(false);
            }
            catch(ArgumentException e) { throw ApiErrorException.Validation(new Dictionary<string,string>{{"filters.fromUtc",e.Message}},"Invalid summary range."); }
        }

        private async Task ReadAsync(HttpContextBase ctx)
        {
            string id = ctx.Request.Url.Parameters["id"] ?? String.Empty;
            RequestHistoryEntry? entry = String.IsNullOrWhiteSpace(id)
                ? null
                : await _Directory.For(ctx).Database.RequestHistory.ReadAsync(id, ctx.Token).ConfigureAwait(false);
            if (entry == null)
            {
                await RouteHelper.SendErrorAsync(ctx, 404, "NotFound", "Request history entry not found.").ConfigureAwait(false);
                return;
            }
            await _Directory.For(ctx).Metadata.AttachOneAsync(TargetTypeEnum.RequestHistory, entry, entry.Id, true, ctx.Token).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 200, entry).ConfigureAwait(false);
        }

        private async Task DeleteAsync(HttpContextBase ctx)
        {
            string id = ctx.Request.Url.Parameters["id"] ?? String.Empty;
            bool deleted = !String.IsNullOrWhiteSpace(id)
                && await _Directory.For(ctx).Database.RequestHistory.DeleteAsync(id, ctx.Token).ConfigureAwait(false);
            if (!deleted)
            {
                await RouteHelper.SendErrorAsync(ctx, 404, "NotFound", "Request history entry not found.").ConfigureAwait(false);
                return;
            }
            await RouteHelper.SendEmptyAsync(ctx, 204).ConfigureAwait(false);
        }

        private async Task DeleteManyAsync(HttpContextBase ctx)
        {
            if (!TryParseFilter(ctx, out RequestHistoryFilter filter, out Dictionary<string, string> errors))
            {
                await RouteHelper.SendErrorAsync(ctx, 400, "ValidationFailed", "One or more query parameters are invalid.", errors).ConfigureAwait(false);
                return;
            }

            int deleted = await _Directory.For(ctx).Database.RequestHistory.DeleteManyAsync(filter, ctx.Token).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 200, new DeleteManyResponse { DeletedCount = deleted }).ConfigureAwait(false);
        }

        #endregion
    }
}
