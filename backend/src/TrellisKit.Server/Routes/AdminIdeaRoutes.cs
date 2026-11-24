namespace TrellisKit.Server.Routes
{
    using System;
    using System.Collections.Generic;
    using System.Threading.Tasks;
    using TrellisKit.Core;
    using TrellisKit.Core.Enumeration;
    using TrellisKit.Core.Requests;
    using TrellisKit.Server.Services;
    using WatsonWebserver;
    using WatsonWebserver.Core;
    using static TrellisKit.Server.Routes.OpenApiExtensions;

    /// <summary>
    /// Staff ideas board routes (docs/PHASE5_CONTRACT.md section 7.2) for SystemAdmin and SupportAgent: the staff board,
    /// staff ideas, edits, status changes, merges, deletes, status administration (SystemAdmin), and ideas from support
    /// tickets (publish-idea, link-idea).
    /// </summary>
    public class AdminIdeaRoutes
    {
        #region Private-Members

        private const string _Tag = "Admin ideas";
        private const string _Idea = "Idea (staff view: author always shown, moderation, sourceTicketId, voterCounts { total, members, nonMembers } on single reads)";
        private const string _Status = "IdeaStatus { id, key, label, color, isClosed, isInitial, sortOrder, isRetired, labels, tags }";
        private const string _Base = Constants.ApiPrefix + "/admin/ideas";
        private const string _Statuses = _Base + "/statuses";
        private const string _Tickets = Constants.ApiPrefix + "/admin/tickets";
        private const string _Staff = " SystemAdmin or SupportAgent.";
        private readonly PropertyDirectory _Directory;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the routes.
        /// </summary>
        /// <param name="directory">Property directory (resolves each request's property services).</param>
        /// <exception cref="ArgumentNullException">The directory is null.</exception>
        public AdminIdeaRoutes(PropertyDirectory directory)
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
            RegisterStatuses(post);
            EnumerationRoutes.Add(post, _Base, IdeaCollections.AdminIdeas, _Idea,
                () => Doc("List ideas (staff)", _Tag, "Every idea in any state. Orderings as the member board (default CreatedDescending). Filters status, open, authorUserId, audience, includeMerged, hidden (true: hidden or removed), merged (true: merged only)." + _Staff),
                (ctx, query) => RunAsync(ctx, actor => _Directory.For(ctx).Ideas.Ideas.AdminListAsync(actor, query, ctx.Token)));
            post.Static.Add(HttpMethod.POST, _Base, CreateAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Create an idea (staff)", _Tag, "Body { title, body, audience?, status? (a status key; default the initial status), showAuthor? (default true), labels?, tags? }. Audited idea.create." + _Staff)
                    .Body("IdeaRequest").Returns(_Idea, 201).Resp(400, "ValidationFailed or ContentFiltered").Resp(403, "Forbidden"));
            post.Parameter.Add(HttpMethod.GET, _Base + "/{id}", GetAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Read an idea (staff)", _Tag, "Any state, with voter counts by membership state (never names)." + _Staff)
                    .PathParam("id", "Idea identifier").Returns(_Idea).Resp(403, "Forbidden").Resp(404, "NotFound"));
            post.Parameter.Add(HttpMethod.PUT, _Base + "/{id}", UpdateAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Edit an idea (staff)", _Tag, "Body { title?, body?, audience?, showAuthor?, labels?, tags? } at any time. Audited idea.update." + _Staff)
                    .PathParam("id", "Idea identifier").Body("IdeaRequest").Returns(_Idea).Resp(400, "ValidationFailed or ContentFiltered").Resp(403, "Forbidden").Resp(404, "NotFound"));
            post.Parameter.Add(HttpMethod.PUT, _Base + "/{id}/status", StatusAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Set an idea's status", _Tag, "Body { status (key), statusNote (0 to 1000), notifyVoters (default true) }. Voters and followers get IdeaStatusChanged (one unread notification per idea, refreshed on later changes); moving to Shipped emails voters who turned Ideas email on (idea_shipped). Audited idea.status." + _Staff)
                    .PathParam("id", "Idea identifier").Body("IdeaStatusChangeRequest").Returns(_Idea).Resp(400, "ValidationFailed").Resp(403, "Forbidden").Resp(404, "NotFound"));
            post.Parameter.Add(HttpMethod.POST, _Base + "/{id}/merge", MergeAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Merge an idea into another", _Tag, "Body { intoIdeaId }. Votes move to the target (de-duplicated by member), followers follow the target, comments stay on the merged idea (read-only); the merged idea leaves the board and answers 409 IdeaMerged to votes and comments. Audited idea.merge." + _Staff)
                    .PathParam("id", "Idea identifier").Body("IdeaMergeRequest").Returns(_Idea).Resp(400, "ValidationFailed").Resp(403, "Forbidden").Resp(404, "NotFound").Resp(409, "AlreadyMerged or IdeaMerged (the target is merged)"));
            post.Parameter.Add(HttpMethod.POST, _Base + "/{id}/unmerge", UnmergeAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Unmerge an idea", _Tag, "The idea returns to the board; votes copied to the target stay there. Audited idea.merge." + _Staff)
                    .PathParam("id", "Idea identifier").Returns(_Idea).Resp(403, "Forbidden").Resp(404, "NotFound").Resp(409, "NotMerged"));
            post.Parameter.Add(HttpMethod.DELETE, _Base + "/{id}", DeleteAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Delete an idea", _Tag, "Soft delete. Audited idea.delete." + _Staff)
                    .PathParam("id", "Idea identifier").Resp(204, "Deleted").Resp(403, "Forbidden").Resp(404, "NotFound"));
            post.Parameter.Add(HttpMethod.POST, _Tickets + "/{id}/publish-idea", PublishAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Publish a ticket as an idea", _Tag, "Body { title?, body?, showRequester (default false), audience? }. Enhancement tickets only. Creates the idea (title and body default to the ticket's), sets the ticket's idea, and records the requester's vote and follow." + _Staff)
                    .PathParam("id", "Ticket identifier").Body("TicketPublishIdeaRequest", false).Returns(_Idea, 201).Resp(400, "ValidationFailed or NotAnEnhancement").Resp(403, "Forbidden").Resp(404, "NotFound").Resp(409, "TicketHasIdea"));
            post.Parameter.Add(HttpMethod.POST, _Tickets + "/{id}/link-idea", LinkAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Link a ticket to an idea", _Tag, "Body { ideaId } (a merged idea links to the idea it was merged into). Sets the ticket's idea and records the requester's vote." + _Staff)
                    .PathParam("id", "Ticket identifier").Body("TicketLinkIdeaRequest").Returns("{ ticketId, ideaId }").Resp(400, "ValidationFailed").Resp(403, "Forbidden").Resp(404, "NotFound"));
        }

        #endregion

        #region Private-Methods

        private void RegisterStatuses(WatsonWebserver.Core.Routing.RoutingGroup post)
        {
            EnumerationRoutes.Add(post, _Statuses, IdeaCollections.AdminStatuses, _Status,
                () => Doc("List idea statuses (staff)", _Tag, "Statuses in display order; filter includeRetired." + _Staff),
                (ctx, query) => RunAsync(ctx, actor => _Directory.For(ctx).Ideas.Statuses.EnumerateAsync(actor, query, true, ctx.Token)));
            post.Static.Add(HttpMethod.POST, _Statuses, CreateStatusAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Create an idea status", _Tag, "Body { key, label, color?, isClosed?, isInitial? } (an initial status clears the flag on the others; at most 50). Audited idea.status.create. SystemAdmin.")
                    .Body("IdeaStatusRequest").Returns(_Status, 201).Resp(400, "ValidationFailed").Resp(403, "Forbidden").Resp(409, "StatusKeyInUse"));
            post.Static.Add(HttpMethod.PUT, _Statuses + "/order", OrderAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Reorder idea statuses", _Tag, "Body { ideaStatusIds }: every active status exactly once. Audited idea.status.order. SystemAdmin.")
                    .Body("IdeaStatusOrderRequest").Enumeration(_Status, "SortOrderAscending", String.Empty, false).Resp(400, "ValidationFailed").Resp(403, "Forbidden"));
            post.Parameter.Add(HttpMethod.GET, _Statuses + "/{id}", GetStatusAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Read an idea status", _Tag, "One status." + _Staff)
                    .PathParam("id", "Status identifier").Returns(_Status).Resp(403, "Forbidden").Resp(404, "NotFound"));
            post.Parameter.Add(HttpMethod.PUT, _Statuses + "/{id}", UpdateStatusAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Edit an idea status", _Tag, "Body { key?, label?, color?, isClosed?, isInitial? } (omitted fields stay; renaming the key moves every idea to the new key). Audited idea.status.update. SystemAdmin.")
                    .PathParam("id", "Status identifier").Body("IdeaStatusRequest").Returns(_Status).Resp(400, "ValidationFailed").Resp(403, "Forbidden").Resp(404, "NotFound").Resp(409, "StatusKeyInUse"));
            post.Parameter.Add(HttpMethod.DELETE, _Statuses + "/{id}", RetireStatusAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Retire an idea status", _Tag, "Ideas keep the status; it is no longer offered. The initial status cannot be retired. Audited idea.status.retire. SystemAdmin.")
                    .PathParam("id", "Status identifier").Resp(204, "Retired").Resp(403, "Forbidden").Resp(404, "NotFound").Resp(409, "StatusInUse"));
        }

        private RequestInfo Info(HttpContextBase ctx)
        {
            return RequestInfo.From(ctx, _Directory.For(ctx).Settings.Rest);
        }

        private async Task<object> RunAsync<T>(HttpContextBase ctx, Func<ContentActor, Task<T>> body) where T : class
        {
            ContentActor actor = await ActorAsync(ctx).ConfigureAwait(false);
            return await body(actor).ConfigureAwait(false);
        }

        private Task<ContentActor> ActorAsync(HttpContextBase ctx)
        {
            return _Directory.For(ctx).Actors.ResolveAsync(ctx, ctx.Token);
        }

        private async Task CreateAsync(HttpContextBase ctx)
        {
            IdeaRequest request = RouteHelper.ReadJson<IdeaRequest>(ctx);
            ContentActor actor = await ActorAsync(ctx).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 201, await _Directory.For(ctx).Ideas.Ideas.AdminCreateAsync(actor, Info(ctx), request, ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task GetAsync(HttpContextBase ctx)
        {
            ContentActor actor = await ActorAsync(ctx).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 200, await _Directory.For(ctx).Ideas.Ideas.AdminGetAsync(actor, RouteHelper.Param(ctx, "id"), ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task UpdateAsync(HttpContextBase ctx)
        {
            IdeaRequest request = RouteHelper.ReadJson<IdeaRequest>(ctx, out HashSet<string> present);
            ContentActor actor = await ActorAsync(ctx).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 200, await _Directory.For(ctx).Ideas.Ideas.UpdateAsync(actor, Info(ctx), RouteHelper.Param(ctx, "id"), request, present, true, ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task StatusAsync(HttpContextBase ctx)
        {
            IdeaStatusChangeRequest request = RouteHelper.ReadJson<IdeaStatusChangeRequest>(ctx);
            ContentActor actor = await ActorAsync(ctx).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 200, await _Directory.For(ctx).Ideas.Ideas.SetStatusAsync(actor, Info(ctx), RouteHelper.Param(ctx, "id"), request, ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task MergeAsync(HttpContextBase ctx)
        {
            IdeaMergeRequest request = RouteHelper.ReadJson<IdeaMergeRequest>(ctx);
            ContentActor actor = await ActorAsync(ctx).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 200, await _Directory.For(ctx).Ideas.Ideas.MergeAsync(actor, Info(ctx), RouteHelper.Param(ctx, "id"), request, ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task UnmergeAsync(HttpContextBase ctx)
        {
            ContentActor actor = await ActorAsync(ctx).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 200, await _Directory.For(ctx).Ideas.Ideas.UnmergeAsync(actor, Info(ctx), RouteHelper.Param(ctx, "id"), ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task DeleteAsync(HttpContextBase ctx)
        {
            ContentActor actor = await ActorAsync(ctx).ConfigureAwait(false);
            await _Directory.For(ctx).Ideas.Ideas.DeleteAsync(actor, Info(ctx), RouteHelper.Param(ctx, "id"), ctx.Token).ConfigureAwait(false);
            await RouteHelper.SendEmptyAsync(ctx, 204).ConfigureAwait(false);
        }

        private async Task PublishAsync(HttpContextBase ctx)
        {
            TicketPublishIdeaRequest request = RouteHelper.ReadJson<TicketPublishIdeaRequest>(ctx);
            ContentActor actor = await ActorAsync(ctx).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 201, await _Directory.For(ctx).Ideas.Tickets.PublishAsync(actor, Info(ctx), RouteHelper.Param(ctx, "id"), request, ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task LinkAsync(HttpContextBase ctx)
        {
            TicketLinkIdeaRequest request = RouteHelper.ReadJson<TicketLinkIdeaRequest>(ctx);
            ContentActor actor = await ActorAsync(ctx).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 200, await _Directory.For(ctx).Ideas.Tickets.LinkAsync(actor, Info(ctx), RouteHelper.Param(ctx, "id"), request, ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task CreateStatusAsync(HttpContextBase ctx)
        {
            IdeaStatusRequest request = RouteHelper.ReadJson<IdeaStatusRequest>(ctx);
            ContentActor actor = await ActorAsync(ctx).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 201, await _Directory.For(ctx).Ideas.Statuses.CreateAsync(actor, Info(ctx), request, ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task OrderAsync(HttpContextBase ctx)
        {
            IdeaStatusOrderRequest request = RouteHelper.ReadJson<IdeaStatusOrderRequest>(ctx);
            ContentActor actor = await ActorAsync(ctx).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 200, await _Directory.For(ctx).Ideas.Statuses.OrderAsync(actor, Info(ctx), request, ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task GetStatusAsync(HttpContextBase ctx)
        {
            ContentActor actor = await ActorAsync(ctx).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 200, await _Directory.For(ctx).Ideas.Statuses.GetAsync(actor, RouteHelper.Param(ctx, "id"), ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task UpdateStatusAsync(HttpContextBase ctx)
        {
            IdeaStatusRequest request = RouteHelper.ReadJson<IdeaStatusRequest>(ctx, out HashSet<string> present);
            ContentActor actor = await ActorAsync(ctx).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 200, await _Directory.For(ctx).Ideas.Statuses.UpdateAsync(actor, Info(ctx), RouteHelper.Param(ctx, "id"), request, present, ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task RetireStatusAsync(HttpContextBase ctx)
        {
            ContentActor actor = await ActorAsync(ctx).ConfigureAwait(false);
            await _Directory.For(ctx).Ideas.Statuses.RetireAsync(actor, Info(ctx), RouteHelper.Param(ctx, "id"), ctx.Token).ConfigureAwait(false);
            await RouteHelper.SendEmptyAsync(ctx, 204).ConfigureAwait(false);
        }

        #endregion
    }
}
