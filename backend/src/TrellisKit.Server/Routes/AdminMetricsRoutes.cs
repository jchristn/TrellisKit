namespace TrellisKit.Server.Routes
{
    using System;
    using System.Threading.Tasks;
    using TrellisKit.Core;
    using TrellisKit.Core.Exceptions;
    using TrellisKit.Core.Requests;
    using TrellisKit.Server.Services;
    using TrellisKit.Server.Services.Metrics;
    using WatsonWebserver;
    using WatsonWebserver.Core;
    using WatsonWebserver.Core.Routing;
    using static TrellisKit.Server.Routes.OpenApiExtensions;

    /// <summary>Every metric collection uses standard enumeration; source windows remain independently bounded.</summary>
    public sealed class AdminMetricsRoutes
    {
        private readonly PropertyDirectory _Directory;
        private const string _Base=Constants.ApiPrefix+"/admin/metrics";
        /// <summary>Create property-resolved metric routes.</summary>
        public AdminMetricsRoutes(PropertyDirectory directory) { _Directory=directory??throw new ArgumentNullException(nameof(directory)); }
        /// <summary>Register canonical POST enumerations and equivalent GET forms.</summary>
        public void Register(Webserver server)
        {
            RoutingGroup routes=server.Routes.PostAuthentication;
            void Collection(string suffix,string title,string model,string filters,Func<HttpContextBase,Task> handler)
            {
                routes.Static.Add(HttpMethod.GET,_Base+suffix,handler,RouteHelper.HandleExceptionAsync,openApiMetadata:Doc(title,"Admin metrics","SystemAdmin. Standard paging, search, created windows and metadata filters.").Enumeration(model,"CreatedDescending, CreatedAscending"+(suffix==""?", KeyAscending":""),filters,true).Resp(400,"ValidationFailed, InvalidContinuationToken").Resp(403,"Forbidden"));
                routes.Static.Add(HttpMethod.POST,_Base+suffix+"/enumerate",handler,RouteHelper.HandleExceptionAsync,openApiMetadata:Doc(title,"Admin metrics","SystemAdmin. Standard paging, search, created windows and metadata filters.").Body("EnumerationQuery",false).Enumeration(model,"CreatedDescending, CreatedAscending"+(suffix==""?", KeyAscending":""),filters,true).Resp(400,"ValidationFailed, InvalidContinuationToken").Resp(403,"Forbidden"));
            }
            Collection("","Metric registry","MetricDefinition","kind, source, feature",RegistryAsync);
            Collection("/pages","Metric dashboard declarations","MetricPageDeclaration","",PagesAsync);
            Collection("/workers","Worker pass history","WorkerPassRecord","worker, outcome, from, to",WorkersAsync);
            Collection("/annotations","Metric annotations","MetricAnnotation","kind, from, to",AnnotationsAsync);
            routes.Static.Add(HttpMethod.POST,_Base+"/query",QueryAsync,RouteHelper.HandleExceptionAsync,openApiMetadata:Doc("Enumerate metric observations","Admin metrics","MetricEnumerationQuery: standard enumeration plus nested query (MetricQuery). Objects are individual current/comparison buckets. Scalar metric/unit/calendar/watermark context remains available on empty pages. Source selection at most 2000 buckets and 20 groups plus Other; page size uses standard admin clamp.").Body("MetricEnumerationQuery",false).Enumeration("MetricObservationRow","CreatedDescending, CreatedAscending","period, seriesKey",true).Resp(400,"ValidationFailed, UnknownMetric, InvalidContinuationToken").Resp(403,"Forbidden"));
            routes.Static.Add(HttpMethod.POST,_Base+"/query/enumerate",QueryAsync,RouteHelper.HandleExceptionAsync,openApiMetadata:Doc("Enumerate metric observations","Admin metrics","Canonical alias; identical enumeration semantics to /query.").Body("MetricEnumerationQuery",false).Enumeration("MetricObservationRow","CreatedDescending, CreatedAscending","period, seriesKey",true).Resp(400,"ValidationFailed, UnknownMetric, InvalidContinuationToken").Resp(403,"Forbidden"));
        }
        private async Task<ApplicationServices> AdminAsync(HttpContextBase ctx)
        {
            ApplicationServices services=_Directory.For(ctx);ContentActor actor=await services.Actors.ResolveAsync(ctx,ctx.Token).ConfigureAwait(false);
            if(!actor.IsSystemAdmin)throw ApiErrorException.Forbidden("Administrator access is required for metrics.");return services;
        }
        private static EnumerationQuery Read(HttpContextBase ctx) => ctx.Request.Method==HttpMethod.GET?RouteHelper.ReadEnumerationQuery<EnumerationQuery>(ctx):RouteHelper.ReadEnumerationBody<EnumerationQuery>(ctx);
        private async Task RegistryAsync(HttpContextBase ctx) { ApplicationServices s=await AdminAsync(ctx).ConfigureAwait(false);await RouteHelper.SendJsonAsync(ctx,200,new MetricEnumerationService(s).Definitions(Read(ctx))).ConfigureAwait(false); }
        private async Task PagesAsync(HttpContextBase ctx) { ApplicationServices s=await AdminAsync(ctx).ConfigureAwait(false);await RouteHelper.SendJsonAsync(ctx,200,new MetricEnumerationService(s).Pages(Read(ctx))).ConfigureAwait(false); }
        private async Task WorkersAsync(HttpContextBase ctx) { ApplicationServices s=await AdminAsync(ctx).ConfigureAwait(false);await RouteHelper.SendJsonAsync(ctx,200,await new MetricEnumerationService(s).WorkersAsync(Read(ctx),ctx.Token).ConfigureAwait(false)).ConfigureAwait(false); }
        private async Task AnnotationsAsync(HttpContextBase ctx) { ApplicationServices s=await AdminAsync(ctx).ConfigureAwait(false);await RouteHelper.SendJsonAsync(ctx,200,await new MetricEnumerationService(s).AnnotationsAsync(Read(ctx),ctx.Token).ConfigureAwait(false)).ConfigureAwait(false); }
        private async Task QueryAsync(HttpContextBase ctx)
        {
            ApplicationServices s=await AdminAsync(ctx).ConfigureAwait(false);ContentActor actor=await s.Actors.ResolveAsync(ctx,ctx.Token).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx,200,await new MetricEnumerationService(s).QueryAsync(actor,RouteHelper.ReadEnumerationBody<MetricEnumerationQuery>(ctx),ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }
    }
}
