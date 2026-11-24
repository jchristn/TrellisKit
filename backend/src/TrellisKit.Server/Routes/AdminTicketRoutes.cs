namespace TrellisKit.Server.Routes
{
    using System;
    using System.Collections.Generic;
    using System.Threading.Tasks;
    using TrellisKit.Core;
    using TrellisKit.Core.Enumeration;
    using TrellisKit.Core.Requests;
    using TrellisKit.Server.Services;
    using TrellisKit.Server.Services.Metrics;
    using WatsonWebserver;
    using WatsonWebserver.Core;
    using static TrellisKit.Server.Routes.OpenApiExtensions;

    /// <summary>
    /// Staff support routes (docs/PHASE5_CONTRACT.md section 6.2, as-built note C) for SystemAdmin and SupportAgent: the
    /// ticket inbox, ticket detail with notes, staff replies and internal notes, updates, the CSV export, support metrics,
    /// deletion of Closed tickets, and (SystemAdmin only) ticket type administration.
    /// </summary>
    public class AdminTicketRoutes
    {
        #region Private-Members

        private const string _Base = Constants.ApiPrefix + "/admin/tickets";
        private const string _Types = Constants.ApiPrefix + "/admin/support/ticket-types";
        private const string _Tag = "Admin support";
        private const string _Staff = " SystemAdmin or SupportAgent.";
        private const string _Admin = " SystemAdmin.";
        private const string _CsvPaging = "Bounded: accepts the inbox enumeration query and returns at most enumeration.maxResultsAdmin rows (the default page size here). When more rows remain the X-Continuation-Token response header carries the token for the next call (send it as continuationToken); X-Total-Records has the total.";
        private readonly PropertyDirectory _Directory;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the routes.
        /// </summary>
        /// <param name="directory">Property directory (resolves each request's property services).</param>
        /// <exception cref="ArgumentNullException">The directory is null.</exception>
        public AdminTicketRoutes(PropertyDirectory directory)
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

            EnumerationRoutes.Add(post, _Base, SupportCollections.Inbox, "AdminTicketSummary (Ticket without messages, with the membership snapshot)",
                () => Doc("List tickets (inbox)", _Tag, "Orderings LastActivityDescending (default), CreatedAscending, CreatedDescending, DueAscending, PriorityDescending. Filters status (one or a list; default every status except Closed), type, priority, assigneeUserId, unassigned, requesterUserId, overdue, hasIdea. search covers the number, title, body, and the requester's display name and email (PostgreSQL text matching on this property's tickets; tickets are never in global search). Exact totals." + _Staff),
                (ctx, query) => RunAsync(ctx, actor => _Directory.For(ctx).Support.Staff.InboxAsync(actor, query, ctx.Token)));
            post.Static.Add(HttpMethod.GET, _Base + "/export.csv", ExportAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Export tickets as CSV", _Tag, _CsvPaging + " Ticket metadata only (never bodies or messages)." + _Staff)
                    .EnumerationQueryParams(SupportCollections.Inbox.OrderingNames(), "status, type, priority, assigneeUserId, unassigned, requesterUserId, overdue, hasIdea")
                    .File("text/csv", "One CSV page with a header row; X-Continuation-Token names the next page").Resp(400, "ValidationFailed or InvalidContinuationToken"));
            post.Parameter.Add(HttpMethod.GET, _Base + "/{id}", GetAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Read a ticket (staff)", _Tag, "Every message kind in the newest 20 (Reply, Note, System), the membership snapshot at creation, and currentMembership." + _Staff)
                    .PathParam("id", "Ticket identifier").Bounded("Ticket", "messages preview of at most 20 with totalRecords and the enumerate path").Resp(404, "NotFound or NotAvailable"));
            post.Parameter.Add(HttpMethod.PUT, _Base + "/{id}", UpdateAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Update a ticket", _Tag, "Body { status, priority, type, assigneeUserId, labels, tags }; omitted fields stay, assigneeUserId null unassigns, the assignee must be a SupportAgent or SystemAdmin. Each change writes a staff timeline line and is audited as ticket.update; the requester hears about status changes only (TicketUpdate, ticket_resolved for Resolved and Closed, ticket.updated)." + _Staff)
                    .PathParam("id", "Ticket identifier").Body("TicketUpdateRequest").Returns("Ticket").Resp(400, "ValidationFailed").Resp(404, "NotFound").Resp(409, "Conflict"));
            post.Parameter.Add(HttpMethod.DELETE, _Base + "/{id}", DeleteAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Delete a ticket", _Tag, "Only Closed tickets. Deletes the ticket, its messages, their attachments, and the labels and tags. Audited as ticket.delete." + _Staff)
                    .PathParam("id", "Ticket identifier").Resp(204, "Deleted").Resp(404, "NotFound").Resp(409, "TicketNotClosed"));
            EnumerationRoutes.Add(post, _Base + "/{id}/messages", SupportCollections.Messages, "TicketMessage { id, ticketId, kind, author, authorRole, authorName, body, statusChange, attachments, labels, tags, createdUtc }",
                () => Doc("List a ticket's messages (staff)", _Tag, "Replies, internal notes, and system timeline lines (CreatedAscending default); filter kind." + _Staff),
                (ctx, query) => RunAsync(ctx, actor => _Directory.For(ctx).Support.Staff.MessagesAsync(actor, RouteHelper.Param(ctx, "id"), query, ctx.Token)), "id");
            post.Parameter.Add(HttpMethod.POST, _Base + "/{id}/messages", PostMessageAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Reply or add a note", _Tag, "Body { kind, body, mediaIds, setStatus }: kind Reply (default) is visible to the requester and sets WaitingOnMember unless setStatus says otherwise (notifies and emails ticket_reply, publishes ticket.updated); kind Note is staff only, changes nothing, and is never returned to members or emailed." + _Staff)
                    .PathParam("id", "Ticket identifier").Body("StaffTicketMessageRequest").Returns("TicketMessage", 201).Resp(400, "ValidationFailed").Resp(404, "NotFound").Resp(409, "MediaNotAttachable or Conflict"));
            post.Static.Add(HttpMethod.GET, Constants.ApiPrefix + "/admin/metrics/support", MetricsAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Support metrics", _Tag, "Query days (1 to 365, default 30). openedByDay and resolvedByDay (one entry per day), firstResponseMedianHours, overdueNow, openNow, and byType (at most 20 with totalRecords and the enumerate path)." + _Staff)
                    .QueryParam("days", "1 to 365, default 30").Enumeration("LegacyMetricRow", "CreatedDescending, CreatedAscending", "days, series", true).Resp(400, "ValidationFailed"));
            post.Static.Add(HttpMethod.POST, Constants.ApiPrefix + "/admin/metrics/support/enumerate", MetricsAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Support metrics", _Tag, "Query days (1 to 365, default 30). openedByDay and resolvedByDay (one entry per day), firstResponseMedianHours, overdueNow, openNow, and byType (at most 20 with totalRecords and the enumerate path)." + _Staff)
                    .QueryParam("days", "1 to 365, default 30").Body("EnumerationQuery", false).Enumeration("LegacyMetricRow", "CreatedDescending, CreatedAscending", "days, series", true).Resp(400, "ValidationFailed"));

            EnumerationRoutes.Add(post, _Types, SupportCollections.AdminTicketTypes, "TicketType { id, key, label, description, helpText, staffRecipients, sortOrder, isRetired, labels, tags }",
                () => Doc("List ticket types (admin)", _Tag, "Every ticket type in order, with staff recipients. filters.includeRetired (default true)." + _Admin),
                (ctx, query) => RunAsync(ctx, actor => _Directory.For(ctx).Support.Types.ListAdminAsync(actor, query, ctx.Token)));
            post.Static.Add(HttpMethod.POST, _Types, CreateTypeAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Create a ticket type", _Tag, "Body { key, label, description, helpText, staffRecipients, isRetired, labels, tags }: key 1 to 64 letters, digits, underscores, or hyphens (unique), label 1 to 80, at most 10 staff recipients (staff_ticket_opened). At most 50 types. Audited as ticket_type.create." + _Admin)
                    .Body("TicketTypeRequest").Returns("TicketType", 201).Resp(400, "ValidationFailed").Resp(403, "Forbidden").Resp(409, "KeyInUse or TicketTypeLimitReached"));
            post.Static.Add(HttpMethod.PUT, _Types + "/order", OrderTypesAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Reorder ticket types", _Tag, "Body { ticketTypeIds }: the listed types first in that order, the others after them. Audited as ticket_type.order." + _Admin)
                    .Body("TicketTypeOrderRequest").Returns("TicketTypeOrderResponse").Resp(400, "ValidationFailed").Resp(403, "Forbidden"));
            post.Parameter.Add(HttpMethod.GET, _Types + "/{id}", GetTypeAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Read a ticket type", _Tag, "With staff recipients." + _Admin)
                    .PathParam("id", "Ticket type identifier").Returns("TicketType").Resp(403, "Forbidden").Resp(404, "NotFound"));
            post.Parameter.Add(HttpMethod.PUT, _Types + "/{id}", UpdateTypeAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Edit a ticket type", _Tag, "Any create field; omitted fields stay; isRetired false restores a retired type. Audited as ticket_type.update." + _Admin)
                    .PathParam("id", "Ticket type identifier").Body("TicketTypeRequest").Returns("TicketType").Resp(400, "ValidationFailed").Resp(403, "Forbidden").Resp(404, "NotFound").Resp(409, "KeyInUse"));
            post.Parameter.Add(HttpMethod.DELETE, _Types + "/{id}", RetireTypeAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Retire a ticket type", _Tag, "Hides the type from the new ticket form; existing tickets keep it. Idempotent. Audited as ticket_type.retire." + _Admin)
                    .PathParam("id", "Ticket type identifier").Resp(204, "Retired").Resp(403, "Forbidden").Resp(404, "NotFound"));
        }

        #endregion

        #region Private-Methods

        private RequestInfo Info(HttpContextBase ctx)
        {
            return RequestInfo.From(ctx, _Directory.For(ctx).Settings.Rest);
        }

        private async Task<object> RunAsync<T>(HttpContextBase ctx, Func<ContentActor, Task<T>> body) where T : class
        {
            ContentActor actor = await _Directory.For(ctx).Actors.ResolveAsync(ctx, ctx.Token).ConfigureAwait(false);
            return await body(actor).ConfigureAwait(false);
        }

        private async Task<ContentActor> ActorAsync(HttpContextBase ctx)
        {
            ContentActor actor = await _Directory.For(ctx).Actors.ResolveAsync(ctx, ctx.Token).ConfigureAwait(false);
            return actor;
        }

        private async Task ExportAsync(HttpContextBase ctx)
        {
            EnumerationQuery query = RouteHelper.ReadEnumerationQuery<EnumerationQuery>(ctx);
            ContentActor actor = await ActorAsync(ctx).ConfigureAwait(false);
            await RouteHelper.SendCsvPageAsync(ctx, "tickets.csv", await _Directory.For(ctx).Support.Staff.ExportCsvAsync(actor, query, ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task GetAsync(HttpContextBase ctx)
        {
            ContentActor actor = await ActorAsync(ctx).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 200, await _Directory.For(ctx).Support.Staff.GetAsync(actor, RouteHelper.Param(ctx, "id"), ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task UpdateAsync(HttpContextBase ctx)
        {
            TicketUpdateRequest request = RouteHelper.ReadJson<TicketUpdateRequest>(ctx, out HashSet<string> present);
            ContentActor actor = await ActorAsync(ctx).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 200, await _Directory.For(ctx).Support.Staff.UpdateAsync(actor, Info(ctx), RouteHelper.Param(ctx, "id"), request, present, ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task DeleteAsync(HttpContextBase ctx)
        {
            ContentActor actor = await ActorAsync(ctx).ConfigureAwait(false);
            await _Directory.For(ctx).Support.Staff.DeleteAsync(actor, Info(ctx), RouteHelper.Param(ctx, "id"), ctx.Token).ConfigureAwait(false);
            await RouteHelper.SendEmptyAsync(ctx, 204).ConfigureAwait(false);
        }

        private async Task PostMessageAsync(HttpContextBase ctx)
        {
            StaffTicketMessageRequest request = RouteHelper.ReadJson<StaffTicketMessageRequest>(ctx);
            ContentActor actor = await ActorAsync(ctx).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 201, await _Directory.For(ctx).Support.Staff.PostAsync(actor, Info(ctx), RouteHelper.Param(ctx, "id"), request, ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task MetricsAsync(HttpContextBase ctx)
        {
            EnumerationQuery query=ctx.Request.Method==HttpMethod.GET?RouteHelper.ReadEnumerationQuery<EnumerationQuery>(ctx):RouteHelper.ReadEnumerationBody<EnumerationQuery>(ctx);
            int days=AggregateEnumeration.Days(query,1);
            ContentActor actor = await ActorAsync(ctx).ConfigureAwait(false);
            ApplicationServices services=_Directory.For(ctx);
            TrellisKit.Core.Responses.SupportMetrics summary=await services.Support.Staff.MetricsAsync(actor, days, ctx.Token).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx,200,AggregateEnumeration.Daily(services,"support-daily",query,summary)).ConfigureAwait(false);
        }

        private async Task CreateTypeAsync(HttpContextBase ctx)
        {
            TicketTypeRequest request = RouteHelper.ReadJson<TicketTypeRequest>(ctx);
            ContentActor actor = await ActorAsync(ctx).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 201, await _Directory.For(ctx).Support.Types.CreateAsync(actor, Info(ctx), request, ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task OrderTypesAsync(HttpContextBase ctx)
        {
            TicketTypeOrderRequest request = RouteHelper.ReadJson<TicketTypeOrderRequest>(ctx);
            ContentActor actor = await ActorAsync(ctx).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 200, await _Directory.For(ctx).Support.Types.OrderAsync(actor, Info(ctx), request, ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task GetTypeAsync(HttpContextBase ctx)
        {
            ContentActor actor = await ActorAsync(ctx).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 200, await _Directory.For(ctx).Support.Types.GetAsync(actor, RouteHelper.Param(ctx, "id"), ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task UpdateTypeAsync(HttpContextBase ctx)
        {
            TicketTypeRequest request = RouteHelper.ReadJson<TicketTypeRequest>(ctx, out HashSet<string> present);
            ContentActor actor = await ActorAsync(ctx).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 200, await _Directory.For(ctx).Support.Types.UpdateAsync(actor, Info(ctx), RouteHelper.Param(ctx, "id"), request, present, ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task RetireTypeAsync(HttpContextBase ctx)
        {
            ContentActor actor = await ActorAsync(ctx).ConfigureAwait(false);
            await _Directory.For(ctx).Support.Types.RetireAsync(actor, Info(ctx), RouteHelper.Param(ctx, "id"), ctx.Token).ConfigureAwait(false);
            await RouteHelper.SendEmptyAsync(ctx, 204).ConfigureAwait(false);
        }

        #endregion
    }
}
