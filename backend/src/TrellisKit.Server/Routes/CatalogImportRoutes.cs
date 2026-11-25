namespace TrellisKit.Server.Routes
{
    using TrellisKit.Core;
    using TrellisKit.Core.Enumeration;
    using TrellisKit.Core.Requests;
    using TrellisKit.Server.Services;
    using WatsonWebserver;
    using WatsonWebserver.Core;
    using static TrellisKit.Server.Routes.OpenApiExtensions;

    /// <summary>System-administrator catalog source configuration and explicit licensing review.</summary>
    public sealed class CatalogImportRoutes
    {
        private readonly PropertyDirectory _Directory;
        private const string _Tag="Catalog imports";
        private const string _Shape="CatalogImportSource {id,name,url,license,attribution,enabled,maxEntries,revision,reviewedUtc,reviewedByUserId,lastAttemptUtc,createdUtc,updatedUtc,labels,tags}";
        /// <summary>Instantiate for the property directory.</summary>
        public CatalogImportRoutes(PropertyDirectory directory){_Directory=directory;}
        /// <summary>Register bounded source enumeration and revisioned mutations.</summary>
        public void Register(Webserver server)
        {
            string path=Constants.ApiPrefix+"/admin/catalog/import-sources";
            EnumerationRoutes.Add(server.Routes.PostAuthentication,path,ExerciseCatalogCollections.ImportSources,_Shape,()=>Doc("List catalog import sources",_Tag,"SystemAdmin. Catalog feature required. Search name/url; filters enabled/reviewed true or false, labels/tags and bounded continuation paging."),async(ctx,q)=>await _Directory.For(ctx).CatalogImports.EnumerateAsync(await _Directory.For(ctx).Actors.ResolveAsync(ctx,ctx.Token).ConfigureAwait(false),q,ctx.Token).ConfigureAwait(false));
            server.Routes.PostAuthentication.Static.Add(HttpMethod.POST,path,async ctx=>
            {
                ApplicationServices s=_Directory.For(ctx);
                await RouteHelper.SendJsonAsync(ctx,201,await s.CatalogImports.SaveAsync(await s.Actors.ResolveAsync(ctx,ctx.Token).ConfigureAwait(false),null,RouteHelper.ReadJson<CatalogImportSourceRequest>(ctx),RequestInfo.From(ctx,s.Settings.Rest),ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
            },RouteHelper.HandleExceptionAsync,openApiMetadata:Doc("Create catalog import source",_Tag,"SystemAdmin. Sources start unreviewed; no fetch until explicitly reviewed. HTTPS manifest URL, permissive license and bounded entries. No automatic publication.").Body("CatalogImportSourceRequest {name,url,license,attribution,enabled,maxEntries,labels,tags}").Returns(_Shape).Resp(201,"Created").Resp(400,"ValidationFailed").Resp(403,"Forbidden").Resp(404,"NotAvailable").Resp(409,"SourceUrlInUse"));
            server.Routes.PostAuthentication.Parameter.Add(HttpMethod.GET,path+"/{id}",async ctx=>
            {
                ApplicationServices s=_Directory.For(ctx);
                await RouteHelper.SendJsonAsync(ctx,200,await s.CatalogImports.GetAsync(await s.Actors.ResolveAsync(ctx,ctx.Token).ConfigureAwait(false),RouteHelper.Param(ctx,"id"),ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
            },RouteHelper.HandleExceptionAsync,openApiMetadata:Doc("Read catalog import source",_Tag,"SystemAdmin. Includes configuration revision and source review status.").PathParam("id","Source identifier").Returns(_Shape).Resp(403,"Forbidden").Resp(404,"NotFound or NotAvailable"));
            server.Routes.PostAuthentication.Parameter.Add(HttpMethod.PUT,path+"/{id}",async ctx=>
            {
                ApplicationServices s=_Directory.For(ctx);
                await RouteHelper.SendJsonAsync(ctx,200,await s.CatalogImports.SaveAsync(await s.Actors.ResolveAsync(ctx,ctx.Token).ConfigureAwait(false),RouteHelper.Param(ctx,"id"),RouteHelper.ReadJson<CatalogImportSourceRequest>(ctx),RequestInfo.From(ctx,s.Settings.Rest),ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
            },RouteHelper.HandleExceptionAsync,openApiMetadata:Doc("Edit catalog import source",_Tag,"SystemAdmin. Full replacement requires expectedRevision and revokes review even for metadata edits. Set enabled=false to withdraw a source; already planned jobs cannot apply changes after its revision changes.").PathParam("id","Source identifier").Body("CatalogImportSourceRequest {expectedRevision,name,url,license,attribution,enabled,maxEntries,labels,tags}").Returns(_Shape).Resp(400,"ValidationFailed").Resp(403,"Forbidden").Resp(404,"NotFound or NotAvailable").Resp(409,"CatalogSourceChanged or SourceUrlInUse"));
            server.Routes.PostAuthentication.Parameter.Add(HttpMethod.POST,path+"/{id}/review",async ctx=>
            {
                ApplicationServices s=_Directory.For(ctx);
                await RouteHelper.SendJsonAsync(ctx,200,await s.CatalogImports.ReviewAsync(await s.Actors.ResolveAsync(ctx,ctx.Token).ConfigureAwait(false),RouteHelper.Param(ctx,"id"),RouteHelper.ReadJson<CatalogImportReviewRequest>(ctx),RequestInfo.From(ctx,s.Settings.Rest),ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
            },RouteHelper.HandleExceptionAsync,openApiMetadata:Doc("Review catalog source license",_Tag,"SystemAdmin. Explicit acknowledgement authorizes future bounded draft imports from the exact configuration revision. Catalog publication remains separate human review.").PathParam("id","Source identifier").Body("CatalogImportReviewRequest {expectedRevision,licenseReviewed}").Returns(_Shape).Resp(400,"ValidationFailed").Resp(403,"Forbidden").Resp(404,"NotFound or NotAvailable").Resp(409,"CatalogSourceChanged"));
        }
    }
}
