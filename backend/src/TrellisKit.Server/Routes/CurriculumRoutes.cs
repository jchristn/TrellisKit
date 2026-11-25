namespace TrellisKit.Server.Routes
{
    using System;
    using System.Collections.Generic;
    using System.Threading.Tasks;
    using TrellisKit.Core;
    using TrellisKit.Core.Enumeration;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Requests;
    using TrellisKit.Server.Services;
    using WatsonWebserver;
    using WatsonWebserver.Core;
    using static TrellisKit.Server.Routes.OpenApiExtensions;

    /// <summary>
    /// Curriculum routes (docs/TRELLISKIT.md, Ranks, curricula, and attendance). Every route answers 404 NotAvailable
    /// while ranks.enabled is off.
    /// </summary>
    public class CurriculumRoutes
    {
        #region Private-Members

        private const string _Tag = "Curricula";
        private const string _AdminTag = "Admin curricula";
        private const string _Member = Constants.ApiPrefix + "/curricula";
        private const string _Admin = Constants.ApiPrefix + "/admin/curricula";
        private const string _View = "CurriculumView";
        private const string _Editors = " Curriculum editors (SystemAdmin or LibraryEditor); 403 Forbidden otherwise. 404 NotAvailable while ranks.enabled is off.";
        private const string _Readers = " Curriculum staff (SystemAdmin, LibraryEditor, or RankAwarder); 403 Forbidden otherwise. 404 NotAvailable while ranks.enabled is off.";
        private const string _ItemsBound = "items at most 500 (curriculum items); labels and tags 50 each";
        private readonly PropertyDirectory _Directory;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="directory">Property directory.</param>
        /// <exception cref="ArgumentNullException">The directory is null.</exception>
        public CurriculumRoutes(PropertyDirectory directory)
        {
            _Directory = directory ?? throw new ArgumentNullException(nameof(directory));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Register the routes.
        /// </summary>
        /// <param name="server">Webserver.</param>
        /// <exception cref="ArgumentNullException">The server is null.</exception>
        public void Register(Webserver server)
        {
            ArgumentNullException.ThrowIfNull(server);
            WatsonWebserver.Core.Routing.RoutingGroup post = server.Routes.PostAuthentication;
            EnumerationRoutes.Add(post, _Member, CurriculumCollections.Curricula, _View,
                () => Doc("List curricula", _Tag, "Published curricula the caller may see (Public, and Members for members and staff), position order by default, each with the caller's progress { total, required, completed, completedRequired, percent }. Filters fieldKey, labels, tags; search over name and description. 404 NotAvailable while ranks.enabled is off."),
                async (ctx, query) =>
                {
                    ContentActor actor = await ActorAsync(ctx).ConfigureAwait(false);
                    return await _Directory.For(ctx).Ranks.Curricula.ListAsync(actor, query, ctx.Token).ConfigureAwait(false);
                });
            post.Parameter.Add(HttpMethod.GET, _Member + "/{id}", async ctx =>
            {
                ContentActor actor = await ActorAsync(ctx).ConfigureAwait(false);
                await RouteHelper.SendJsonAsync(ctx, 200, await _Directory.For(ctx).Ranks.Curricula.GetAsync(actor, RouteHelper.Param(ctx, "id"), ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
            }, RouteHelper.HandleExceptionAsync, openApiMetadata: Doc("Read a curriculum", _Tag, "A published curriculum (Hidden ones by identifier) with its items in order and the caller's state per item: Lesson items complete from LessonProgress.Completed, Series items when every published lesson is complete, CatalogEntry and Custom items by self-completion (optional items) or verification (required items). Catalog entry titles are not resolved (title null; clients read the catalog when available). Staff see every status. 404 NotAvailable while ranks.enabled is off.")
                .PathParam("id", "Curriculum identifier").Bounded(_View, _ItemsBound).Resp(404, "NotFound or NotAvailable"));
            foreach (bool complete in new[] { true, false })
            {
                bool c = complete;
                post.Parameter.Add(c ? HttpMethod.PUT : HttpMethod.DELETE, Constants.ApiPrefix + "/me/curricula/{id}/items/{itemId}/complete", async ctx =>
                {
                    ContentActor actor = await ActorAsync(ctx).ConfigureAwait(false);
                    await RouteHelper.SendJsonAsync(ctx, 200, await _Directory.For(ctx).Ranks.Curricula.SelfCompleteAsync(actor, RouteHelper.Param(ctx, "id"), RouteHelper.Param(ctx, "itemId"), c, ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
                }, RouteHelper.HandleExceptionAsync, openApiMetadata: Doc(c ? "Complete a curriculum item" : "Undo a curriculum item", _Tag, (c
                        ? "Marks a CatalogEntry or Custom item of a published curriculum SelfComplete (idempotent; verified items stay Verified). Optional items count as done; required items count once a rank awarder verifies them."
                        : "Removes the caller's SelfComplete mark. Verified items cannot be undone (409 AlreadyVerified).") + " Lesson and Series items complete from library progress (409 NotSelfCompletable). 404 NotAvailable while ranks.enabled is off.")
                    .PathParam("id", "Curriculum identifier").PathParam("itemId", "Item identifier").Returns("CurriculumItemView").Resp(404, "NotFound or NotAvailable").Resp(409, c ? "NotSelfCompletable" : "NotSelfCompletable or AlreadyVerified"));
            }

            EnumerationRoutes.Add(post, _Admin, CurriculumCollections.AdminCurricula, _View,
                () => Doc("List curricula (staff)", _AdminTag, "Every curriculum in any status. Filters fieldKey, status (Draft, Published, Archived), visibility (Members, Public, Hidden), labels, tags." + _Readers),
                async (ctx, query) =>
                {
                    ContentActor actor = await ActorAsync(ctx).ConfigureAwait(false);
                    return await _Directory.For(ctx).Ranks.Curricula.ListAdminAsync(actor, query, ctx.Token).ConfigureAwait(false);
                });
            post.Static.Add(HttpMethod.POST, _Admin, async ctx =>
            {
                CurriculumRequest request = RouteHelper.ReadJson<CurriculumRequest>(ctx);
                ContentActor actor = await ActorAsync(ctx).ConfigureAwait(false);
                await RouteHelper.SendJsonAsync(ctx, 201, await _Directory.For(ctx).Ranks.Curricula.CreateAsync(actor, Info(ctx), request, ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
            }, RouteHelper.HandleExceptionAsync, openApiMetadata: Doc("Create a curriculum", _AdminTag, "Body { name, description, fieldKey, visibility, position, labels, tags }: name 1 to 120 characters; description at most 2000; fieldKey an enabled Rank field whose options group the items (optional); visibility Members (default), Public, or Hidden; position 0 to 100000. Created as Draft. Audited curriculum.create." + _Editors)
                .Body("CurriculumRequest").Returns(_View, 201).Resp(400, "ValidationFailed").Resp(403, "Forbidden").Resp(404, "NotAvailable"));
            post.Parameter.Add(HttpMethod.GET, _Admin + "/{id}", async ctx =>
            {
                ContentActor actor = await ActorAsync(ctx).ConfigureAwait(false);
                await RouteHelper.SendJsonAsync(ctx, 200, await _Directory.For(ctx).Ranks.Curricula.GetAdminAsync(actor, RouteHelper.Param(ctx, "id"), null, ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
            }, RouteHelper.HandleExceptionAsync, openApiMetadata: Doc("Read a curriculum (staff)", _AdminTag, "Any status, with items." + _Readers)
                .PathParam("id", "Curriculum identifier").Bounded(_View, _ItemsBound).Resp(403, "Forbidden").Resp(404, "NotFound or NotAvailable"));
            post.Parameter.Add(HttpMethod.PUT, _Admin + "/{id}", async ctx =>
            {
                CurriculumRequest request = RouteHelper.ReadJson<CurriculumRequest>(ctx, out HashSet<string> present);
                ContentActor actor = await ActorAsync(ctx).ConfigureAwait(false);
                await RouteHelper.SendJsonAsync(ctx, 200, await _Directory.For(ctx).Ranks.Curricula.UpdateAsync(actor, Info(ctx), RouteHelper.Param(ctx, "id"), request, present, ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
            }, RouteHelper.HandleExceptionAsync, openApiMetadata: Doc("Edit a curriculum", _AdminTag, "Any create field; omitted fields stay. fieldKey cannot change while items are grouped by options it lacks. Audited curriculum.update." + _Editors)
                .PathParam("id", "Curriculum identifier").Body("CurriculumRequest").Returns(_View).Resp(400, "ValidationFailed").Resp(403, "Forbidden").Resp(404, "NotFound or NotAvailable"));
            post.Parameter.Add(HttpMethod.PUT, _Admin + "/{id}/items", async ctx =>
            {
                CurriculumItemsRequest request = RouteHelper.ReadJson<CurriculumItemsRequest>(ctx);
                ContentActor actor = await ActorAsync(ctx).ConfigureAwait(false);
                await RouteHelper.SendJsonAsync(ctx, 200, await _Directory.For(ctx).Ranks.Curricula.ReplaceItemsAsync(actor, Info(ctx), RouteHelper.Param(ctx, "id"), request, ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
            }, RouteHelper.HandleExceptionAsync, openApiMetadata: Doc("Replace curriculum items", _AdminTag, "Body { items }: items [{ id, optionKey, itemType, targetId, customText, required, labels, tags }] in order (at most 500). itemType Lesson or Series needs an existing targetId; CatalogEntry a catalog entry identifier stored as text (no catalog lookup); Custom customText 1 to 300 characters. optionKey groups by an option of the curriculum's fieldKey. Listed ids keep their member progress; unlisted items are deleted with it. Audited curriculum.items." + _Editors)
                .PathParam("id", "Curriculum identifier").Body("CurriculumItemsRequest").Returns(_View).Resp(400, "ValidationFailed").Resp(403, "Forbidden").Resp(404, "NotFound or NotAvailable"));
            foreach (string action in new[] { "publish", "archive" })
            {
                string a = action;
                post.Parameter.Add(HttpMethod.POST, _Admin + "/{id}/" + a, async ctx =>
                {
                    ContentActor actor = await ActorAsync(ctx).ConfigureAwait(false);
                    CurriculumStatusEnum status = a == "publish" ? CurriculumStatusEnum.Published : CurriculumStatusEnum.Archived;
                    await RouteHelper.SendJsonAsync(ctx, 200, await _Directory.For(ctx).Ranks.Curricula.SetStatusAsync(actor, Info(ctx), RouteHelper.Param(ctx, "id"), status, ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
                }, RouteHelper.HandleExceptionAsync, openApiMetadata: Doc(a == "publish" ? "Publish a curriculum" : "Archive a curriculum", _AdminTag, (a == "publish"
                        ? "Draft or Archived to Published (needs at least one item). Audited curriculum.publish."
                        : "To Archived: hidden from members; member progress is kept. Audited curriculum.archive.") + _Editors)
                    .PathParam("id", "Curriculum identifier").Returns(_View).Resp(403, "Forbidden").Resp(404, "NotFound or NotAvailable").Resp(409, a == "publish" ? "InvalidStatus or CurriculumEmpty" : "InvalidStatus"));
            }
            post.Parameter.Add(HttpMethod.GET, _Admin + "/{id}/progress/{userId}", async ctx =>
            {
                ContentActor actor = await ActorAsync(ctx).ConfigureAwait(false);
                await RouteHelper.SendJsonAsync(ctx, 200, await _Directory.For(ctx).Ranks.Curricula.GetAdminAsync(actor, RouteHelper.Param(ctx, "id"), RouteHelper.Param(ctx, "userId"), ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
            }, RouteHelper.HandleExceptionAsync, openApiMetadata: Doc("Read a member's curriculum progress", _AdminTag, "The curriculum with the member's state per item and progress counts." + _Readers)
                .PathParam("id", "Curriculum identifier").PathParam("userId", "Member identifier").Bounded(_View, _ItemsBound).Resp(403, "Forbidden").Resp(404, "NotFound or NotAvailable"));
            post.Parameter.Add(HttpMethod.POST, _Admin + "/{id}/progress/{userId}/items/{itemId}/verify", async ctx =>
            {
                ContentActor actor = await ActorAsync(ctx).ConfigureAwait(false);
                await RouteHelper.SendJsonAsync(ctx, 200, await _Directory.For(ctx).Ranks.Curricula.VerifyAsync(actor, Info(ctx), RouteHelper.Param(ctx, "id"), RouteHelper.Param(ctx, "userId"), RouteHelper.Param(ctx, "itemId"), ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
            }, RouteHelper.HandleExceptionAsync, openApiMetadata: Doc("Verify a curriculum item", _AdminTag, "Marks any item Verified for the member (a verified item always counts). Nobody verifies their own items. Audited curriculum.verify. Rank awarders (RankAwarder or SystemAdmin); 403 Forbidden otherwise. 404 NotAvailable while ranks.enabled is off.")
                .PathParam("id", "Curriculum identifier").PathParam("userId", "Member identifier").PathParam("itemId", "Item identifier").Returns("CurriculumItemView").Resp(403, "Forbidden").Resp(404, "NotFound or NotAvailable"));
        }

        #endregion

        #region Private-Methods

        private RequestInfo Info(HttpContextBase ctx)
        {
            return RequestInfo.From(ctx, _Directory.For(ctx).Settings.Rest);
        }

        private Task<ContentActor> ActorAsync(HttpContextBase ctx)
        {
            return _Directory.For(ctx).Actors.ResolveAsync(ctx, ctx.Token);
        }

        #endregion
    }
}
