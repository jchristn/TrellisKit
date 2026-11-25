namespace TrellisKit.Server.Routes
{
    using System;
    using System.Threading.Tasks;
    using TrellisKit.Core;
    using TrellisKit.Core.Enumeration;
    using TrellisKit.Core.Exceptions;
    using TrellisKit.Core.Requests;
    using TrellisKit.Server.Services;
    using WatsonWebserver;
    using WatsonWebserver.Core;
    using WatsonWebserver.Core.Routing;
    using static TrellisKit.Server.Routes.OpenApiExtensions;

    /// <summary>Reviewed exercise and technique catalog routes, with bounded collections.</summary>
    public sealed class ExerciseCatalogRoutes
    {
        private readonly PropertyDirectory _Directory;
        private const string _Tag = "Exercise catalog";
        private const string _Shape = "CatalogEntry { id,kind,name,slug,summary,program,difficulty,movementPattern,plane,laterality,techniqueType,fromPosition,toPosition,aliases,modalityDetails:{localizedNames:[{languageTag,text}],familyKey,attireKey,levelKey,notationSystemKey,notationText},apparatusSetup:{apparatusKey,manufacturer,model,setupText,springSetupText},primaryMuscles,secondaryMuscles,equipment,instructions,cues,commonErrors,regressions,progressions,substitutions,contraindications,lessonIds,sources [{url,title,license,attribution,method,model,retrievedUtc}],labels,tags,status,revision,createdUtc,updatedUtc,publishedUtc,reviewedUtc,reviewNote,viewer {canEdit,canSubmit,canReview} }";
        /// <summary>Instantiate for the property directory.</summary>
        public ExerciseCatalogRoutes(PropertyDirectory directory) { _Directory = directory; }
        /// <summary>Register anonymous, member, and library staff routes.</summary>
        public void Register(Webserver server)
        {
            RoutingGroup pre = server.Routes.PreAuthentication;
            RoutingGroup post = server.Routes.PostAuthentication;
            pre.Static.Add(HttpMethod.GET, Constants.ApiPrefix + "/catalog/config", async ctx =>
            {
                await ActorAsync(ctx, true).ConfigureAwait(false);
                await RouteHelper.SendJsonAsync(ctx,200,await _Directory.For(ctx).Library.ExerciseCatalog.ConfigAsync(ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
            }, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Read catalog configuration",_Tag,"Anonymous. Requires features.catalog. Property programs, muscle/equipment vocabularies, kinds, difficulties, relationshipKinds and bounded descriptorProfiles (max 50; families/attires/levels/notationSystems max 100 each; nameLanguages max 20). Public catalog IP rate limit applies.").Returns("{ programs,muscles,equipment,kinds,difficulties,relationshipKinds,descriptorProfiles }").Resp(404,"NotAvailable").Resp(429,"RateLimited"));
            RegisterReads(pre, Constants.ApiPrefix + "/public/catalog/entries", false, true);
            RegisterReads(post, Constants.ApiPrefix + "/catalog/entries", false, false);
            string path = Constants.ApiPrefix + "/admin/catalog/entries";
            RegisterReads(post,path,true,false);
            post.Static.Add(HttpMethod.POST,path,async ctx =>
            {
                ApplicationServices services = _Directory.For(ctx);
                ContentActor actor = await ActorAsync(ctx,false).ConfigureAwait(false);
                await RouteHelper.SendJsonAsync(ctx,201,await services.Library.ExerciseCatalog.CreateAsync(actor,RequestInfo.From(ctx,services.Settings.Rest),RouteHelper.ReadJson<CatalogEntryRequest>(ctx),ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
            },RouteHelper.HandleExceptionAsync,openApiMetadata: Doc("Create catalog draft",_Tag,"LibraryEditor or SystemAdmin. Validated structured content and licensed source references. Slug uses the common public-page rules: 3 to 80 lowercase letters/digits, single separating hyphens, and no reserved words; matches POST /api/v1/public-pages/slugs/check. Optional modalityDetails notationText 1 to 500 NFC characters requires its configured notationSystemKey. Optional apparatusSetup requires configured and selected equipment; manufacturer/model max 120, setup/spring text max 500, model requires manufacturer, springs require manufacturer and model. Initial status Draft. Sources carry provenance, never automatic publication.").Body("CatalogEntryRequest (editable CatalogEntry fields; no workflow state)").Returns(_Shape).Resp(201,"Created").Resp(400,"ValidationFailed or ContentFiltered").Resp(403,"Forbidden").Resp(409,"SlugInUse"));
            post.Parameter.Add(HttpMethod.PUT,path + "/{id}",async ctx =>
            {
                await RouteHelper.SendJsonAsync(ctx,200,await _Directory.For(ctx).Library.ExerciseCatalog.UpdateAsync(await ActorAsync(ctx,false).ConfigureAwait(false),RouteHelper.Param(ctx,"id"),RouteHelper.ReadJson<CatalogEntryRequest>(ctx),ctx.Token,RequestInfo.From(ctx,_Directory.For(ctx).Settings.Rest)).ConfigureAwait(false)).ConfigureAwait(false);
            },RouteHelper.HandleExceptionAsync,openApiMetadata: Doc("Edit catalog draft",_Tag,"Full editable aggregate replacement; expectedRevision required. Slug uses the same 3-to-80-character, non-reserved public-page rules as creation. Editing reviewed content returns it to Draft and removes public visibility until approval. Relationships are preserved. Omitted/null modalityDetails or apparatusSetup preserves that group; an explicit object replaces its entire group including omitted fields.").PathParam("id","Catalog identifier").Body("CatalogEntryRequest plus expectedRevision").Returns(_Shape).Resp(400,"ValidationFailed or ContentFiltered").Resp(403,"Forbidden").Resp(404,"NotFound").Resp(409,"CatalogChanged or SlugInUse"));
            foreach (string action in new[] { "submit", "approve", "reject", "archive" })
            {
                post.Parameter.Add(HttpMethod.POST,path + "/{id}/" + action,async ctx => await RouteHelper.SendJsonAsync(ctx,200,await _Directory.For(ctx).Library.ExerciseCatalog.ActionAsync(await ActorAsync(ctx,false).ConfigureAwait(false),RouteHelper.Param(ctx,"id"),action,RouteHelper.ReadJson<CatalogActionRequest>(ctx),ctx.Token,RequestInfo.From(ctx,_Directory.For(ctx).Settings.Rest)).ConfigureAwait(false)).ConfigureAwait(false),RouteHelper.HandleExceptionAsync,
                    openApiMetadata: Doc("Catalog " + action,_Tag,"LibraryEditor or SystemAdmin; approval/rejection requires SystemAdmin and InReview. Submit checks publication readiness; approve explicitly reviews source licensing and content. No automatic publication.").PathParam("id","Catalog identifier").Body("{ expectedRevision, note? }").Returns(_Shape).Resp(400,"ValidationFailed or ContentFiltered").Resp(403,"Forbidden").Resp(404,"NotFound").Resp(409,"CatalogChanged or InvalidTransition"));
            }
            post.Parameter.Add(HttpMethod.DELETE,path + "/{id}",async ctx =>
            {
                if (!Int64.TryParse(RouteHelper.Query(ctx,"expectedRevision"),out long revision)) throw ApiErrorException.Field("expectedRevision","A positive revision query parameter is required.");
                await _Directory.For(ctx).Library.ExerciseCatalog.ActionAsync(await ActorAsync(ctx,false).ConfigureAwait(false),RouteHelper.Param(ctx,"id"),"delete",new CatalogActionRequest { ExpectedRevision = revision },ctx.Token,RequestInfo.From(ctx,_Directory.For(ctx).Settings.Rest)).ConfigureAwait(false);
                await RouteHelper.SendEmptyAsync(ctx,204).ConfigureAwait(false);
            },RouteHelper.HandleExceptionAsync,openApiMetadata: Doc("Delete catalog entry",_Tag,"Library staff. Soft deletion removes public visibility. Supply expectedRevision query parameter.").PathParam("id","Catalog identifier").Resp(204,"Deleted").Resp(400,"ValidationFailed or ContentFiltered").Resp(403,"Forbidden").Resp(404,"NotFound").Resp(409,"CatalogChanged"));
            post.Parameter.Add(HttpMethod.PUT,path + "/{id}/relationships",async ctx => await RouteHelper.SendJsonAsync(ctx,200,await _Directory.For(ctx).Library.ExerciseCatalog.SetRelationshipsAsync(await ActorAsync(ctx,false).ConfigureAwait(false),RouteHelper.Param(ctx,"id"),RouteHelper.ReadJson<CatalogRelationshipsRequest>(ctx),ctx.Token,RequestInfo.From(ctx,_Directory.For(ctx).Settings.Rest)).ConfigureAwait(false)).ConfigureAwait(false),RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Replace catalog relationships",_Tag,"Library staff. At most 100 directed edges. Same-property targets; no duplicates or self references. Atomic revision check and replacement; source returns to Draft for review.").PathParam("id","Catalog source identifier").Body("{ expectedRevision, relationships: [{targetId,kind,note,labels,tags}] }").Returns(_Shape).Resp(400,"ValidationFailed or ContentFiltered").Resp(403,"Forbidden").Resp(404,"NotFound").Resp(409,"CatalogChanged"));
        }
        private void RegisterReads(RoutingGroup group,string path,bool admin,bool anonymous)
        {
            EnumerationRoutes.Add(group,path,admin ? ExerciseCatalogCollections.AdminEntries : ExerciseCatalogCollections.Entries,_Shape,() => Doc("List catalog entries",_Tag,admin ? "Library staff workflow inventory. Filters kind/program/difficulty/muscle/equipment/familyKey/nameLanguage/attireKey/levelKey/notationSystemKey/status; search name/summary/aliases/localized names, metadata filters, continuation paging." : "Published entries only. Filters kind/program/difficulty/muscle/equipment/familyKey/nameLanguage/attireKey/levelKey/notationSystemKey; search name/summary/aliases/localized names, metadata filters, continuation paging."),async (ctx,q) => await _Directory.For(ctx).Library.ExerciseCatalog.ListAsync(await ActorAsync(ctx,anonymous).ConfigureAwait(false),q,admin,ctx.Token).ConfigureAwait(false));
            group.Parameter.Add(HttpMethod.GET,path + "/{idOrSlug}",async ctx => await RouteHelper.SendJsonAsync(ctx,200,await _Directory.For(ctx).Library.ExerciseCatalog.GetAsync(await ActorAsync(ctx,anonymous).ConfigureAwait(false),RouteHelper.Param(ctx,"idOrSlug"),admin,ctx.Token).ConfigureAwait(false)).ConfigureAwait(false),RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Read catalog entry",_Tag,admin ? "Library staff can read any workflow state." : "Published entry; private review notes and inaccessible lesson references omitted.").PathParam("idOrSlug","Catalog identifier or current slug").Returns(_Shape).Resp(403,"Forbidden").Resp(404,"NotFound or NotAvailable").Resp(429,"RateLimited"));
            EnumerationRoutes.Add(group,path + "/{id}/relationships",ExerciseCatalogCollections.Relationships,"CatalogRelationship {id,sourceId,targetId,kind,note,labels,tags,targetName,targetSlug,targetKind,createdUtc}",() => Doc("List catalog relationships",_Tag,"Bounded outgoing edges, filter kind and search target name/note. Public reads require published source and targets."),async (ctx,q) => await _Directory.For(ctx).Library.ExerciseCatalog.RelationshipsAsync(await ActorAsync(ctx,anonymous).ConfigureAwait(false),RouteHelper.Param(ctx,"id"),q,admin,ctx.Token).ConfigureAwait(false),"id");
        }
        private async Task<ContentActor> ActorAsync(HttpContextBase ctx,bool anonymous)
        {
            ApplicationServices services = _Directory.For(ctx);
            ContentActor actor = anonymous ? services.Actors.Anonymous(RequestInfo.From(ctx,services.Settings.Rest).SourceIp) : await services.Actors.ResolveAsync(ctx,ctx.Token).ConfigureAwait(false);
            if (anonymous)
            {
                services.Limits.EnforceIp(actor.SourceIp, "public-catalog", services.Settings.RateLimits.PublicFeedPerMinute, RateLimitService.Minute);
                ctx.Response.Headers["Cache-Control"] = "public, max-age=0, must-revalidate";
            }
            else services.Media.EnforceRead(actor);
            return actor;
        }
    }
}
