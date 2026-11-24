namespace TrellisKit.Server.Services
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core;
    using TrellisKit.Core.Enumeration;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Exceptions;
    using TrellisKit.Core.Helpers;
    using TrellisKit.Core.Models;
    using TrellisKit.Core.Observability;
    using TrellisKit.Core.Requests;
    using TrellisKit.Core.Responses;
    using TrellisKit.Core.Settings;

    /// <summary>
    /// Events (docs/PHASE4_CONTRACT.md sections 4.1, 4.2, 4.7, 4.9): member lists and reads, attendees, my events,
    /// public events, and the staff routes (create, edit with scopes, publish, cancel, duplicate, delete drafts, RSVP
    /// lists, CSV export, metrics). Event staff is SystemAdmin or EventManager; hosts edit the description, cover,
    /// labels, and tags of their events and check people in. Thread safety: stateless apart from the shared services.
    /// </summary>
    public class EventService
    {
        #region Public-Members

        /// <summary>
        /// Clock (tests replace it).
        /// </summary>
        public Func<DateTime> Clock
        {
            get { return _Clock; }
            set { _Clock = value ?? throw new ArgumentNullException(nameof(Clock)); }
        }

        /// <summary>
        /// Default radius of a near filter without radiusKm, in kilometers. Default 50.
        /// </summary>
        public const double DefaultRadiusKm = 50;

        /// <summary>
        /// Largest radius of a near filter, in kilometers. Default 2000.
        /// </summary>
        public const double MaxRadiusKm = 2000;

        #endregion

        #region Private-Members

        private readonly ApplicationServices _Services;
        private Func<DateTime> _Clock = () => DateTime.UtcNow;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="services">Property services.</param>
        /// <exception cref="ArgumentNullException">The services are null.</exception>
        public EventService(ApplicationServices services)
        {
            _Services = services ?? throw new ArgumentNullException(nameof(services));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// POST /api/v1/events/enumerate: events the caller may list (locked stubs in PartiallyGated).
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="query">Query.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Page.</returns>
        /// <exception cref="ApiErrorException">400 ValidationFailed or InvalidGeo.</exception>
        public async Task<EnumerationResult<EventSummaryView>> ListAsync(ContentActor actor, EnumerationQuery? query, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            EnumerationQuery q = query ?? new EnumerationQuery();
            EventListFilter filter = Filter(actor, q, false);
            filter.Audiences = actor.IsEventStaff ? null : Narrow(ContentAudienceRule.ListedAudiences(actor), q);
            if (!actor.IsEventStaff && !actor.IsAnonymous && !String.IsNullOrEmpty(actor.UserId)) filter.BlockerUserId = actor.UserId;
            if (!actor.IsEventStaff) filter.Statuses = filter.Statuses.Where(s => s == EventStatusEnum.Published || s == EventStatusEnum.Canceled).ToList();
            EnumerationRequest request = Prepare(q, EventCollections.Events, actor, filter);
            EnumerationResult<EventRecord> page = await _Services.Database.Events.Records.EnumerateAsync(filter, request, token).ConfigureAwait(false);
            List<EventRecord> rows = page.Objects.Where(e => EventAccess.Read(e, actor) != EventReadEnum.Hidden).ToList();
            return page.With(await _Services.Events.Views.SummariesAsync(actor, rows, token).ConfigureAwait(false));
        }

        /// <summary>
        /// POST /api/v1/events/map/enumerate (docs/PHASE4_CONTRACT.md section 4.2): the events of the viewport the caller
        /// may list as points while they fit in the configured feature budget, otherwise server-side grid clusters sized from the zoom (the
        /// clustering of the venue map). Online events are never on the map. Positions follow the stricter of the event's
        /// and its venue's address visibility (LocalityOnly, or a venue the caller may not read: off the map; Approximate:
        /// rounded to 2 places); event staff see exact positions. Generated features use standard keyset pages.
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="query">Body (bbox required, zoom, maxResults, labels, tags, search, filters).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>One page of generated features with exact feature totals and matched source count.</returns>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        /// <exception cref="ApiErrorException">400 ValidationFailed or InvalidGeo, 429 RateLimited.</exception>
        public async Task<EnumerationResult<MapFeatureView>> MapAsync(ContentActor actor, MapEnumerationQuery query, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            ArgumentNullException.ThrowIfNull(query);
            _Services.Limits.EnforceMember(actor, "event-map", _Services.Settings.Events.MapPerMinute, RateLimitService.Minute);
            GeoBox box = DirectoryFilters.ValidateBox(query.Bbox, "bbox");
            int zoom = Math.Clamp(query.Zoom ?? 2, 0, 22);
            int cap = _Services.Settings.Events.MapMaxFeatures;
            int max = cap;
            string scope = MapFeatureBuilder.Scope(box, zoom, max, actor, false);
            EnumerationRequest request = _Services.Enumeration.Prepare(query, EventCollections.Map, actor.IsStaffFor(TargetTypeEnum.Event), scope);
            EventListFilter filter = Filter(actor, query, false);
            filter.Box = box;
            filter.IncludeOnline = false;
            filter.Audiences = actor.IsEventStaff ? null : Narrow(ContentAudienceRule.ListedAudiences(actor), query);
            filter.MapVenueAudiences = actor.IsEventStaff ? null : ContentAudienceRule.VisibleAudiences(actor);
            if (!actor.IsEventStaff && !actor.IsAnonymous && !String.IsNullOrEmpty(actor.UserId)) filter.BlockerUserId = actor.UserId;
            System.Diagnostics.Stopwatch watch = System.Diagnostics.Stopwatch.StartNew();
            using (System.Diagnostics.Activity? activity = TrellisKitTelemetry.StartSpan("events.map"))
            {
                long count = await _Services.Database.Events.Records.CountMapAsync(filter, request, token).ConfigureAwait(false);
                List<MapFeatureView> features = new List<MapFeatureView>();
                string mode;
                if (count <= max)
                {
                    mode = "points";
                    List<EventMapPoint> points = await _Services.Database.Events.Records.MapPointsAsync(filter, request, max, null, token).ConfigureAwait(false);
                    features.AddRange(await MapPointsAsync(actor, points, token).ConfigureAwait(false));
                }
                else
                {
                    mode = "clusters";
                    double cell = TrellisKit.Core.Helpers.GeoMath.CellSize(box, zoom, max);
                    List<MapCluster> cells = await _Services.Database.Events.Records.MapClustersAsync(filter, request, cell, max, token).ConfigureAwait(false);
                    List<string> singles = cells.Where(c => c.ItemId != null).Select(c => c.ItemId!).ToList();
                    List<EventMapPoint> points = singles.Count == 0 ? new List<EventMapPoint>() : await _Services.Database.Events.Records.MapPointsAsync(filter, request, singles.Count, singles, token).ConfigureAwait(false);
                    Dictionary<string, MapFeatureView> byId = (await MapPointsAsync(actor, points, token).ConfigureAwait(false)).ToDictionary(p => p.Event!.Id, StringComparer.Ordinal);
                    features.AddRange(MapFeatureBuilder.Merge(cells, byId));
                }
                activity?.SetTag("map.mode", mode);
                TrellisKitTelemetry.Add(EventTelemetry.MapQueries, 1, "mode", mode);
                TrellisKitTelemetry.Record(EventTelemetry.MapDuration, watch.Elapsed.TotalSeconds);
                return MapFeatureBuilder.Page(request, features, count, scope);
            }
        }

        /// <summary>
        /// POST /api/v1/events/{id}/occurrences/enumerate: the occurrences of the same recurrence the caller may see.
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="id">Any occurrence of the recurrence.</param>
        /// <param name="query">Query.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Page (a single event is its own only occurrence, subject to the same filters and paging).</returns>
        /// <exception cref="ApiErrorException">404 NotFound.</exception>
        public async Task<EnumerationResult<EventSummaryView>> OccurrencesAsync(ContentActor actor, string id, EnumerationQuery? query, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            EventRecord e = await RequireReadableAsync(actor, id, token).ConfigureAwait(false);
            EnumerationQuery q = query ?? new EnumerationQuery();
            EventListFilter filter = Filter(actor, q, false);
            if (!q.HasFilter("when")) filter.When = EventWhenEnum.All;
            filter.RecurrenceId = e.RecurrenceId;
            if (e.RecurrenceId == null) filter.EventId = e.Id;
            filter.IncludeCanceled = filter.IncludeCanceled || !q.HasFilter("includeCanceled");
            if (EventAccess.IsStaffOrHost(e, actor)) filter.Statuses = new List<EventStatusEnum> { EventStatusEnum.Draft, EventStatusEnum.Published, EventStatusEnum.Canceled };
            filter.Audiences = actor.IsEventStaff ? null : ContentAudienceRule.ListedAudiences(actor);
            EnumerationRequest request = Prepare(q, EventCollections.Occurrences, actor, filter, e.RecurrenceId ?? e.Id);
            EnumerationResult<EventRecord> page = await _Services.Database.Events.Records.EnumerateAsync(filter, request, token).ConfigureAwait(false);
            List<EventRecord> rows = page.Objects.Where(o => EventAccess.Read(o, actor) != EventReadEnum.Hidden).ToList();
            return page.With(await _Services.Events.Views.SummariesAsync(actor, rows, token).ConfigureAwait(false));
        }

        /// <summary>
        /// POST /api/v1/me/events/enumerate: events the caller RSVPed to or hosts (canceled ones included).
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="query">Query (filters rsvpStatus, upcoming, past).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Page.</returns>
        public async Task<EnumerationResult<EventSummaryView>> MyEventsAsync(ContentActor actor, EnumerationQuery? query, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            string userId = actor.RequireUserId();
            EnumerationQuery q = query ?? new EnumerationQuery();
            Dictionary<string, string> errors = new Dictionary<string, string>();
            bool? upcoming = q.FilterBool("upcoming", errors);
            bool? past = q.FilterBool("past", errors);
            EventListFilter filter = new EventListFilter
            {
                NowUtc = _Clock(),
                MineUserId = userId,
                When = upcoming == true && past != true ? EventWhenEnum.Upcoming : past == true && upcoming != true ? EventWhenEnum.Past : EventWhenEnum.All,
                Statuses = new List<EventStatusEnum> { EventStatusEnum.Published, EventStatusEnum.Canceled },
                DraftHostUserId = userId,
                Categories = ForumFilters.List(q, "category") ?? new List<string>(),
                Formats = ForumFilters.Enums<EventFormatEnum>(q, "format", errors),
                OnlyUnmoderated = false
            };
            RsvpStatuses(q, filter, userId, errors);
            RouteHelperErrors(errors);
            EnumerationRequest request = Prepare(q, EventCollections.MyEvents, actor, filter);
            EnumerationResult<EventRecord> page = await _Services.Database.Events.Records.EnumerateAsync(filter, request, token).ConfigureAwait(false);
            List<EventRecord> rows = page.Objects.Where(e => EventAccess.Read(e, actor) != EventReadEnum.Hidden || e.HostUserIds.Contains(userId, StringComparer.Ordinal)).ToList();
            return page.With(await _Services.Events.Views.SummariesAsync(actor, rows, token).ConfigureAwait(false));
        }

        /// <summary>
        /// GET /api/v1/events/{idOrSlug}.
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="idOrSlug">Identifier or slug.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Event (a locked stub when the caller lacks the membership its audience needs).</returns>
        /// <exception cref="ApiErrorException">404 NotFound.</exception>
        public async Task<EventView> GetAsync(ContentActor actor, string idOrSlug, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            EventRecord e = await LoadAsync(idOrSlug, token).ConfigureAwait(false) ?? throw ApiErrorException.NotFound("Event");
            if (EventAccess.Read(e, actor) == EventReadEnum.Hidden) throw ApiErrorException.NotFound("Event");
            return await _Services.Events.Views.DetailAsync(actor, e, token).ConfigureAwait(false);
        }

        /// <summary>
        /// POST /api/v1/events/{id}/attendees/enumerate.
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="id">Event.</param>
        /// <param name="query">Query (filter status: Going default; staff and hosts also Interested, Waitlisted, CheckedIn).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Page.</returns>
        /// <exception cref="ApiErrorException">400, 404 when the caller may not see attendees.</exception>
        public async Task<EnumerationResult<EventAttendeeView>> AttendeesAsync(ContentActor actor, string id, EnumerationQuery? query, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            EventRecord e = await RequireReadableAsync(actor, id, token).ConfigureAwait(false);
            EventRsvp? mine = String.IsNullOrEmpty(actor.UserId) ? null : await _Services.Database.Events.Rsvps.ReadAsync(e.Id, actor.UserId, token).ConfigureAwait(false);
            if (!EventAccess.CanSeeAttendees(e, actor, mine)) throw ApiErrorException.NotFound("Event");
            bool staff = EventAccess.IsStaffOrHost(e, actor);
            EnumerationQuery q = query ?? new EnumerationQuery();
            List<string> raw = ForumFilters.List(q, "status") ?? new List<string> { "Going" };
            List<EventRsvpStatusEnum> statuses = new List<EventRsvpStatusEnum>();
            bool? checkedIn = null;
            foreach (string s in raw)
            {
                if (String.Equals(s, "CheckedIn", StringComparison.OrdinalIgnoreCase)) { statuses.Add(EventRsvpStatusEnum.Going); checkedIn = true; continue; }
                if (!Enum.TryParse(s, true, out EventRsvpStatusEnum status) || !Enum.IsDefined(status)) throw ApiErrorException.Field("filters.status", "Use Going, Interested, Waitlisted, or CheckedIn.");
                statuses.Add(status);
            }
            if (!staff && (statuses.Any(s => s != EventRsvpStatusEnum.Going) || checkedIn != null)) throw ApiErrorException.Field("filters.status", "Only event staff and hosts may list other statuses.");
            EnumerationRequest request = _Services.Enumeration.Prepare(q, EventCollections.Attendees, actor.IsStaffFor(TargetTypeEnum.EventRsvp), e.Id);
            EnumerationResult<EventRsvp> page = await _Services.Database.Events.Rsvps.EnumerateAsync(e.Id, statuses.Distinct().ToList(), checkedIn, staff, request, token).ConfigureAwait(false);
            Dictionary<string, UserSummary> people = await _Services.Summaries.BuildAsync(page.Objects.Select(r => r.UserId), token).ConfigureAwait(false);
            List<EventAttendeeView> views = page.Objects.Select(r => new EventAttendeeView
            {
                Id = r.Id,
                User = people.TryGetValue(r.UserId, out UserSummary? u) ? u : UserSummary.Deleted(),
                Status = r.Status,
                GuestCount = r.GuestCount,
                Position = r.Position,
                CheckedInUtc = staff ? r.CheckedInUtc : null,
                CreatedUtc = r.CreatedUtc
            }).ToList();
            await _Services.Metadata.AttachAsync(TargetTypeEnum.EventRsvp, views, v => v.Id, actor.IsStaffFor(TargetTypeEnum.EventRsvp), token).ConfigureAwait(false);
            return page.With(views);
        }

        /// <summary>
        /// POST /api/v1/public/events/enumerate: upcoming Public published events (events.publicListingsEnabled).
        /// </summary>
        /// <param name="actor">Anonymous caller.</param>
        /// <param name="query">Query (maxResults at most 20).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Page.</returns>
        /// <exception cref="ApiErrorException">404 NotAvailable when public listings are off.</exception>
        public async Task<EnumerationResult<PublicEventView>> PublicListAsync(ContentActor actor, EnumerationQuery? query, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            RequirePublic();
            EnumerationQuery q = query ?? new EnumerationQuery();
            Dictionary<string, string> errors = new Dictionary<string, string>();
            EventListFilter filter = new EventListFilter
            {
                NowUtc = _Clock(),
                When = EventWhenEnum.Upcoming,
                Audiences = new List<ContentAudienceEnum> { ContentAudienceEnum.Public },
                StartsAfterUtc = q.FilterUtc("startsAfterUtc", errors),
                StartsBeforeUtc = q.FilterUtc("startsBeforeUtc", errors),
                Categories = ForumFilters.List(q, "category") ?? new List<string>(),
                Formats = ForumFilters.Enums<EventFormatEnum>(q, "format", errors)
            };
            RouteHelperErrors(errors);
            EnumerationRequest request = _Services.Enumeration.Prepare(q, EventCollections.PublicEvents, false, String.Empty, 20);
            EnumerationResult<EventRecord> page = await _Services.Database.Events.Records.EnumerateAsync(filter, request, token).ConfigureAwait(false);
            return page.With(await _Services.Events.Views.PublicAsync(actor, page.Objects, token).ConfigureAwait(false));
        }

        /// <summary>
        /// GET /api/v1/public/events/{id}.
        /// </summary>
        /// <param name="actor">Anonymous caller.</param>
        /// <param name="idOrSlug">Identifier or slug.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Public event.</returns>
        /// <exception cref="ApiErrorException">404 NotFound or NotAvailable.</exception>
        public async Task<PublicEventView> PublicGetAsync(ContentActor actor, string idOrSlug, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            RequirePublic();
            EventRecord? e = await LoadAsync(idOrSlug, token).ConfigureAwait(false);
            if (e == null || e.Audience != ContentAudienceEnum.Public || e.ModerationState != ModerationStateEnum.None || (e.Status != EventStatusEnum.Published && e.Status != EventStatusEnum.Canceled))
                throw ApiErrorException.NotFound("Event");
            return (await _Services.Events.Views.PublicAsync(actor, new List<EventRecord> { e }, token).ConfigureAwait(false))[0];
        }

        /// <summary>
        /// POST /api/v1/admin/events/enumerate (event staff).
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="query">Query.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Page of AdminEvent.</returns>
        /// <exception cref="ApiErrorException">403 Forbidden for non-staff.</exception>
        public async Task<EnumerationResult<AdminEventView>> AdminListAsync(ContentActor actor, EnumerationQuery? query, CancellationToken token = default)
        {
            RequireStaff(actor);
            EnumerationQuery q = query ?? new EnumerationQuery();
            EventListFilter filter = Filter(actor, q, true);
            if (!q.HasFilter("when")) filter.When = EventWhenEnum.All;
            if (!q.HasFilter("status")) filter.Statuses = new List<EventStatusEnum> { EventStatusEnum.Draft, EventStatusEnum.Published, EventStatusEnum.Canceled };
            filter.OnlyUnmoderated = false;
            filter.Audiences = Narrow(null, q);
            EnumerationRequest request = Prepare(q, EventCollections.AdminEvents, actor, filter);
            EnumerationResult<EventRecord> page = await _Services.Database.Events.Records.EnumerateAsync(filter, request, token).ConfigureAwait(false);
            return page.With(await _Services.Events.Views.AdminPageAsync(actor, page.Objects, token).ConfigureAwait(false));
        }

        /// <summary>
        /// GET /api/v1/admin/events/{id} (event staff, or a host of the event).
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="id">Event.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>AdminEvent.</returns>
        /// <exception cref="ApiErrorException">403, 404.</exception>
        public async Task<AdminEventView> AdminGetAsync(ContentActor actor, string id, CancellationToken token = default)
        {
            EventRecord e = await RequireManagedAsync(actor, id, true, token).ConfigureAwait(false);
            return await _Services.Events.Views.AdminAsync(actor, e, token).ConfigureAwait(false);
        }

        /// <summary>
        /// POST /api/v1/admin/events: create a Draft (with recurrence, a recurring series of Draft occurrences).
        /// </summary>
        /// <param name="actor">Event staff.</param>
        /// <param name="info">Request (audit).</param>
        /// <param name="request">Body.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>AdminEvent (the first occurrence of a series).</returns>
        /// <exception cref="ApiErrorException">400, 403.</exception>
        public async Task<AdminEventView> CreateAsync(ContentActor actor, RequestInfo info, EventRequest request, CancellationToken token = default)
        {
            RequireStaff(actor);
            ArgumentNullException.ThrowIfNull(request);
            EventRecord created = await CreateCoreAsync(actor, request, false, token).ConfigureAwait(false);
            await _Services.Audit.WriteAsync(info, "event.create", "Event", created.Id, created.Title + (created.RecurrenceId == null ? String.Empty : " (recurring)"), token).ConfigureAwait(false);
            EventTelemetry.Count(EventTelemetry.Events, 1, "action", "create");
            return await _Services.Events.Views.AdminAsync(actor, created, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Validate and write a new event (staff create or member proposal).
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="request">Body.</param>
        /// <param name="proposal">Member proposal (status Proposed, single event).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The stored event (first occurrence of a series).</returns>
        /// <exception cref="ApiErrorException">400 and 409 from validation.</exception>
        public async Task<EventRecord> CreateCoreAsync(ContentActor actor, EventRequest request, bool proposal, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            ArgumentNullException.ThrowIfNull(request);
            HashSet<string> present = EventEditor.PresentOf(request);
            EventEditResult result = await _Services.Events.Editor.ApplyAsync(actor, null, request, present, proposal, token).ConfigureAwait(false);
            EventRecord e = result.Record;
            DateTime now = _Clock();
            if (proposal)
            {
                e.Status = EventStatusEnum.Proposed;
                e.ProposedByUserId = actor.UserId;
            }
            if (result.Rule != null)
            {
                e = await _Services.Events.Recurrence.CreateSeriesAsync(actor, e, result.Rule, result.StartLocal, token).ConfigureAwait(false);
                EventRecurrence? r = await _Services.Database.Events.Recurrences.ReadAsync(e.RecurrenceId!, token).ConfigureAwait(false);
                string metadataTarget = r?.TemplateEventId ?? e.Id;
                if (request.Labels != null || request.Tags != null)
                {
                    await _Services.Metadata.ApplyAsync(actor, TargetTypeEnum.Event, metadataTarget, request.Labels, request.Tags, token).ConfigureAwait(false);
                    foreach (EventRecord o in await _Services.Database.Events.Records.ListOccurrencesAsync(e.RecurrenceId!, 5000, token).ConfigureAwait(false))
                        await _Services.Metadata.SnapshotAsync(TargetTypeEnum.Event, metadataTarget, TargetTypeEnum.Event, o.Id, token).ConfigureAwait(false);
                }
                if (e.CoverMediaId != null) await AttachCoverAsync(actor, metadataTarget, e.CoverMediaId, token).ConfigureAwait(false);
                await SyncCoversAsync(new[] { e.CoverMediaId }, token).ConfigureAwait(false);
                return await _Services.Database.Events.Records.ReadAsync(e.Id, token).ConfigureAwait(false) ?? e;
            }
            await _Services.Database.Events.Records.CreateManyAsync(new List<EventRecord> { e }, now, token).ConfigureAwait(false);
            if (request.Labels != null || request.Tags != null) await _Services.Metadata.ApplyAsync(actor, TargetTypeEnum.Event, e.Id, request.Labels, request.Tags, token).ConfigureAwait(false);
            if (e.CoverMediaId != null) await AttachCoverAsync(actor, e.Id, e.CoverMediaId, token).ConfigureAwait(false);
            await _Services.Forums.Search.EnqueueAsync(TargetTypeEnum.Event, e.Id, token).ConfigureAwait(false);
            await SyncCoversAsync(new[] { e.CoverMediaId }, token).ConfigureAwait(false);
            return await _Services.Database.Events.Records.ReadAsync(e.Id, token).ConfigureAwait(false) ?? e;
        }

        /// <summary>
        /// PUT /api/v1/admin/events/{id}: edit (omitted fields stay). On an occurrence, scope One (default), Following,
        /// or All. Time, location, or format changes send EventUpdated unless notifyAttendees is false.
        /// </summary>
        /// <param name="actor">Event staff, or a host for the fields hosts may edit.</param>
        /// <param name="info">Request (audit).</param>
        /// <param name="id">Event.</param>
        /// <param name="request">Body.</param>
        /// <param name="present">Present fields.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>AdminEvent.</returns>
        /// <exception cref="ApiErrorException">400, 403, 404, 409.</exception>
        public async Task<AdminEventView> UpdateAsync(ContentActor actor, RequestInfo info, string id, EventRequest request, HashSet<string> present, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            ArgumentNullException.ThrowIfNull(present);
            EventRecord e = await RequireManagedAsync(actor, id, true, token).ConfigureAwait(false);
            if (e.Status == EventStatusEnum.Proposed || e.Status == EventStatusEnum.Rejected || e.Status == EventStatusEnum.Withdrawn)
            {
                if (!actor.IsEventStaff) throw ApiErrorException.Forbidden("Only event staff may edit a proposal.");
            }
            EventEditScopeEnum scope = request.Scope ?? EventEditScopeEnum.One;
            if (e.RecurrenceId == null && present.Contains("recurrence") && request.Recurrence != null)
                throw ApiErrorException.Field("recurrence", "A single event cannot become recurring; create a recurring event instead.");
            EventRecord after;
            if (e.RecurrenceId != null && scope != EventEditScopeEnum.One)
            {
                after = await _Services.Events.Recurrence.EditSeriesAsync(actor, info, e, request, present, scope, token).ConfigureAwait(false);
            }
            else
            {
                if (present.Contains("recurrence") && request.Recurrence != null) throw ApiErrorException.Field("recurrence", "Change the rule with scope Following or All.");
                after = await UpdateOneAsync(actor, info, e, request, present, token).ConfigureAwait(false);
            }
            EventTelemetry.Count(EventTelemetry.Events, 1, "action", "update");
            await SyncCoversAsync(new[] { e.CoverMediaId, after.CoverMediaId }, token).ConfigureAwait(false);
            return await _Services.Events.Views.AdminAsync(actor, after, token).ConfigureAwait(false);
        }

        /// <summary>
        /// POST /api/v1/admin/events/{id}/publish (a recurring event publishes its series).
        /// </summary>
        /// <param name="actor">Event staff.</param>
        /// <param name="info">Request (audit).</param>
        /// <param name="id">Event.</param>
        /// <param name="request">Body.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>AdminEvent.</returns>
        /// <exception cref="ApiErrorException">403, 404, 409 EventCanceled or NotDraft.</exception>
        public async Task<AdminEventView> PublishAsync(ContentActor actor, RequestInfo info, string id, EventPublishRequest request, CancellationToken token = default)
        {
            RequireStaff(actor);
            ArgumentNullException.ThrowIfNull(request);
            EventRecord e = await RequireManagedAsync(actor, id, false, token).ConfigureAwait(false);
            if (e.Status == EventStatusEnum.Canceled) throw new ApiErrorException(409, "EventCanceled", "This event was canceled.");
            if (e.Status != EventStatusEnum.Draft && e.Status != EventStatusEnum.Published) throw new ApiErrorException(409, "NotDraft", "Only drafts can be published (proposals are approved).");
            EventRecord after = await PublishCoreAsync(actor, info, e, request.NotifyMembers ?? false, token).ConfigureAwait(false);
            return await _Services.Events.Views.AdminAsync(actor, after, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Publish an event (or its series) and optionally fan out NewEvent.
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="info">Request (audit).</param>
        /// <param name="e">Event.</param>
        /// <param name="notifyMembers">NewEvent fan-out.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The event afterwards.</returns>
        public async Task<EventRecord> PublishCoreAsync(ContentActor actor, RequestInfo info, EventRecord e, bool notifyMembers, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(e);
            DateTime now = _Clock();
            if (e.Status != EventStatusEnum.Published)
            {
                if (e.RecurrenceId != null)
                {
                    await _Services.Events.Recurrence.PublishSeriesAsync(e, token).ConfigureAwait(false);
                }
                else
                {
                    e.Status = EventStatusEnum.Published;
                    e.PublishedUtc = now;
                    await _Services.Database.Events.Records.UpdateAsync(e, false, now, token).ConfigureAwait(false);
                    await _Services.Forums.Search.EnqueueAsync(TargetTypeEnum.Event, e.Id, token).ConfigureAwait(false);
                }
                await _Services.Audit.WriteAsync(info, "event.publish", "Event", e.Id, e.Title, token).ConfigureAwait(false);
                EventTelemetry.Count(EventTelemetry.Events, 1, "action", "publish");
            }
            EventRecord after = await _Services.Database.Events.Records.ReadAsync(e.Id, token).ConfigureAwait(false) ?? e;
            await SyncCoversAsync(new[] { after.CoverMediaId }, token).ConfigureAwait(false);
            if (notifyMembers && after.Status == EventStatusEnum.Published) await _Services.Events.Notifier.StartEventAsync(NotificationTypeEnum.NewEvent, after, String.Empty, token).ConfigureAwait(false);
            if (!String.IsNullOrWhiteSpace(after.Description)) await MentionsAsync(after, token).ConfigureAwait(false);
            return after;
        }

        /// <summary>
        /// POST /api/v1/admin/events/{id}/cancel: status Canceled, reminders stopped, RSVPs kept, EventCanceled sent
        /// (Essential email). Occurrences take scope One (default), Following, or All.
        /// </summary>
        /// <param name="actor">Event staff.</param>
        /// <param name="info">Request (audit).</param>
        /// <param name="id">Event.</param>
        /// <param name="request">Body.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>AdminEvent.</returns>
        /// <exception cref="ApiErrorException">400 ValidationFailed, 403, 404, 409 EventCanceled.</exception>
        public async Task<AdminEventView> CancelAsync(ContentActor actor, RequestInfo info, string id, EventCancelRequest request, CancellationToken token = default)
        {
            RequireStaff(actor);
            ArgumentNullException.ThrowIfNull(request);
            EventRecord e = await RequireManagedAsync(actor, id, false, token).ConfigureAwait(false);
            string reason = MarkdownNormalizer.NormalizeSingleLine(request.Reason);
            if (reason.Length < 3 || reason.Length > 500) throw ApiErrorException.Field("reason", "Use 3 to 500 characters.");
            if (e.Status == EventStatusEnum.Canceled) throw new ApiErrorException(409, "EventCanceled", "This event is already canceled.");
            if (e.Status != EventStatusEnum.Published && e.Status != EventStatusEnum.Draft) throw new ApiErrorException(409, "NotDraft", "Only drafts and published events can be canceled.");
            bool notify = request.NotifyAttendees ?? true;
            EventEditScopeEnum scope = e.RecurrenceId == null ? EventEditScopeEnum.One : request.Scope ?? EventEditScopeEnum.One;
            DateTime now = _Clock();
            if (scope == EventEditScopeEnum.One)
            {
                await CancelOneAsync(actor, e, reason, now, token).ConfigureAwait(false);
                if (notify) await _Services.Events.Notifier.StartEventAsync(NotificationTypeEnum.EventCanceled, e, String.Empty, token).ConfigureAwait(false);
            }
            else
            {
                List<EventRecord> targets = await _Services.Events.Recurrence.SeriesCancelTargetsAsync(e, scope, token).ConfigureAwait(false);
                if (!targets.Any(t => t.Id == e.Id) && e.StartUtc > now) targets.Add(e);
                foreach (EventRecord t in targets) await CancelOneAsync(actor, t, reason, now, token).ConfigureAwait(false);
                if (notify && targets.Count > 0) await _Services.Events.Notifier.StartSeriesAsync(NotificationTypeEnum.EventCanceled, e.RecurrenceId!, e.Id, now, String.Empty, token).ConfigureAwait(false);
            }
            await _Services.Audit.WriteAsync(info, "event.cancel", "Event", e.Id, "scope " + scope + "; " + reason, token).ConfigureAwait(false);
            EventTelemetry.Count(EventTelemetry.Events, 1, "action", "cancel");
            EventRecord after = await _Services.Database.Events.Records.ReadAsync(e.Id, token).ConfigureAwait(false) ?? e;
            return await _Services.Events.Views.AdminAsync(actor, after, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Cancel one event row (occurrences become Canceled exceptions).
        /// </summary>
        /// <param name="actor">Caller, or null for the system (moderation removal).</param>
        /// <param name="e">Event.</param>
        /// <param name="reason">Reason.</param>
        /// <param name="nowUtc">Now.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        public async Task CancelOneAsync(ContentActor? actor, EventRecord e, string reason, DateTime nowUtc, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(e);
            EventRecord next = e.Clone();
            next.Status = EventStatusEnum.Canceled;
            next.CanceledUtc = nowUtc;
            next.CancelReason = reason;
            next.CanceledByUserId = actor?.UserId;
            next.IcsSequence = e.IcsSequence + 1;
            if (e.RecurrenceId != null) next.IsException = true;
            await _Services.Database.Events.Records.UpdateAsync(next, false, nowUtc, token).ConfigureAwait(false);
            await _Services.Database.Events.Reminders.SkipPendingAsync(e.Id, nowUtc, token).ConfigureAwait(false);
            if (e.RecurrenceId != null) await _Services.Events.Recurrence.UpsertExceptionAsync(e.RecurrenceId, e, EventRecurrenceExceptionKindEnum.Canceled, token).ConfigureAwait(false);
            await _Services.Forums.Search.EnqueueAsync(TargetTypeEnum.Event, e.Id, token).ConfigureAwait(false);
        }

        /// <summary>
        /// POST /api/v1/admin/events/{id}/duplicate: a Draft copy (no RSVPs, comments, reactions, or recurrence).
        /// </summary>
        /// <param name="actor">Event staff.</param>
        /// <param name="info">Request (audit).</param>
        /// <param name="id">Event.</param>
        /// <param name="request">Body (startLocal, timeZone).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The copy.</returns>
        /// <exception cref="ApiErrorException">400 InvalidSchedule, 403, 404.</exception>
        public async Task<AdminEventView> DuplicateAsync(ContentActor actor, RequestInfo info, string id, EventDuplicateRequest request, CancellationToken token = default)
        {
            RequireStaff(actor);
            ArgumentNullException.ThrowIfNull(request);
            EventRecord e = await RequireManagedAsync(actor, id, false, token).ConfigureAwait(false);
            TimeZoneInfo zone = EventTimeZones.Require(String.IsNullOrWhiteSpace(request.TimeZone) ? e.TimeZone : request.TimeZone);
            if (!EventTimeZones.TryParseLocal(request.StartLocal, out DateTime startLocal)) throw EventTimeZones.Invalid("startLocal", "Use yyyy-MM-ddTHH:mm in the event's time zone.");
            DateTime now = _Clock();
            EventRecord copy = e.Clone();
            copy.Id = TrellisKit.Core.Helpers.IdGenerator.Generate(TrellisKit.Core.Helpers.EventIdPrefixes.Event);
            copy.Slug = await _Services.Events.Editor.FreeSlugAsync(PublicSlugRules.Derive("event", e.Title, e.Locality, e.Region), null, token).ConfigureAwait(false);
            copy.Status = EventStatusEnum.Draft;
            copy.TimeZone = zone.Id;
            TimeSpan duration = e.EndUtc - e.StartUtc;
            copy.StartUtc = e.IsAllDay ? EventTimeZones.ToUtcLenient(startLocal.Date, zone) : EventTimeZones.ToUtcStrict(startLocal, zone, "startLocal");
            copy.EndUtc = copy.StartUtc + duration;
            copy.RecurrenceId = null;
            copy.OriginalStartUtc = null;
            copy.OccurrenceIndex = null;
            copy.IsException = false;
            copy.RsvpOpensUtc = null;
            copy.RsvpClosesUtc = null;
            copy.CanceledUtc = null;
            copy.CancelReason = null;
            copy.CanceledByUserId = null;
            copy.PublishedUtc = null;
            copy.ProposedByUserId = null;
            copy.ProposalNote = null;
            copy.ReviewNote = null;
            copy.ReviewerUserId = null;
            copy.ReviewedUtc = null;
            copy.PromotedFeedPostId = null;
            copy.ModerationState = ModerationStateEnum.None;
            copy.ModeratedUtc = null;
            copy.ModeratedByUserId = null;
            copy.ModerationReason = null;
            copy.IcsSequence = 0;
            copy.CreatedByUserId = actor.UserId;
            copy.CreatedUtc = now;
            copy.UpdatedUtc = now;
            await _Services.Database.Events.Records.CreateManyAsync(new List<EventRecord> { copy }, now, token).ConfigureAwait(false);
            await _Services.Metadata.SnapshotAsync(TargetTypeEnum.Event, e.Id, TargetTypeEnum.Event, copy.Id, token).ConfigureAwait(false);
            await _Services.Audit.WriteAsync(info, "event.duplicate", "Event", copy.Id, "from " + e.Id, token).ConfigureAwait(false);
            EventTelemetry.Count(EventTelemetry.Events, 1, "action", "duplicate");
            EventRecord stored = await _Services.Database.Events.Records.ReadAsync(copy.Id, token).ConfigureAwait(false) ?? copy;
            return await _Services.Events.Views.AdminAsync(actor, stored, token).ConfigureAwait(false);
        }

        /// <summary>
        /// DELETE /api/v1/admin/events/{id}: Drafts only (published events are canceled). A draft occurrence is
        /// deleted on its own (an exception) unless scope All deletes the whole draft series.
        /// </summary>
        /// <param name="actor">Event staff.</param>
        /// <param name="info">Request (audit).</param>
        /// <param name="id">Event.</param>
        /// <param name="scope">One or All.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        /// <exception cref="ApiErrorException">403, 404, 409 NotDraft.</exception>
        public async Task DeleteAsync(ContentActor actor, RequestInfo info, string id, EventEditScopeEnum scope, CancellationToken token = default)
        {
            RequireStaff(actor);
            EventRecord e = await RequireManagedAsync(actor, id, false, token).ConfigureAwait(false);
            if (e.Status != EventStatusEnum.Draft) throw new ApiErrorException(409, "NotDraft", "Only drafts can be deleted; cancel published events instead.");
            DateTime now = _Clock();
            if (e.RecurrenceId != null && scope == EventEditScopeEnum.All) await _Services.Events.Recurrence.DeleteSeriesAsync(e, token).ConfigureAwait(false);
            else
            {
                await _Services.Database.Events.Records.DeleteAsync(new[] { e.Id }, now, token).ConfigureAwait(false);
                if (e.RecurrenceId != null) await _Services.Events.Recurrence.UpsertExceptionAsync(e.RecurrenceId, e, EventRecurrenceExceptionKindEnum.Deleted, token).ConfigureAwait(false);
            }
            if (e.CoverMediaId != null) await _Services.Media.DetachAsync(TargetTypeEnum.Event, e.Id, null, token).ConfigureAwait(false);
            await SyncCoversAsync(new[] { e.CoverMediaId }, token).ConfigureAwait(false);
            await _Services.Forums.Search.EnqueueAsync(TargetTypeEnum.Event, e.Id, token).ConfigureAwait(false);
            await _Services.Audit.WriteAsync(info, "event.delete", "Event", e.Id, e.Title, token).ConfigureAwait(false);
            EventTelemetry.Count(EventTelemetry.Events, 1, "action", "delete");
        }

        /// <summary>
        /// POST /api/v1/admin/events/{id}/rsvps/enumerate (event staff and hosts).
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="id">Event.</param>
        /// <param name="query">Query (filters status, checkedIn).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Page.</returns>
        /// <exception cref="ApiErrorException">400, 403, 404.</exception>
        public async Task<EnumerationResult<AdminRsvpView>> AdminRsvpsAsync(ContentActor actor, string id, EnumerationQuery? query, CancellationToken token = default)
        {
            EventRecord e = await RequireManagedAsync(actor, id, true, token).ConfigureAwait(false);
            EnumerationQuery q = query ?? new EnumerationQuery();
            Dictionary<string, string> errors = new Dictionary<string, string>();
            List<EventRsvpStatusEnum> statuses = ForumFilters.Enums<EventRsvpStatusEnum>(q, "status", errors);
            bool? checkedIn = q.FilterBool("checkedIn", errors);
            RouteHelperErrors(errors);
            EnumerationRequest request = _Services.Enumeration.Prepare(q, EventCollections.AdminRsvps, actor.IsStaffFor(TargetTypeEnum.EventRsvp), e.Id);
            EnumerationResult<EventRsvp> page = await _Services.Database.Events.Rsvps.EnumerateAsync(e.Id, statuses, checkedIn, true, request, token).ConfigureAwait(false);
            return page.With(await AdminRsvpViewsAsync(page.Objects, actor, token).ConfigureAwait(false));
        }

        /// <summary>
        /// GET /api/v1/admin/events/{id}/rsvps/export.csv: one page of RSVPs as CSV (display name, handle, status,
        /// guests, check-in); X-Continuation-Token continues.
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="id">Event.</param>
        /// <param name="continuationToken">Continuation token, or null.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>CSV page.</returns>
        /// <exception cref="ApiErrorException">403, 404.</exception>
        public async Task<CsvPage> ExportCsvAsync(ContentActor actor, string id, string? continuationToken, CancellationToken token = default)
        {
            EventRecord e = await RequireManagedAsync(actor, id, true, token).ConfigureAwait(false);
            EnumerationQuery q = new EnumerationQuery { MaxResults = 1000, ContinuationToken = continuationToken };
            EnumerationRequest request = _Services.Enumeration.Prepare(q, EventCollections.AdminRsvps, true, e.Id);
            EnumerationResult<EventRsvp> page = await _Services.Database.Events.Rsvps.EnumerateAsync(e.Id, new List<EventRsvpStatusEnum>(), null, true, request, token).ConfigureAwait(false);
            Dictionary<string, UserSummary> people = await _Services.Summaries.BuildAsync(page.Objects.Select(r => r.UserId), token).ConfigureAwait(false);
            CsvWriter csv = new CsvWriter();
            csv.Row("displayName", "handle", "status", "guests", "position", "checkedInUtc", "createdUtc");
            foreach (EventRsvp r in page.Objects)
            {
                UserSummary u = people.TryGetValue(r.UserId, out UserSummary? found) ? found : UserSummary.Deleted();
                csv.Row(u.DisplayName, u.Handle ?? String.Empty, r.Status.ToString(), r.GuestCount, r.Position?.ToString(CultureInfo.InvariantCulture) ?? String.Empty,
                    r.CheckedInUtc?.ToString("O", CultureInfo.InvariantCulture) ?? String.Empty, r.CreatedUtc.ToString("O", CultureInfo.InvariantCulture));
            }
            return new CsvPage { Csv = csv.ToString(), ContinuationToken = page.ContinuationToken, TotalRecords = page.TotalRecords };
        }

        /// <summary>
        /// GET /api/v1/admin/metrics/events?days=30 (event staff).
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="days">Window (1 to 365, default 30).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Metrics.</returns>
        /// <exception cref="ApiErrorException">403.</exception>
        public async Task<EventMetricsView> MetricsAsync(ContentActor actor, int? days, CancellationToken token = default)
        {
            RequireStaff(actor);
            int window = Math.Clamp(days ?? 30, 1, 365);
            DateTime now = _Clock();
            DateTime since = now.Date.AddDays(1 - window);
            DateTime until = now.Date.AddDays(window + 1);
            List<EventDayCount> events = await _Services.Database.Events.Records.EventsByDayAsync(since, until, token).ConfigureAwait(false);
            List<EventDayCount> rsvps = await _Services.Database.Events.Records.RsvpsByDayAsync(since, now, token).ConfigureAwait(false);
            long[] totals = await _Services.Database.Events.Records.CheckInTotalsAsync(since, now, token).ConfigureAwait(false);
            List<EventCategoryCount> categories = await _Services.Database.Events.Records.CategoryCountsAsync(since, until, 500, token).ConfigureAwait(false);
            Dictionary<string, EventCategory> labels = (await _Services.Database.Events.Categories.ListAllAsync(token).ConfigureAwait(false)).ToDictionary(c => c.Key.ToLowerInvariant(), c => c, StringComparer.Ordinal);
            double rate = totals[0] == 0 ? 0 : Math.Round((double)totals[1] / totals[0], 4);
            return new EventMetricsView
            {
                Days = window,
                EventsByDay = events.Select(d => new EventDayCountView { Day = d.Day, Count = d.Count }).ToList(),
                RsvpsByDay = rsvps.Select(d => new EventDayCountView { Day = d.Day, Count = d.Count }).ToList(),
                CheckInRate = rate,
                NoShowRate = totals[0] == 0 ? 0 : Math.Round(1 - rate, 4),
                ByCategory = new EventCategoryMetricsPage
                {
                    Objects = categories.Take(20).Select(c => new EventCategoryMetricView
                    {
                        Key = c.Key,
                        Label = labels.TryGetValue(c.Key.ToLowerInvariant(), out EventCategory? cat) ? cat.Label : c.Key.Length == 0 ? "Uncategorized" : c.Key,
                        Events = c.Events,
                        Rsvps = c.Rsvps
                    }).ToList(),
                    TotalRecords = categories.Count,
                    Enumerate = Constants.ApiPrefix + "/admin/events/categories/enumerate"
                }
            };
        }

        /// <summary>
        /// Load an event the caller manages: event staff, or (allowHost) a host of the event.
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="id">Event.</param>
        /// <param name="allowHost">Whether hosts qualify.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Event.</returns>
        /// <exception cref="ApiErrorException">403 Forbidden, 404 NotFound.</exception>
        public async Task<EventRecord> RequireManagedAsync(ContentActor actor, string id, bool allowHost, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            EventRecord? e = await _Services.Database.Events.Records.ReadAsync(id, token).ConfigureAwait(false);
            if (e == null || e.Status == EventStatusEnum.Template)
            {
                if (!actor.IsEventStaff) throw ApiErrorException.Forbidden("Event staff only.");
                throw ApiErrorException.NotFound("Event");
            }
            if (actor.IsEventStaff) return e;
            if (allowHost && EventAccess.IsHost(e, actor)) return e;
            throw ApiErrorException.Forbidden("Event staff only.");
        }

        /// <summary>
        /// The staff view of one RSVP.
        /// </summary>
        /// <param name="rsvp">RSVP.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>View.</returns>
        public async Task<AdminRsvpView> AdminRsvpViewAsync(EventRsvp rsvp, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(rsvp);
            List<AdminRsvpView> views = await AdminRsvpViewsAsync(new List<EventRsvp> { rsvp }, null, token).ConfigureAwait(false);
            return views[0];
        }

        /// <summary>
        /// Throw 403 unless the caller is event staff.
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <exception cref="ApiErrorException">403 Forbidden.</exception>
        public static void RequireStaff(ContentActor actor)
        {
            ArgumentNullException.ThrowIfNull(actor);
            if (!actor.IsEventStaff) throw ApiErrorException.Forbidden("Event staff only (SystemAdmin or EventManager).");
        }

        #endregion

        #region Private-Methods

        private async Task<EventRecord> UpdateOneAsync(ContentActor actor, RequestInfo info, EventRecord e, EventRequest request, HashSet<string> present, CancellationToken token)
        {
            EventEditResult result = await _Services.Events.Editor.ApplyAsync(actor, e, request, present, e.Status == EventStatusEnum.Proposed, token).ConfigureAwait(false);
            EventRecord next = result.Record;
            DateTime now = _Clock();
            if (e.RecurrenceId != null && result.Changes.Any) next.IsException = true;
            await _Services.Database.Events.Records.UpdateAsync(next, result.ChildrenChanged, now, token).ConfigureAwait(false);
            if (e.RecurrenceId != null && result.Changes.Any) await _Services.Events.Recurrence.UpsertExceptionAsync(e.RecurrenceId, e, EventRecurrenceExceptionKindEnum.Edited, token).ConfigureAwait(false);
            if (present.Contains("labels") || present.Contains("tags")) await _Services.Metadata.ApplyAsync(actor, TargetTypeEnum.Event, e.Id, request.Labels, request.Tags, token).ConfigureAwait(false);
            if (result.CoverChanged)
            {
                if (next.CoverMediaId == null) await _Services.Media.DetachAsync(TargetTypeEnum.Event, e.Id, null, token).ConfigureAwait(false);
                else await AttachCoverAsync(actor, e.Id, next.CoverMediaId, token).ConfigureAwait(false);
            }
            if (result.Changes.MoreSpace && next.Status == EventStatusEnum.Published) await _Services.Events.Rsvps.PromoteAfterChangeAsync(next, token).ConfigureAwait(false);
            if (result.Changes.Material && next.Status == EventStatusEnum.Published && (request.NotifyAttendees ?? true))
                await _Services.Events.Notifier.StartEventAsync(NotificationTypeEnum.EventUpdated, next, result.Changes.Flags, token).ConfigureAwait(false);
            await _Services.Forums.Search.EnqueueAsync(TargetTypeEnum.Event, e.Id, token).ConfigureAwait(false);
            await _Services.Audit.WriteAsync(info, "event.update", "Event", e.Id, (result.Changes.Material ? "changed " + EventChangeSet.Describe(result.Changes.Flags) : "edited") + (e.RecurrenceId == null ? String.Empty : " (scope One)"), token).ConfigureAwait(false);
            return await _Services.Database.Events.Records.ReadAsync(e.Id, token).ConfigureAwait(false) ?? next;
        }

        /// <summary>
        /// Keep event cover images on the public media paths exactly while some event signed-out visitors may see (Public,
        /// not moderated, Published or Canceled, not deleted) uses them, so public listings load them without a media
        /// grant; covers of members-only, draft, and moderated events are withdrawn from the public paths. Covers are
        /// decided per media item because the occurrences of a recurring series share one cover.
        /// </summary>
        /// <param name="mediaIds">Cover media identifiers (nulls and duplicates ignored).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        /// <exception cref="ArgumentNullException">The identifiers are null.</exception>
        public async Task SyncCoversAsync(IEnumerable<string?> mediaIds, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(mediaIds);
            foreach (string id in mediaIds.Where(i => !String.IsNullOrEmpty(i)).Select(i => i!).Distinct(StringComparer.Ordinal))
            {
                bool shouldBePublic = await _Services.Database.Events.Records.CountPublicWithCoverAsync(id, token).ConfigureAwait(false) > 0;
                await _Services.Media.SyncMediaPublicAsync(id, shouldBePublic, token).ConfigureAwait(false);
            }
        }

        private async Task<List<MapFeatureView>> MapPointsAsync(ContentActor actor, List<EventMapPoint> points, CancellationToken token)
        {
            List<EventMapPoint> visible = points.Where(p => EventAccess.Read(p.Event, actor) != EventReadEnum.Hidden && p.Event.Lat != null && p.Event.Lng != null).ToList();
            List<MapFeatureView> result = new List<MapFeatureView>();
            if (visible.Count == 0) return result;
            List<EventSummaryView> summaries = await _Services.Events.Views.SummariesAsync(actor, visible.Select(p => p.Event).ToList(), token).ConfigureAwait(false);
            for (int i = 0; i < visible.Count; i++)
            {
                EventRecord e = visible[i].Event;
                EventSummaryView s = summaries[i];
                bool exact = visible[i].Visibility == "Exact";
                int places = exact ? 5 : 2;
                result.Add(new MapFeatureView
                {
                    CreatedUtc = e.CreatedUtc,
                    Kind = MapFeatureKindEnum.Point,
                    Event = new MapEventView
                    {
                        Id = e.Id,
                        Slug = e.Slug,
                        Title = s.Title,
                        StartUtc = s.StartUtc,
                        EndUtc = s.EndUtc,
                        TimeZone = s.TimeZone,
                        StartLocal = s.StartLocal,
                        IsAllDay = s.IsAllDay,
                        Format = s.Format,
                        Status = s.Status,
                        Category = s.Category,
                        LocationName = s.Location?.Name,
                        Locality = s.Location?.Address?.Locality,
                        Region = s.Location?.Address?.Region,
                        CountryCode = s.Location?.Address?.CountryCode,
                        Venue = s.Location?.Venue,
                        Geo = new EventGeoView { Lat = Math.Round(e.Lat!.Value, places), Lng = Math.Round(e.Lng!.Value, places), Precision = exact ? "Exact" : "Approximate" },
                        SpotsLeft = s.SpotsLeft,
                        Locked = s.Locked,
                        CoverThumbUrl = MapFeatureBuilder.ThumbUrl(s.Cover)
                    }
                });
            }
            return result;
        }

        private async Task AttachCoverAsync(ContentActor actor, string targetId, string mediaId, CancellationToken token)
        {
            List<TrellisKit.Core.Models.MediaAttachment> media = await _Services.Media.ValidateAttachableAsync(actor, new List<string> { mediaId }, 1, 0, TargetTypeEnum.Event, targetId, token).ConfigureAwait(false);
            if (media.Count > 0) await _Services.Media.AttachAsync(TargetTypeEnum.Event, targetId, new List<string> { mediaId }, token).ConfigureAwait(false);
        }

        private async Task MentionsAsync(EventRecord e, CancellationToken token)
        {
            string? author = e.CreatedByUserId ?? e.HostUserIds.FirstOrDefault();
            if (String.IsNullOrEmpty(author) || MarkdownNormalizer.MentionHandles(e.Description).Count == 0) return;
            await _Services.Mentions.StoreAsync(e.Description, TargetTypeEnum.Event, e.Id, author, token).ConfigureAwait(false);
            List<NotificationDraft> drafts = await _Services.Mentions.ClaimDraftsAsync(TargetTypeEnum.Event, e.Id, author, null, MarkdownNormalizer.Excerpt(e.Description), token).ConfigureAwait(false);
            foreach (NotificationDraft draft in drafts)
            {
                draft.Path = "/events/" + e.Id;
                draft.SubjectNoun = "event";
            }
            if (drafts.Count > 0) await _Services.Notifications.CreateForSourceAsync(drafts, token).ConfigureAwait(false);
        }

        private async Task<List<AdminRsvpView>> AdminRsvpViewsAsync(List<EventRsvp> rows, ContentActor? actor, CancellationToken token)
        {
            Dictionary<string, UserSummary> people = await _Services.Summaries.BuildAsync(rows.Select(r => r.UserId), token).ConfigureAwait(false);
            List<AdminRsvpView> views = rows.Select(r => new AdminRsvpView
            {
                Id = r.Id,
                EventId = r.EventId,
                User = people.TryGetValue(r.UserId, out UserSummary? u) ? u : UserSummary.Deleted(),
                Status = r.Status,
                GuestCount = r.GuestCount,
                Position = r.Position,
                HideFromAttendeeLists = r.HideFromAttendeeLists,
                WaitlistedUtc = r.WaitlistedUtc,
                PromotedUtc = r.PromotedUtc,
                CheckedInUtc = r.CheckedInUtc,
                CheckedInByUserId = r.CheckedInByUserId,
                CreatedUtc = r.CreatedUtc,
                UpdatedUtc = r.UpdatedUtc
            }).ToList();
            await _Services.Metadata.AttachAsync(TargetTypeEnum.EventRsvp, views, v => v.Id, actor?.IsStaffFor(TargetTypeEnum.EventRsvp) ?? true, token).ConfigureAwait(false);
            return views;
        }

        private async Task<EventRecord?> LoadAsync(string idOrSlug, CancellationToken token)
        {
            if (String.IsNullOrWhiteSpace(idOrSlug)) return null;
            string key = idOrSlug.Trim();
            EventRecord? e = key.StartsWith(TrellisKit.Core.Helpers.EventIdPrefixes.Event, StringComparison.Ordinal) ? await _Services.Database.Events.Records.ReadAsync(key, token).ConfigureAwait(false) : null;
            return e ?? await _Services.Database.Events.Records.ReadBySlugAsync(key, token).ConfigureAwait(false);
        }

        private async Task<EventRecord> RequireReadableAsync(ContentActor actor, string id, CancellationToken token)
        {
            EventRecord? e = await LoadAsync(id, token).ConfigureAwait(false);
            if (e == null || EventAccess.Read(e, actor) != EventReadEnum.Open) throw ApiErrorException.NotFound("Event");
            return e;
        }

        private void RequirePublic()
        {
            if (!_Services.Settings.Events.PublicListingsEnabled) throw new ApiErrorException(404, "NotAvailable", "Public events are turned off for this property.");
        }

        private EnumerationRequest Prepare(EnumerationQuery q, EnumerationCollection collection, ContentActor actor, EventListFilter filter, string scope = "")
        {
            EnumerationRequest request = _Services.Enumeration.Prepare(q, collection, actor.IsStaffFor(TargetTypeEnum.Event), scope);
            if (String.Equals(request.Ordering.Name, "DistanceAscending", StringComparison.Ordinal))
            {
                if (filter.NearLat == null) throw ApiErrorException.Field("ordering", "DistanceAscending needs filters.near.");
                filter.ByDistance = true;
            }
            if (String.Equals(request.Ordering.Name, "Relevance", StringComparison.Ordinal) && String.IsNullOrWhiteSpace(q.Search)) throw ApiErrorException.Field("ordering", "Relevance needs search.");
            return request;
        }

        private EventListFilter Filter(ContentActor actor, EnumerationQuery q, bool admin)
        {
            Dictionary<string, string> errors = new Dictionary<string, string>();
            EventListFilter f = new EventListFilter
            {
                NowUtc = _Clock(),
                When = q.FilterEnum<EventWhenEnum>("when", errors) ?? EventWhenEnum.Upcoming,
                StartsAfterUtc = q.FilterUtc("startsAfterUtc", errors),
                StartsBeforeUtc = q.FilterUtc("startsBeforeUtc", errors),
                Categories = ForumFilters.List(q, "category") ?? new List<string>(),
                Formats = ForumFilters.Enums<EventFormatEnum>(q, "format", errors),
                VenueId = q.FilterString("venueId"),
                HostUserId = q.FilterString("hostUserId"),
                InstructorId = q.FilterString("instructorId"),
                RecurrenceId = q.FilterString("recurrenceId"),
                HasSpots = q.FilterBool("hasSpots", errors) == true,
                IncludeCanceled = q.FilterBool("includeCanceled", errors) == true,
                IncludeOnline = q.FilterBool("includeOnline", errors) == true,
                Statuses = ForumFilters.Enums<EventStatusEnum>(q, "status", errors).Where(s => s != EventStatusEnum.Template).ToList()
            };
            if (!admin && !String.IsNullOrEmpty(actor.UserId) && !actor.IsAnonymous) RsvpStatuses(q, f, actor.UserId, errors);
            Near(q, f);
            f.Box = DirectoryFilters.Box(q);
            if (f.Box != null && f.NearLat != null) throw DirectoryFilters.InvalidGeo("filters.bbox", "near and bbox cannot be combined.");
            RouteHelperErrors(errors);
            return f;
        }

        private static void RsvpStatuses(EnumerationQuery q, EventListFilter f, string userId, Dictionary<string, string> errors)
        {
            List<string>? raw = ForumFilters.List(q, "rsvpStatus");
            if (raw == null) return;
            f.RsvpUserId = userId;
            foreach (string s in raw)
            {
                if (String.Equals(s, "None", StringComparison.OrdinalIgnoreCase)) f.RsvpNone = true;
                else if (Enum.TryParse(s, true, out EventRsvpStatusEnum status) && Enum.IsDefined(status)) f.RsvpStatuses.Add(status);
                else errors["filters.rsvpStatus"] = "Use Going, Interested, Waitlisted, or None.";
            }
        }

        private static void Near(EnumerationQuery q, EventListFilter f)
        {
            JsonElement? near = null;
            foreach (KeyValuePair<string, JsonElement> pair in q.Filters)
                if (String.Equals(pair.Key, "near", StringComparison.OrdinalIgnoreCase)) near = pair.Value;
            if (near == null || near.Value.ValueKind == JsonValueKind.Null) return;
            double? lat = null;
            double? lng = null;
            double? radius = null;
            JsonElement n = near.Value;
            if (n.ValueKind == JsonValueKind.Object)
            {
                lat = Number(n, "lat");
                lng = Number(n, "lng");
                radius = Number(n, "radiusKm");
            }
            else if (n.ValueKind == JsonValueKind.String)
            {
                string[] parts = (n.GetString() ?? String.Empty).Split(',', StringSplitOptions.TrimEntries);
                if (parts.Length >= 2 && Double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out double a) && Double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double b))
                {
                    lat = a;
                    lng = b;
                    if (parts.Length >= 3 && Double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out double c)) radius = c;
                }
            }
            double r = radius ?? DefaultRadiusKm;
            if (lat == null || lng == null || lat.Value < -90 || lat.Value > 90 || lng.Value < -180 || lng.Value > 180 || r <= 0 || r > MaxRadiusKm)
                throw new ApiErrorException(400, "InvalidGeo", "near needs lat (-90 to 90), lng (-180 to 180), and radiusKm (up to " + MaxRadiusKm.ToString(CultureInfo.InvariantCulture) + ").",
                    new Dictionary<string, string> { { "filters.near", "Invalid position or radius." } });
            f.NearLat = lat;
            f.NearLng = lng;
            f.NearRadiusKm = r;
        }

        private static double? Number(JsonElement e, string name)
        {
            foreach (JsonProperty p in e.EnumerateObject())
            {
                if (!String.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)) continue;
                if (p.Value.ValueKind == JsonValueKind.Number && p.Value.TryGetDouble(out double d)) return d;
                if (p.Value.ValueKind == JsonValueKind.String && Double.TryParse(p.Value.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out double s)) return s;
            }
            return null;
        }

        private static List<ContentAudienceEnum>? Narrow(List<ContentAudienceEnum>? allowed, EnumerationQuery q)
        {
            Dictionary<string, string> errors = new Dictionary<string, string>();
            List<ContentAudienceEnum> requested = ForumFilters.Enums<ContentAudienceEnum>(q, "audience", errors);
            RouteHelperErrors(errors);
            if (requested.Count == 0) return allowed;
            return allowed == null ? requested : allowed.Where(requested.Contains).ToList();
        }

        private static void RouteHelperErrors(Dictionary<string, string> errors)
        {
            if (errors.Count > 0) throw ApiErrorException.Validation(errors);
        }

        #endregion
    }
}
