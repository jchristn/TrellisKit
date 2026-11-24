namespace TrellisKit.Server.Routes
{
    using System;
    using System.Threading.Tasks;
    using TrellisKit.Core;
    using TrellisKit.Core.Exceptions;
    using TrellisKit.Core.Metrics;
    using TrellisKit.Core.Requests;
    using TrellisKit.Server.Services;
    using TrellisKit.Server.Services.Metrics;
    using WatsonWebserver;
    using WatsonWebserver.Core;
    using WatsonWebserver.Core.Routing;
    using static TrellisKit.Server.Routes.OpenApiExtensions;

    /// <summary>Platform-host-only metrics comparisons. Property administrator tokens cannot cross this boundary.</summary>
    public sealed class PlatformMetricsRoutes
    {
        private readonly PlatformService _Platform;
        private readonly PlatformMetricsService _Metrics;
        private readonly SettingsService _Settings;
        private const string _Base = Constants.ApiPrefix + "/platform";

        /// <summary>Create platform metrics routes.</summary>
        /// <param name="platform">Platform authentication and registry.</param>
        /// <param name="metrics">Bounded cross-property reader.</param>
        /// <param name="settings">Client-address resolver settings.</param>
        public PlatformMetricsRoutes(PlatformService platform, PlatformMetricsService metrics, SettingsService settings)
        {
            _Platform = platform ?? throw new ArgumentNullException(nameof(platform));
            _Metrics = metrics ?? throw new ArgumentNullException(nameof(metrics));
            _Settings = settings ?? throw new ArgumentNullException(nameof(settings));
        }

        /// <summary>Register on the platform realm. SingleProperty and non-platform hosts are rejected by realm resolution.</summary>
        /// <param name="server">Watson server.</param>
        public void Register(Webserver server)
        {
            RoutingGroup routes = server.Routes.PreAuthentication;
            routes.Static.Add(HttpMethod.POST, _Base + "/metrics/comparisons/enumerate", CompareAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Compare registered metrics across properties", "Platform", "Platform administrator token required. EnumerationQuery plus query (MetricQuery) and includeSeries. Scan at most 500 properties, return at most 20; keyset continuation or skip; ValueDescending or ValueAscending. Property labels/tags/search/status apply. Counts sum, gauges use the last bucket, distributions rank the worst bucket percentile; percentiles are never averaged. Missing/private values remain null.")
                    .Body("PlatformMetricQuery",false).Enumeration("PropertyMetricComparison", "CreatedDescending, CreatedAscending, ValueDescending, ValueAscending", "status", true).Resp(400, "ValidationFailed, UnknownMetric, InvalidContinuationToken").Resp(401, "Unauthorized"));
            routes.Parameter.Add(HttpMethod.POST, _Base + "/properties/{propertyId}/metrics/query", QueryAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Query a property's registered metric", "Platform", "Platform administrator token required. MetricEnumerationQuery body; pages actual observation rows and reads only this registered property's retained aggregates, even while disabled. No raw content is returned.")
                    .PathParam("propertyId", "Registered property id").Body("MetricEnumerationQuery",false).Enumeration("MetricObservationRow","CreatedDescending, CreatedAscending","period, seriesKey",true).Resp(400, "ValidationFailed").Resp(401, "Unauthorized").Resp(404, "NotFound"));
        }

        private async Task CallerAsync(HttpContextBase ctx)
        {
            PlatformCaller? caller = await _Platform.AuthenticateAsync(ctx.Request.RetrieveHeaderValue("Authorization"), ClientAddressResolver.Resolve(ctx, _Settings.Rest), ctx.Token).ConfigureAwait(false);
            if (caller == null) throw new ApiErrorException(401, "Unauthorized", "A valid platform administrator token is required.");
        }

        private async Task CompareAsync(HttpContextBase ctx)
        {
            await CallerAsync(ctx).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 200, await _Metrics.EnumerateAsync(RouteHelper.ReadJson<PlatformMetricQuery>(ctx), ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task QueryAsync(HttpContextBase ctx)
        {
            await CallerAsync(ctx).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 200, await _Metrics.QueryAsync(RouteHelper.Param(ctx, "propertyId"), RouteHelper.ReadEnumerationBody<MetricEnumerationQuery>(ctx), ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }
    }
}
