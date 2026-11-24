namespace TrellisKit.Server.Routes
{
    using System;
    using System.Collections.Generic;
    using System.Threading.Tasks;
    using TrellisKit.Core;
    using TrellisKit.Core.Enumeration;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Exceptions;
    using TrellisKit.Core.Requests;
    using TrellisKit.Server.Services;
    using TrellisKit.Server.Services.Metrics;
    using WatsonWebserver;
    using WatsonWebserver.Core;
    using static TrellisKit.Server.Routes.OpenApiExtensions;

    /// <summary>
    /// Staff event routes (docs/PHASE4_CONTRACT.md section 4.7; proposals from resolved decision 10; scopes from
    /// resolved decision 11). The route group admits member sessions; the event service checks event staff (SystemAdmin
    /// or EventManager) or, where noted, a host of the event. Category management is SystemAdmin only.
    /// </summary>
    public class AdminEventRoutes
    {
        #region Private-Members

        private const string _Tag = "Admin events";
        private const string _Base = Constants.ApiPrefix + "/admin/events";
        private const string _AdminEvent = "AdminEvent";
        private const string _Fields = "title, slug, summary, description, category, format, startLocal, endLocal, timeZone, isAllDay, location { venueId, name, address { line1, line2, locality, region, postalCode, countryCode }, addressVisibility, geo { lat, lng } }, " +
            "online { platformLabel, joinUrl }, capacity, maxGuestsPerRsvp, waitlistEnabled, rsvpAudience, rsvpOpensUtc, rsvpClosesUtc, attendeeVisibility, externalUrl, coverMediaId, hostUserIds, instructorIds, reminderOffsetsMinutes, commentsEnabled, audience, labels, tags";
        private const string _Recurrence = "recurrence { frequency: Daily | Weekly | Monthly | Yearly, interval (1 to 99), weekdays (Mon to Sun), monthDay (1 to 31 or -1), setPosition (1 to 4 or -1), count, untilDate (yyyy-MM-dd) }";
        private readonly PropertyDirectory _Directory;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="directory">Property directory.</param>
        /// <exception cref="ArgumentNullException">The directory is null.</exception>
        public AdminEventRoutes(PropertyDirectory directory)
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
            RegisterCategories(post);
            RegisterEvents(post);
            RegisterRsvps(post);
            post.Static.Add(HttpMethod.GET, Constants.ApiPrefix + "/admin/metrics/events", MetricsAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Event metrics", _Tag, "Enumerate daily event and RSVP observations. Filters days (1 to 365, default 30), series. Scalar rates and a bounded category preview accompany the page. Event staff.")
                    .Enumeration("LegacyMetricRow", "CreatedDescending, CreatedAscending", "days, series").Resp(403, "Forbidden"));
            post.Static.Add(HttpMethod.POST, Constants.ApiPrefix + "/admin/metrics/events/enumerate", MetricsAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Enumerate event metrics", _Tag, "The same daily observation collection as GET.")
                    .Body("EnumerationQuery", false).Enumeration("LegacyMetricRow", "CreatedDescending, CreatedAscending", "days, series", false).Resp(403, "Forbidden"));
        }

        #endregion

        #region Private-Methods

        private void RegisterCategories(WatsonWebserver.Core.Routing.RoutingGroup post)
        {
            EnumerationRoutes.Add(post, _Base + "/categories", EventCollections.AdminCategories, "EventCategory",
                () => Doc("List event categories (admin)", _Tag, "Every category including retired ones (filter includeRetired, default true). Event staff."),
                (ctx, query) => RunAsync(ctx, actor => _Directory.For(ctx).Events.Categories.AdminListAsync(actor, query, ctx.Token)));
            post.Static.Add(HttpMethod.POST, _Base + "/categories", CreateCategoryAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Create an event category", _Tag, "Body { key, label, color, labels, tags }. Keys are 1 to 40 letters, digits, and hyphens and never change. At most events.maxCategories. SystemAdmin. Audited as event.category.create.")
                    .Body("EventCategoryRequest").Returns("EventCategory", 201).Resp(400, "ValidationFailed").Resp(403, "Forbidden").Resp(409, "KeyInUse or CategoryLimitReached"));
            post.Static.Add(HttpMethod.PUT, _Base + "/categories/order", OrderCategoriesAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Order event categories", _Tag, "Body { categoryIds } (every category id once). SystemAdmin.")
                    .Body("EventCategoryOrderRequest").Returns("{ updated }").Resp(400, "ValidationFailed").Resp(403, "Forbidden"));
            post.Parameter.Add(HttpMethod.PUT, _Base + "/categories/{id}", UpdateCategoryAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Edit an event category", _Tag, "Body { label, color, labels, tags } (omitted fields stay; an edit restores a retired category). SystemAdmin.")
                    .PathParam("id", "Category identifier").Body("EventCategoryRequest").Returns("EventCategory").Resp(400, "ValidationFailed").Resp(403, "Forbidden").Resp(404, "NotFound"));
            post.Parameter.Add(HttpMethod.DELETE, _Base + "/categories/{id}", DeleteCategoryAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Delete an event category", _Tag, "A category in use is retired (it stays on its events but cannot be chosen); an unused one is deleted. SystemAdmin.")
                    .PathParam("id", "Category identifier").Returns("{ retired }").Resp(403, "Forbidden").Resp(404, "NotFound"));
        }

        private void RegisterEvents(WatsonWebserver.Core.Routing.RoutingGroup post)
        {
            EnumerationRoutes.Add(post, _Base, EventCollections.AdminEvents, _AdminEvent,
                () => Doc("List events (admin)", _Tag, "Every event in Draft, Published, or Canceled (filter status for one), when All by default, with the member filters. Items are AdminEvent (Event plus openReportCount, checkedInCount, guestCount, reminderStatus, onlineJoinUrl, venueId, hostUserIds, instructorIds, proposedBy, proposalNote, reviewNote, reviewedUtc, moderationState). Event staff."),
                (ctx, query) => RunAsync(ctx, actor => _Directory.For(ctx).Events.Catalog.AdminListAsync(actor, query, ctx.Token)));
            EnumerationRoutes.Add(post, _Base + "/proposals", EventCollections.AdminProposals, _AdminEvent,
                () => Doc("List event proposals (review queue)", _Tag, "Member proposals, oldest first. Filter status (default Proposed), proposedByUserId. Event staff."),
                (ctx, query) => RunAsync(ctx, actor => _Directory.For(ctx).Events.Proposals.QueueAsync(actor, query, ctx.Token)));
            post.Static.Add(HttpMethod.POST, _Base, CreateAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Create an event", _Tag, "Body { " + _Fields + ", recurrence }. Local wall times in the IANA timeZone (default the venue's): a time in a spring-forward gap is 400 InvalidSchedule with a hint; an ambiguous fall-back time takes the first occurrence. " +
                    "InPerson and Hybrid need a location (a directory venue's address is copied), Online and Hybrid an https joinUrl. With " + _Recurrence + " (exactly one of count and untilDate, or neither for until the horizon), occurrences are materialized as events up to events.recurrenceHorizonDays ahead (at most events.maxOccurrences); the first occurrence is startLocal and must fall on the rule. " +
                    "Created as a Draft. Event staff. Audited as event.create.")
                    .Body("EventRequest").Returns(_AdminEvent, 201).Resp(400, "ValidationFailed, InvalidSchedule, InvalidGeo, UnknownOption, or ContentFiltered").Resp(403, "Forbidden").Resp(409, "SlugInUse or MediaNotAttachable"));
            post.Parameter.Add(HttpMethod.GET, _Base + "/{id}", GetAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Read an event (admin)", _Tag, "AdminEvent. Event staff, or a host of the event.")
                    .PathParam("id", "Event identifier").Returns(_AdminEvent).Resp(403, "Forbidden").Resp(404, "NotFound"));
            post.Parameter.Add(HttpMethod.PUT, _Base + "/{id}", UpdateAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Edit an event", _Tag, "Body { " + _Fields + ", notifyAttendees, recurrence, scope } (omitted fields stay). Hosts may send only description, coverMediaId, labels, and tags (else 403). Time, location, or format changes send EventUpdated to Going, Waitlisted, and Interested members unless notifyAttendees is false, increment the ICS SEQUENCE, and reschedule reminders not yet due. " +
                    "On an occurrence: scope One (default; it becomes an exception), Following (the rule ends before this occurrence and a new recurrence starts here with the edits and an optional new rule), or All (every not-yet-started occurrence that is not an exception; a time or rule change regenerates occurrences, matched by local date so RSVPs stay, removed dates are canceled with EventCanceled; one aggregated EventUpdated per member). " +
                    "A canceled event takes only labels, tags, summary, and description. Audited as event.update.")
                    .PathParam("id", "Event identifier").Body("EventRequest").Returns(_AdminEvent).Resp(400, "ValidationFailed, InvalidSchedule, InvalidGeo, UnknownOption, or ContentFiltered").Resp(403, "Forbidden").Resp(404, "NotFound").Resp(409, "EventCanceled, SlugInUse, or MediaNotAttachable"));
            post.Parameter.Add(HttpMethod.DELETE, _Base + "/{id}", DeleteAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Delete a draft event", _Tag, "Drafts only (published events are canceled, never deleted). A draft occurrence is deleted on its own (never materialized again); scope=All deletes the whole draft series. Event staff. Audited as event.delete.")
                    .PathParam("id", "Event identifier").QueryParam("scope", "One (default) or All").Resp(204, "Deleted").Resp(403, "Forbidden").Resp(404, "NotFound").Resp(409, "NotDraft"));
            post.Parameter.Add(HttpMethod.POST, _Base + "/{id}/publish", PublishAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Publish an event", _Tag, "Body { notifyMembers } (default false: true runs a NewEvent fan-out to every member who may read it and has Events notifications on). A recurring event publishes its series (later occurrences are created published). Mentions in the description notify. Idempotent. Event staff. Audited as event.publish.")
                    .PathParam("id", "Event identifier").Body("EventPublishRequest", false).Returns(_AdminEvent).Resp(403, "Forbidden").Resp(404, "NotFound").Resp(409, "EventCanceled or NotDraft"));
            post.Parameter.Add(HttpMethod.POST, _Base + "/{id}/cancel", CancelAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Cancel an event", _Tag, "Body { reason (3 to 500), notifyAttendees (default true), scope }. Status Canceled, reminders stopped, RSVPs kept; EventCanceled in-app, push, and the Essential email event_canceled to RSVPed members. On an occurrence: scope One (default), Following (the rule ends before it), or All (every not-yet-started occurrence; one notice per member). Event staff. Audited as event.cancel.")
                    .PathParam("id", "Event identifier").Body("EventCancelRequest").Returns(_AdminEvent).Resp(400, "ValidationFailed").Resp(403, "Forbidden").Resp(404, "NotFound").Resp(409, "EventCanceled or NotDraft"));
            post.Parameter.Add(HttpMethod.POST, _Base + "/{id}/duplicate", DuplicateAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Duplicate an event", _Tag, "Body { startLocal, timeZone }. A Draft copy of every field (labels and tags included) without RSVPs, comments, reactions, or recurrence, keeping the duration. Event staff. Audited as event.duplicate.")
                    .PathParam("id", "Event identifier").Body("EventDuplicateRequest").Returns(_AdminEvent, 201).Resp(400, "InvalidSchedule").Resp(403, "Forbidden").Resp(404, "NotFound"));
            post.Parameter.Add(HttpMethod.POST, _Base + "/{id}/approve", ApproveAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Approve an event proposal", _Tag, "Body { reviewNote, publish, notifyMembers } (publish default true; notifyMembers default false). The proposer becomes the host and gets EventProposalDecision and the event_proposal_decision email. Event staff. Audited as event.approve.")
                    .PathParam("id", "Event identifier").Body("EventReviewRequest", false).Returns(_AdminEvent).Resp(400, "ValidationFailed").Resp(403, "Forbidden").Resp(404, "NotFound").Resp(409, "NotPending"));
            post.Parameter.Add(HttpMethod.POST, _Base + "/{id}/reject", RejectAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Reject an event proposal", _Tag, "Body { reviewNote } (3 to 1000, shown to the proposer). Status Rejected; the proposer is told. Event staff. Audited as event.reject.")
                    .PathParam("id", "Event identifier").Body("EventReviewRequest").Returns(_AdminEvent).Resp(400, "ValidationFailed").Resp(403, "Forbidden").Resp(404, "NotFound").Resp(409, "NotPending"));
        }

        private void RegisterRsvps(WatsonWebserver.Core.Routing.RoutingGroup post)
        {
            EnumerationRoutes.Add(post, _Base + "/{id}/rsvps", EventCollections.AdminRsvps, "AdminRsvp",
                () => Doc("List an event's RSVPs", _Tag, "Every RSVP (members who hide from attendee lists included). Filters status (Going, Interested, Waitlisted), checkedIn. Orderings CreatedAscending (default), CreatedDescending, DisplayNameAscending, WaitlistAscending, UpdatedDescending. Event staff, or a host of the event."),
                (ctx, query) => RunAsync(ctx, actor => _Directory.For(ctx).Events.Catalog.AdminRsvpsAsync(actor, RouteHelper.Param(ctx, "id"), query, ctx.Token)), "id");
            post.Parameter.Add(HttpMethod.GET, _Base + "/{id}/rsvps/export.csv", ExportAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Export an event's RSVPs", _Tag, "CSV (displayName, handle, status, guests, position, checkedInUtc, createdUtc), up to 1000 rows per page; X-Continuation-Token continues (query continuationToken). Event staff, or a host of the event.")
                    .PathParam("id", "Event identifier").QueryParam("continuationToken", "Next page").File("text/csv", "CSV").Resp(403, "Forbidden").Resp(404, "NotFound"));
            AddAction(post, "promote", "Promote an RSVP to Going", "Event staff move the RSVP to Going even beyond capacity; the member gets EventWaitlistPromoted unless notifyMember is false. Audited as event.rsvp.promote.");
            AddAction(post, "move-to-waitlist", "Move an RSVP to the waitlist", "Event staff move the RSVP to the end of the waitlist (freed space promotes others). Audited.");
            AddAction(post, "remove", "Remove an RSVP", "Event staff delete the RSVP; the waitlist is promoted. Audited as event.rsvp.remove.");
            AddAction(post, "check-in", "Check an attendee in", "Event staff and hosts; Going RSVPs only. Idempotent. Audited as event.checkIn.");
            AddAction(post, "undo-check-in", "Undo a check-in", "Event staff and hosts. Idempotent. Audited.");
        }

        private void AddAction(WatsonWebserver.Core.Routing.RoutingGroup post, string action, string summary, string description)
        {
            post.Parameter.Add(HttpMethod.POST, _Base + "/{id}/rsvps/{rsvpId}/" + action, async ctx =>
            {
                EventRsvpActionRequest request = RouteHelper.ReadJson<EventRsvpActionRequest>(ctx);
                ContentActor actor = await ActorAsync(ctx).ConfigureAwait(false);
                await RouteHelper.SendJsonAsync(ctx, 200, await _Directory.For(ctx).Events.Rsvps.StaffActionAsync(actor, Info(ctx), RouteHelper.Param(ctx, "id"), RouteHelper.Param(ctx, "rsvpId"), action, request, ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
            }, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc(summary, _Tag, "Body { reason, notifyMember } (both optional). " + description)
                    .PathParam("id", "Event identifier").PathParam("rsvpId", "RSVP identifier").Body("EventRsvpActionRequest", false).Returns("AdminRsvp").Resp(403, "Forbidden").Resp(404, "NotFound").Resp(409, "NotGoing"));
        }

        private async Task<object> RunAsync<T>(HttpContextBase ctx, Func<ContentActor, Task<T>> body) where T : class
        {
            ContentActor actor = await _Directory.For(ctx).Actors.ResolveAsync(ctx, ctx.Token).ConfigureAwait(false);
            return await body(actor).ConfigureAwait(false);
        }

        private Task<ContentActor> ActorAsync(HttpContextBase ctx)
        {
            return _Directory.For(ctx).Actors.ResolveAsync(ctx, ctx.Token);
        }

        private RequestInfo Info(HttpContextBase ctx)
        {
            return RequestInfo.From(ctx, _Directory.For(ctx).Settings.Rest);
        }

        private async Task CreateAsync(HttpContextBase ctx)
        {
            EventRequest request = RouteHelper.ReadJson<EventRequest>(ctx);
            ContentActor actor = await ActorAsync(ctx).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 201, await _Directory.For(ctx).Events.Catalog.CreateAsync(actor, Info(ctx), request, ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task GetAsync(HttpContextBase ctx)
        {
            ContentActor actor = await ActorAsync(ctx).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 200, await _Directory.For(ctx).Events.Catalog.AdminGetAsync(actor, RouteHelper.Param(ctx, "id"), ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task UpdateAsync(HttpContextBase ctx)
        {
            EventRequest request = RouteHelper.ReadJson<EventRequest>(ctx, out HashSet<string> present);
            ContentActor actor = await ActorAsync(ctx).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 200, await _Directory.For(ctx).Events.Catalog.UpdateAsync(actor, Info(ctx), RouteHelper.Param(ctx, "id"), request, present, ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task DeleteAsync(HttpContextBase ctx)
        {
            ContentActor actor = await ActorAsync(ctx).ConfigureAwait(false);
            Dictionary<string, string> errors = new Dictionary<string, string>();
            EventEditScopeEnum scope = RouteHelper.QueryEnum<EventEditScopeEnum>(ctx, "scope", errors) ?? EventEditScopeEnum.One;
            RouteHelper.ThrowIfErrors(errors);
            await _Directory.For(ctx).Events.Catalog.DeleteAsync(actor, Info(ctx), RouteHelper.Param(ctx, "id"), scope, ctx.Token).ConfigureAwait(false);
            await RouteHelper.SendEmptyAsync(ctx, 204).ConfigureAwait(false);
        }

        private async Task PublishAsync(HttpContextBase ctx)
        {
            EventPublishRequest request = RouteHelper.ReadJson<EventPublishRequest>(ctx);
            ContentActor actor = await ActorAsync(ctx).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 200, await _Directory.For(ctx).Events.Catalog.PublishAsync(actor, Info(ctx), RouteHelper.Param(ctx, "id"), request, ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task CancelAsync(HttpContextBase ctx)
        {
            EventCancelRequest request = RouteHelper.ReadJson<EventCancelRequest>(ctx);
            ContentActor actor = await ActorAsync(ctx).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 200, await _Directory.For(ctx).Events.Catalog.CancelAsync(actor, Info(ctx), RouteHelper.Param(ctx, "id"), request, ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task DuplicateAsync(HttpContextBase ctx)
        {
            EventDuplicateRequest request = RouteHelper.ReadJson<EventDuplicateRequest>(ctx);
            ContentActor actor = await ActorAsync(ctx).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 201, await _Directory.For(ctx).Events.Catalog.DuplicateAsync(actor, Info(ctx), RouteHelper.Param(ctx, "id"), request, ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task ApproveAsync(HttpContextBase ctx)
        {
            EventReviewRequest request = RouteHelper.ReadJson<EventReviewRequest>(ctx);
            ContentActor actor = await ActorAsync(ctx).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 200, await _Directory.For(ctx).Events.Proposals.ApproveAsync(actor, Info(ctx), RouteHelper.Param(ctx, "id"), request, ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task RejectAsync(HttpContextBase ctx)
        {
            EventReviewRequest request = RouteHelper.ReadJson<EventReviewRequest>(ctx);
            ContentActor actor = await ActorAsync(ctx).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 200, await _Directory.For(ctx).Events.Proposals.RejectAsync(actor, Info(ctx), RouteHelper.Param(ctx, "id"), request, ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task ExportAsync(HttpContextBase ctx)
        {
            ContentActor actor = await ActorAsync(ctx).ConfigureAwait(false);
            TrellisKit.Core.Responses.CsvPage page = await _Directory.For(ctx).Events.Catalog.ExportCsvAsync(actor, RouteHelper.Param(ctx, "id"), RouteHelper.Query(ctx, "continuationToken"), ctx.Token).ConfigureAwait(false);
            await RouteHelper.SendCsvPageAsync(ctx, "rsvps-" + RouteHelper.Param(ctx, "id") + ".csv", page).ConfigureAwait(false);
        }

        private async Task MetricsAsync(HttpContextBase ctx)
        {
            ContentActor actor = await ActorAsync(ctx).ConfigureAwait(false);
            EnumerationQuery query = ctx.Request.Method == HttpMethod.GET ? RouteHelper.ReadEnumerationQuery<EnumerationQuery>(ctx) : RouteHelper.ReadEnumerationBody<EnumerationQuery>(ctx);
            int days = AggregateEnumeration.Days(query, 1);
            ApplicationServices services = _Directory.For(ctx);
            TrellisKit.Core.Responses.EventMetricsView summary = await services.Events.Catalog.MetricsAsync(actor, days, ctx.Token).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 200, AggregateEnumeration.Daily(services, "events-daily", query, summary)).ConfigureAwait(false);
        }

        private async Task CreateCategoryAsync(HttpContextBase ctx)
        {
            EventCategoryRequest request = RouteHelper.ReadJson<EventCategoryRequest>(ctx);
            ContentActor actor = await ActorAsync(ctx).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 201, await _Directory.For(ctx).Events.Categories.CreateAsync(actor, Info(ctx), request, ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task UpdateCategoryAsync(HttpContextBase ctx)
        {
            EventCategoryRequest request = RouteHelper.ReadJson<EventCategoryRequest>(ctx, out HashSet<string> present);
            ContentActor actor = await ActorAsync(ctx).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 200, await _Directory.For(ctx).Events.Categories.UpdateAsync(actor, Info(ctx), RouteHelper.Param(ctx, "id"), request, present, ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task DeleteCategoryAsync(HttpContextBase ctx)
        {
            ContentActor actor = await ActorAsync(ctx).ConfigureAwait(false);
            bool retired = await _Directory.For(ctx).Events.Categories.DeleteAsync(actor, Info(ctx), RouteHelper.Param(ctx, "id"), ctx.Token).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 200, new Dictionary<string, bool> { { "retired", retired } }).ConfigureAwait(false);
        }

        private async Task OrderCategoriesAsync(HttpContextBase ctx)
        {
            EventCategoryOrderRequest request = RouteHelper.ReadJson<EventCategoryOrderRequest>(ctx);
            ContentActor actor = await ActorAsync(ctx).ConfigureAwait(false);
            int updated = await _Directory.For(ctx).Events.Categories.OrderAsync(actor, Info(ctx), request, ctx.Token).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 200, new Dictionary<string, int> { { "updated", updated } }).ConfigureAwait(false);
        }

        #endregion
    }
}
