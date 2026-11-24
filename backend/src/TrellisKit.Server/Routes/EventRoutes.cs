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
    /// Routes of events (occurrences and recurrence, RSVPs, reminders, proposals, calendar feeds) (docs/PHASE4_CONTRACT.md section 4).
    /// Member routes are declared object gated in AccessRoutes; the event service decides per event (locked stubs,
    /// rsvpAudience). Registers the admin and public route classes too.
    /// </summary>
    public class EventRoutes
    {
        #region Private-Members

        private const string _Tag = "Events";
        private const string _Events = Constants.ApiPrefix + "/events";
        private const string _Summary = "EventSummary";
        private const string _Event = "Event";
        private const string _RecurrenceText = "Occurrences of a recurring event carry recurrence { id, rule { frequency, interval, weekdays, monthDay, setPosition, count, untilDate }, summary, occurrenceIndex, isException, originalStartUtc }.";
        private readonly PropertyDirectory _Directory;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="directory">Property directory.</param>
        /// <exception cref="ArgumentNullException">The directory is null.</exception>
        public EventRoutes(PropertyDirectory directory)
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
            RegisterEvents(post);
            RegisterMe(post);
            TargetConversationRoutes.Register(post, _Directory, _Events, TrellisKit.Core.Enums.TargetTypeEnum.Event, _Tag, "an event");
            TargetPromotionRoutes.Register(post, _Directory, _Events, TrellisKit.Core.Enums.TargetTypeEnum.Event, _Tag);
            new AdminEventRoutes(_Directory).Register(server);
            new PublicEventRoutes(_Directory).Register(server);
        }

        #endregion

        #region Private-Methods

        private void RegisterEvents(WatsonWebserver.Core.Routing.RoutingGroup post)
        {
            EnumerationRoutes.Add(post, _Events, EventCollections.Events, _Summary,
                () => Doc("List events", _Tag, "Events the caller may list: Published (and Canceled with filters.includeCanceled), Upcoming by default. Orderings StartAscending (default), StartDescending, CreatedDescending, CreatedAscending, DistanceAscending (needs filters.near), Relevance (needs search). " +
                    "Filters when (Upcoming, Past, All), startsAfterUtc, startsBeforeUtc, category, format, venueId, hostUserId, instructorId, near { lat, lng, radiusKm } (online-only events excluded unless includeOnline), rsvpStatus (Going, Interested, Waitlisted, None: the caller's), hasSpots, includeCanceled, audience, status, recurrenceId. " +
                    "In PartiallyGated, Members events appear to free accounts as locked stubs (locked true, viewer { canOpen: false, requires: Membership }). Events hosted only by members the caller blocked are omitted. " + _RecurrenceText + " Signed out with anonymous browsing: Public events only."),
                (ctx, query) => RunAsync(ctx, actor => _Directory.For(ctx).Events.Catalog.ListAsync(actor, query, ctx.Token)));
            post.Static.Add(HttpMethod.POST, _Events + "/map/enumerate", MapAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Enumerate events map features", _Tag, "Body { bbox { south, west, north, east } (required; west greater than east crosses the antimeridian), zoom (0 to 22, default 2), maxResults (transport page size, default 25), continuationToken, ordering, createdAfterUtc, createdBeforeUtc, labels, labelMatch, tags, search, " +
                    "filters (when, startsAfterUtc, startsBeforeUtc, category, format, venueId, hostUserId, instructorId, rsvpStatus, hasSpots, includeCanceled, audience) }. The events the caller may list (as POST /api/v1/events/enumerate: audiences, locked stubs, blocked hosts) whose position is in the viewport. " +
                    "While they fit in events.mapMaxFeatures every one is a Point { kind, event { id, slug, title, startUtc, endUtc, timeZone, startLocal, isAllDay, format, status, category, locationName, locality, region, countryCode, venue, geo { lat, lng, precision }, spotsLeft, locked, coverThumbUrl } }; " +
                    "otherwise the server clusters on a grid sized from the zoom (as the directory map) and returns Cluster { kind, count, lat, lng, bbox } per non-empty cell. Online events are never on the map. Positions follow the stricter of the event's and its venue's address visibility: " +
                    "LocalityOnly events, and events at venues the caller may not read, are left out; Approximate ones sit at 2 decimals; event staff see exact positions. Standard keyset pages enumerate a generated feature set whose configured budget is independent of page size. totalRecords counts features; matchedRecords counts source records. IDs and cursors are scoped to viewport, zoom, budget and visibility. Created ordering uses point creation time and Unix epoch for generated cells. Rate limited (events.mapPerMinute).")
                    .Body("MapEnumerationQuery").Enumeration("MapFeatureView", "StartAscending, StartDescending, CreatedAscending, CreatedDescending", "when, startsAfterUtc, startsBeforeUtc, category, format, venueId, hostUserId, instructorId, rsvpStatus, hasSpots, includeCanceled, audience", false).Resp(400, "ValidationFailed or InvalidGeo").Resp(404, "NotAvailable (features.events is false)").Resp(429, "RateLimited"));
            EnumerationRoutes.Add(post, _Events + "/categories",EventCollections.Categories, "EventCategory",
                () => Doc("List event categories", _Tag, "Configured categories that are not retired, in admin order (SortOrderAscending, the default; LabelAscending). Seeded from property.json events.categories."),
                (ctx, query) => RunAsync(ctx, actor => _Directory.For(ctx).Events.Categories.ListAsync(actor, query, ctx.Token)));
            post.Static.Add(HttpMethod.POST, _Events + "/proposals", ProposeAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Propose an event", _Tag, "Members propose a single event for review (resolved decision 10). Body { title, summary, description, category, format, startLocal, endLocal, timeZone, isAllDay, location, online, capacity, maxGuestsPerRsvp, waitlistEnabled, rsvpAudience, attendeeVisibility, externalUrl, coverMediaId, reminderOffsetsMinutes, commentsEnabled, audience, labels, tags, proposalNote }. " +
                    "Status Proposed; the proposer becomes the host on approval. Refused: hostUserIds, recurrence, and Public audience. events.proposalsEnabled and property.json events.proposals; at most events.maxPendingProposalsPerMember pending; events.proposalsPerDay per day; word filtered. Event staff get staff_event_proposal.")
                    .Body("EventRequest").Returns("EventProposal", 201).Resp(400, "ValidationFailed, InvalidSchedule, InvalidGeo, UnknownOption, or ContentFiltered")
                    .Resp(403, "ProposalsDisabled, PostingNotAllowed, or Muted").Resp(409, "ProposalLimitReached").Resp(429, "RateLimited (events.proposalsPerDay)"));
            post.Parameter.Add(HttpMethod.POST, _Events + "/proposals/{id}/withdraw", WithdrawAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Withdraw an event proposal", _Tag, "The proposer, while the proposal is Proposed. Status Withdrawn.")
                    .PathParam("id", "Event identifier").Returns("EventProposal").Resp(404, "NotFound").Resp(409, "NotPending"));
            post.Parameter.Add(HttpMethod.GET, _Events + "/{idOrSlug}", GetAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Read an event", _Tag, "By identifier or slug. Event: EventSummary plus description, maxGuestsPerRsvp, hosts, instructors, and attendeePreview (at most 10 Going members when viewer.canSeeAttendees). online.joinUrl only for Going members, event staff, and hosts from joinUrlAvailableUtc (start minus events.joinUrlRevealMinutes). " +
                    "A locked stub in PartiallyGated for free accounts. " + _RecurrenceText)
                    .PathParam("idOrSlug", "Event identifier or slug").Returns(_Event).Resp(404, "NotFound or NotAvailable (features.events is false)"));
            post.Parameter.Add(HttpMethod.PUT, _Events + "/{id}/rsvp", RsvpAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("RSVP to an event", _Tag, "Body { status: Going | Interested, guestCount (0 to the event's maxGuestsPerRsvp), hideFromAttendeeLists }. One transaction locks the event, recounts Going plus guests, and decides: Going on a full event returns 200 with myRsvp.status Waitlisted and a position when waitlistEnabled, else 409 EventFull. " +
                    "Interested never counts against capacity. Window: rsvpOpensUtc (or publish) to rsvpClosesUtc (or the start). The caller must read the event and meet rsvpAudience. At most events.maxActiveRsvpsPerMember RSVPs on upcoming events.")
                    .PathParam("id", "Event identifier").Body("EventRsvpRequest").Returns(_Event).Resp(400, "ValidationFailed").Resp(403, "RsvpNotAllowed or Muted").Resp(404, "NotFound")
                    .Resp(409, "EventFull, RsvpClosed, EventCanceled, or RsvpLimitReached").Resp(429, "RateLimited (events.rsvpsPerMinute, events.rsvpsPerDay)"));
            post.Parameter.Add(HttpMethod.DELETE, _Events + "/{id}/rsvp", CancelRsvpAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Cancel your RSVP", _Tag, "Not going. Allowed until the start; the oldest waitlisted parties that fit are promoted in the same transaction (EventWaitlistPromoted). Idempotent.")
                    .PathParam("id", "Event identifier").Returns(_Event).Resp(404, "NotFound").Resp(409, "RsvpClosed (after the start)").Resp(429, "RateLimited"));
            EnumerationRoutes.Add(post, _Events + "/{id}/attendees", EventCollections.Attendees, "EventAttendee",
                () => Doc("List an event's attendees", _Tag, "Per attendeeVisibility (Everyone, Attendees: Going members, Staff: event staff and hosts); 404 when viewer.canSeeAttendees is false. Filter status: Going (default; members who hide from attendee lists are omitted except for staff and hosts); staff and hosts also Interested, Waitlisted, CheckedIn. Each item carries { id, user, status, guestCount, position, checkedInUtc, createdUtc, labels, tags }."),
                (ctx, query) => RunAsync(ctx, actor => _Directory.For(ctx).Events.Catalog.AttendeesAsync(actor, RouteHelper.Param(ctx, "id"), query, ctx.Token)), "id");
            EnumerationRoutes.Add(post, _Events + "/{id}/occurrences", EventCollections.Occurrences, _Summary,
                () => Doc("List the occurrences of a recurring event", _Tag, "Every occurrence of the same recurrence the caller may see (when All by default, canceled included unless filters.includeCanceled is false), StartAscending by default. A single event lists itself. Filters when, startsAfterUtc, startsBeforeUtc, includeCanceled, rsvpStatus."),
                (ctx, query) => RunAsync(ctx, actor => _Directory.For(ctx).Events.Catalog.OccurrencesAsync(actor, RouteHelper.Param(ctx, "id"), query, ctx.Token)), "id");
            post.Parameter.Add(HttpMethod.GET, _Events + "/{id}/ics", IcsAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Download an event's ICS", _Tag, "RFC 5545 VCALENDAR with one VEVENT (UID, SEQUENCE, DTSTART and DTEND with TZID and an embedded VTIMEZONE, or VALUE=DATE for all-day events, SUMMARY, DESCRIPTION, LOCATION respecting addressVisibility, URL, STATUS:CANCELLED for canceled events, VALARM per reminder offset). " +
                    "An occurrence carries the series UID and RECURRENCE-ID. scope=Series returns the master VEVENT with RRULE (FREQ, INTERVAL, BYDAY with ordinal, BYMONTHDAY, COUNT or UNTIL in UTC), EXDATE for canceled or deleted occurrences, and override VEVENTs with RECURRENCE-ID for edited ones. The join URL is included only for Going callers after it becomes available.")
                    .PathParam("id", "Event identifier").QueryParam("scope", "Series for the whole series of an occurrence").File("text/calendar", "VCALENDAR").Resp(404, "NotFound").Resp(429, "RateLimited (events.icsPerHour)"));
        }

        private void RegisterMe(WatsonWebserver.Core.Routing.RoutingGroup post)
        {
            EnumerationRoutes.Add(post, Constants.ApiPrefix + "/me/events", EventCollections.MyEvents, _Summary,
                () => Doc("List your events", _Tag, "Events you RSVPed to or host (canceled ones included; drafts you host too). Filters rsvpStatus (Going, Interested, Waitlisted, None), upcoming, past, category, format. Signed in."),
                (ctx, query) => RunAsync(ctx, actor => _Directory.For(ctx).Events.Catalog.MyEventsAsync(actor, query, ctx.Token)));
            EnumerationRoutes.Add(post, Constants.ApiPrefix + "/me/events/proposals", EventCollections.MyProposals, "EventProposal",
                () => Doc("List your event proposals", _Tag, "Your proposals, newest first. Filter status (Proposed, Draft, Published, Canceled, Rejected, Withdrawn). Each item carries { id, title, status, startUtc, timeZone, proposalNote, reviewNote, reviewedUtc, createdUtc, labels, tags }."),
                (ctx, query) => RunAsync(ctx, actor => _Directory.For(ctx).Events.Proposals.MineAsync(actor, query, ctx.Token)));
            post.Static.Add(HttpMethod.GET, Constants.ApiPrefix + "/me/calendar-feed", FeedAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Read your calendar feed", _Tag, "Your personal subscription URL (created on the first call): it carries { url, createdUtc, rotatedUtc, lastFetchedUtc }. The URL contains a secret; anyone with it sees your Going and Waitlisted events (never join links or attendees). 404 NotAvailable when events.calendarFeedEnabled is off.")
                    .Returns("CalendarFeed").Resp(404, "NotAvailable"));
            post.Static.Add(HttpMethod.POST, Constants.ApiPrefix + "/me/calendar-feed/rotate", RotateAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Rotate your calendar feed", _Tag, "A new URL; the old one answers 404 from now on.")
                    .Returns("CalendarFeed").Resp(404, "NotAvailable"));
            post.Static.Add(HttpMethod.DELETE, Constants.ApiPrefix + "/me/calendar-feed", DeleteFeedAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Delete your calendar feed", _Tag, "The URL stops working. Idempotent.").Resp(204, "Deleted"));
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

        private async Task MapAsync(HttpContextBase ctx)
        {
            MapEnumerationQuery query = RouteHelper.ReadEnumerationBody<MapEnumerationQuery>(ctx);
            ContentActor actor = await ActorAsync(ctx).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 200, await _Directory.For(ctx).Events.Catalog.MapAsync(actor, query, ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task GetAsync(HttpContextBase ctx)
        {
            ContentActor actor = await ActorAsync(ctx).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 200, await _Directory.For(ctx).Events.Catalog.GetAsync(actor, RouteHelper.Param(ctx, "idOrSlug"), ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task RsvpAsync(HttpContextBase ctx)
        {
            EventRsvpRequest request = RouteHelper.ReadJson<EventRsvpRequest>(ctx);
            ContentActor actor = await ActorAsync(ctx).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 200, await _Directory.For(ctx).Events.Rsvps.SetAsync(actor, RouteHelper.Param(ctx, "id"), request, ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task CancelRsvpAsync(HttpContextBase ctx)
        {
            ContentActor actor = await ActorAsync(ctx).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 200, await _Directory.For(ctx).Events.Rsvps.DeleteAsync(actor, RouteHelper.Param(ctx, "id"), ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task IcsAsync(HttpContextBase ctx)
        {
            ContentActor actor = await ActorAsync(ctx).ConfigureAwait(false);
            bool series = String.Equals(RouteHelper.Query(ctx, "scope"), "Series", StringComparison.OrdinalIgnoreCase);
            IcsDocument doc = await _Directory.For(ctx).Events.Calendar.EventAsync(actor, RouteHelper.Param(ctx, "id"), series, ctx.Token).ConfigureAwait(false);
            await SendIcsAsync(ctx, doc).ConfigureAwait(false);
        }

        private async Task ProposeAsync(HttpContextBase ctx)
        {
            EventRequest request = RouteHelper.ReadJson<EventRequest>(ctx);
            ContentActor actor = await ActorAsync(ctx).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 201, await _Directory.For(ctx).Events.Proposals.ProposeAsync(actor, Info(ctx), request, ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task WithdrawAsync(HttpContextBase ctx)
        {
            ContentActor actor = await ActorAsync(ctx).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 200, await _Directory.For(ctx).Events.Proposals.WithdrawAsync(actor, Info(ctx), RouteHelper.Param(ctx, "id"), ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task FeedAsync(HttpContextBase ctx)
        {
            ContentActor actor = await ActorAsync(ctx).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 200, await _Directory.For(ctx).Events.Calendar.GetFeedAsync(actor, ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task RotateAsync(HttpContextBase ctx)
        {
            ContentActor actor = await ActorAsync(ctx).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 200, await _Directory.For(ctx).Events.Calendar.RotateAsync(actor, ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task DeleteFeedAsync(HttpContextBase ctx)
        {
            ContentActor actor = await ActorAsync(ctx).ConfigureAwait(false);
            await _Directory.For(ctx).Events.Calendar.DeleteFeedAsync(actor, ctx.Token).ConfigureAwait(false);
            await RouteHelper.SendEmptyAsync(ctx, 204).ConfigureAwait(false);
        }

        /// <summary>
        /// Send an ICS document (text/calendar; attachment for single events, private cache for feeds).
        /// </summary>
        /// <param name="ctx">Context.</param>
        /// <param name="doc">Document.</param>
        /// <returns>Task.</returns>
        internal static async Task SendIcsAsync(HttpContextBase ctx, IcsDocument doc)
        {
            ctx.Response.StatusCode = 200;
            ctx.Response.ContentType = "text/calendar; charset=utf-8";
            if (doc.IsFeed) ctx.Response.Headers["Cache-Control"] = "private, max-age=900";
            else
            {
                ctx.Response.Headers["Cache-Control"] = "private, no-store";
                ctx.Response.Headers["Content-Disposition"] = "attachment; filename=\"" + doc.FileName.Replace("\"", String.Empty) + "\"";
            }
            await ctx.Response.Send(doc.Text, ctx.Token).ConfigureAwait(false);
        }

        #endregion
    }
}
