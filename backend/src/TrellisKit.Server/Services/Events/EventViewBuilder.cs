namespace TrellisKit.Server.Services
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Models;
    using TrellisKit.Core.Responses;

    /// <summary>
    /// Builds event payloads for a caller (docs/PHASE4_CONTRACT.md section 2.5): summaries for lists, the detail
    /// (hosts, instructors, attendee preview), the staff view, and the anonymous public view. Every page loads its
    /// categories, venues, recurrences, the caller's RSVPs, covers, and metadata in a bounded set of queries.
    /// Thread safety: stateless apart from the shared services.
    /// </summary>
    public class EventViewBuilder
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
        public EventViewBuilder(ApplicationServices services)
        {
            _Services = services ?? throw new ArgumentNullException(nameof(services));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Summaries of a page of events the caller may list (locked stubs where the read decision is Locked).
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="rows">Events.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Summaries in the same order.</returns>
        public async Task<List<EventSummaryView>> SummariesAsync(ContentActor actor, List<EventRecord> rows, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            ArgumentNullException.ThrowIfNull(rows);
            EventPageData data = await LoadAsync(actor, rows, token).ConfigureAwait(false);
            List<EventSummaryView> views = new List<EventSummaryView>();
            foreach (EventRecord e in rows)
            {
                EventSummaryView view = new EventSummaryView();
                Fill(view, e, actor, data, EventAccess.Read(e, actor) == EventReadEnum.Locked);
                views.Add(view);
            }
            await _Services.Metadata.AttachAsync(TargetTypeEnum.Event, views, v => v.Id, actor.IsStaffFor(TargetTypeEnum.Event), token).ConfigureAwait(false);
            return views;
        }

        /// <summary>
        /// The detail payload of one event.
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="e">Event.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Event.</returns>
        public async Task<EventView> DetailAsync(ContentActor actor, EventRecord e, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            ArgumentNullException.ThrowIfNull(e);
            EventView view = new EventView();
            await FillDetailAsync(view, actor, e, token).ConfigureAwait(false);
            return view;
        }

        /// <summary>
        /// The staff payload of one event (AdminEvent).
        /// </summary>
        /// <param name="actor">Caller (event staff or a host).</param>
        /// <param name="e">Event.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Event.</returns>
        public async Task<AdminEventView> AdminAsync(ContentActor actor, EventRecord e, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            ArgumentNullException.ThrowIfNull(e);
            AdminEventView view = new AdminEventView();
            await FillDetailAsync(view, actor, e, token).ConfigureAwait(false);
            await FillAdminAsync(view, actor, e, token).ConfigureAwait(false);
            return view;
        }

        /// <summary>
        /// Staff payloads of a page (without hosts and attendee previews per row beyond the summary data).
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="rows">Events.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Payloads in order.</returns>
        public async Task<List<AdminEventView>> AdminPageAsync(ContentActor actor, List<EventRecord> rows, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            ArgumentNullException.ThrowIfNull(rows);
            EventPageData data = await LoadAsync(actor, rows, token).ConfigureAwait(false);
            Dictionary<string, UserSummary> hosts = await _Services.Summaries.BuildAsync(rows.SelectMany(r => r.HostUserIds).Distinct(StringComparer.Ordinal), token).ConfigureAwait(false);
            List<AdminEventView> views = new List<AdminEventView>();
            foreach (EventRecord e in rows)
            {
                AdminEventView view = new AdminEventView();
                Fill(view, e, actor, data, false);
                view.Description = e.Description;
                view.MaxGuestsPerRsvp = e.MaxGuestsPerRsvp;
                view.Hosts = e.HostUserIds.Where(hosts.ContainsKey).Select(h => hosts[h]).ToList();
                await FillAdminAsync(view, actor, e, token).ConfigureAwait(false);
                views.Add(view);
            }
            await _Services.Metadata.AttachAsync(TargetTypeEnum.Event, views, v => v.Id, actor.IsStaffFor(TargetTypeEnum.Event), token).ConfigureAwait(false);
            return views;
        }

        /// <summary>
        /// Anonymous public payloads (docs/PHASE4_CONTRACT.md section 4.9).
        /// </summary>
        /// <param name="actor">Anonymous caller.</param>
        /// <param name="rows">Events.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Payloads in order.</returns>
        public async Task<List<PublicEventView>> PublicAsync(ContentActor actor, List<EventRecord> rows, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            ArgumentNullException.ThrowIfNull(rows);
            EventPageData data = await LoadAsync(actor, rows, token).ConfigureAwait(false);
            List<PublicEventView> views = new List<PublicEventView>();
            foreach (EventRecord e in rows)
            {
                EventSummaryView s = new EventSummaryView();
                Fill(s, e, actor, data, false);
                views.Add(new PublicEventView
                {
                    Id = e.Id,
                    Title = e.Title,
                    Slug = e.Slug,
                    PublicUrl = _Services.PublicPages?.EventUrl(e),
                    Summary = e.Summary,
                    Description = e.Description,
                    Category = s.Category,
                    Format = e.Format,
                    StartUtc = e.StartUtc,
                    EndUtc = e.EndUtc,
                    TimeZone = e.TimeZone,
                    IsAllDay = e.IsAllDay,
                    Location = s.Location,
                    OnlinePlatformLabel = e.Format == EventFormatEnum.InPerson ? null : e.OnlinePlatformLabel,
                    Capacity = e.Capacity,
                    GoingCount = e.GoingCount,
                    SpotsLeft = s.SpotsLeft,
                    Cover = s.Cover,
                    Audience = e.Audience,
                    Recurrence = s.Recurrence,
                    PublishedUtc = e.PublishedUtc
                });
            }
            await _Services.Metadata.AttachAsync(TargetTypeEnum.Event, views, v => v.Id, false, token).ConfigureAwait(false);
            return views;
        }

        /// <summary>
        /// The location as the caller may see it.
        /// </summary>
        /// <param name="e">Event.</param>
        /// <param name="venue">Venue snapshot, or null.</param>
        /// <param name="actor">Caller.</param>
        /// <returns>Location or null (Online events).</returns>
        public static EventLocationView? Location(EventRecord e, EventVenueSnapshot? venue, ContentActor actor)
        {
            if (e.Format == EventFormatEnum.Online) return null;
            bool staff = EventAccess.IsStaffOrHost(e, actor);
            string visibility = e.AddressVisibility;
            bool venueReadable = venue != null && (staff || (venue.IsListed && ContentAudienceRule.Meets(venue.Audience, actor)));
            if (venue != null && !venueReadable) visibility = "LocalityOnly";
            if (staff) visibility = "Exact";
            EventLocationView view = new EventLocationView
            {
                Venue = venueReadable ? new EventVenueView { Id = venue!.Id, Slug = venue.Slug, Name = venue.Name, Locality = venue.Locality, Region = venue.Region, CountryCode = venue.CountryCode } : null,
                Name = venue != null && !venueReadable ? null : e.LocationName ?? (venueReadable ? venue!.Name : null),
                AddressVisibility = e.AddressVisibility
            };
            bool hasAddress = e.Line1 != null || e.Locality != null || e.Region != null || e.CountryCode != null;
            if (hasAddress)
            {
                bool exact = visibility == "Exact";
                EventAddressView address = new EventAddressView
                {
                    Line1 = exact ? e.Line1 : null,
                    Line2 = exact ? e.Line2 : null,
                    Locality = e.Locality,
                    Region = e.Region,
                    PostalCode = exact ? e.PostalCode : null,
                    CountryCode = e.CountryCode
                };
                address.Formatted = Formatted(address);
                view.Address = address;
            }
            if (e.Lat != null && e.Lng != null && visibility != "LocalityOnly")
            {
                int places = visibility == "Exact" ? 5 : 2;
                view.Geo = new EventGeoView { Lat = Math.Round(e.Lat.Value, places), Lng = Math.Round(e.Lng.Value, places), Precision = visibility == "Exact" ? "Exact" : "Approximate" };
            }
            return view;
        }

        /// <summary>
        /// One line of a location for calendars and emails (respecting address visibility for members).
        /// </summary>
        /// <param name="location">Location view.</param>
        /// <returns>Text, or empty.</returns>
        public static string LocationText(EventLocationView? location)
        {
            if (location == null) return String.Empty;
            List<string> parts = new List<string>();
            if (!String.IsNullOrWhiteSpace(location.Name)) parts.Add(location.Name.Trim());
            if (location.Address != null && !String.IsNullOrWhiteSpace(location.Address.Formatted)) parts.Add(location.Address.Formatted);
            return String.Join(", ", parts);
        }

        /// <summary>
        /// The rule view of a recurrence.
        /// </summary>
        /// <param name="r">Recurrence.</param>
        /// <returns>View.</returns>
        public static EventRecurrenceRuleView RuleView(EventRecurrence r)
        {
            ArgumentNullException.ThrowIfNull(r);
            return new EventRecurrenceRuleView
            {
                Frequency = r.Frequency,
                Interval = r.Interval,
                Weekdays = r.Weekdays.Select(RecurrenceService.WeekdayName).ToList(),
                MonthDay = r.MonthDay,
                SetPosition = r.SetPosition,
                Count = r.Count,
                UntilDate = r.UntilLocalDate
            };
        }

        #endregion

        #region Private-Methods

        private async Task FillDetailAsync(EventView view, ContentActor actor, EventRecord e, CancellationToken token)
        {
            List<EventRecord> one = new List<EventRecord> { e };
            EventPageData data = await LoadAsync(actor, one, token).ConfigureAwait(false);
            bool locked = EventAccess.Read(e, actor) == EventReadEnum.Locked;
            Fill(view, e, actor, data, locked);
            view.MaxGuestsPerRsvp = e.MaxGuestsPerRsvp;
            if (actor.IsEventStaff || (!String.IsNullOrEmpty(actor.UserId) && String.Equals(actor.UserId, e.ProposedByUserId, StringComparison.Ordinal))) view.ProposalNote = e.ProposalNote;
            if (!locked)
            {
                view.Description = e.Description;
                Dictionary<string, UserSummary> hosts = await _Services.Summaries.BuildAsync(e.HostUserIds, token).ConfigureAwait(false);
                view.Hosts = e.HostUserIds.Where(hosts.ContainsKey).Select(h => hosts[h]).ToList();
                Dictionary<string, EventInstructorSnapshot> instructors = await _Services.Database.Events.Records.ReadInstructorsAsync(e.InstructorIds, token).ConfigureAwait(false);
                bool staff = EventAccess.IsStaffOrHost(e, actor);
                view.Instructors = e.InstructorIds.Where(instructors.ContainsKey).Select(i => instructors[i])
                    .Where(i => staff || (i.IsListed && ContentAudienceRule.Meets(i.Audience, actor)))
                    .Select(i => new EventInstructorView { Id = i.Id, Slug = i.Slug, DisplayName = i.DisplayName, PhotoMediaId = i.PhotoMediaId }).ToList();
                if (view.Viewer.CanSeeAttendees)
                {
                    int size = 10;
                    List<string> preview = await _Services.Database.Events.Rsvps.PreviewAsync(e.Id, staff, size, token).ConfigureAwait(false);
                    Dictionary<string, UserSummary> people = await _Services.Summaries.BuildAsync(preview, token).ConfigureAwait(false);
                    view.AttendeePreview = new EventAttendeePreviewView
                    {
                        Objects = preview.Where(people.ContainsKey).Select(p => people[p]).ToList(),
                        TotalRecords = await _Services.Database.Events.Rsvps.CountGoingAsync(e.Id, staff, token).ConfigureAwait(false),
                        Enumerate = TrellisKit.Core.Constants.ApiPrefix + "/events/" + e.Id + "/attendees/enumerate"
                    };
                }
            }
            await _Services.Metadata.AttachOneAsync(TargetTypeEnum.Event, view, e.Id, actor.IsStaffFor(TargetTypeEnum.Event), token).ConfigureAwait(false);
        }

        private async Task FillAdminAsync(AdminEventView view, ContentActor actor, EventRecord e, CancellationToken token)
        {
            view.OpenReportCount = e.OpenReportCount;
            view.CheckedInCount = e.CheckedInCount;
            view.GuestCount = e.GuestCount;
            view.OnlineJoinUrl = e.OnlineJoinUrl;
            view.VenueId = e.VenueId;
            view.HostUserIds = new List<string>(e.HostUserIds);
            view.InstructorIds = new List<string>(e.InstructorIds);
            view.ProposalNote = e.ProposalNote;
            view.ReviewNote = e.ReviewNote;
            view.ReviewedUtc = e.ReviewedUtc;
            view.ModerationState = e.ModerationState;
            view.IsHost = !actor.IsEventStaff && EventAccess.IsHost(e, actor);
            if (e.ProposedByUserId != null) view.ProposedBy = await _Services.Summaries.BuildOneAsync(e.ProposedByUserId, token).ConfigureAwait(false);
            List<EventReminder> reminders = await _Services.Database.Events.Reminders.ListForEventAsync(e.Id, token).ConfigureAwait(false);
            view.ReminderStatus = reminders.Select(r => new EventReminderStatusView { OffsetMinutes = r.OffsetMinutes, DueUtc = r.DueUtc, SentUtc = r.SentUtc, SkippedUtc = r.SkippedUtc }).ToList();
            if (view.Online != null) view.Online.JoinUrl = e.OnlineJoinUrl;
        }

        private void Fill(EventSummaryView view, EventRecord e, ContentActor actor, EventPageData data, bool locked)
        {
            DateTime now = _Clock();
            TimeZoneInfo zone = EventTimeZones.Find(e.TimeZone) ?? TimeZoneInfo.Utc;
            data.MyRsvps.TryGetValue(e.Id, out EventRsvp? mine);
            bool staffOrHost = EventAccess.IsStaffOrHost(e, actor);
            view.Id = e.Id;
            view.Title = e.Title;
            view.Slug = e.Slug;
            view.PublicUrl = _Services.PublicPages?.EventUrl(e);
            view.Summary = e.Summary;
            if (!String.IsNullOrEmpty(e.CategoryKey) && data.Categories.TryGetValue(e.CategoryKey.ToLowerInvariant(), out EventCategory? category))
                view.Category = new PublicOption { Key = category.Key, Label = category.Label, Color = category.Color };
            else if (!String.IsNullOrEmpty(e.CategoryKey)) view.Category = new PublicOption { Key = e.CategoryKey, Label = e.CategoryKey };
            view.Format = e.Format;
            view.Status = e.Status;
            view.StartUtc = e.StartUtc;
            view.EndUtc = e.EndUtc;
            view.TimeZone = e.TimeZone;
            view.IsAllDay = e.IsAllDay;
            view.StartLocal = EventTimeZones.Format(EventTimeZones.ToLocal(e.StartUtc, zone));
            view.EndLocal = EventTimeZones.Format(EventTimeZones.ToLocal(e.EndUtc, zone));
            view.IsPast = e.EndUtc <= now;
            data.Venues.TryGetValue(e.VenueId ?? String.Empty, out EventVenueSnapshot? venue);
            view.Location = Location(e, venue, actor);
            if (e.Format != EventFormatEnum.InPerson && !locked)
            {
                DateTime available = EventAccess.JoinUrlAvailableUtc(e, _Services.Settings.Events.JoinUrlRevealMinutes);
                bool going = mine != null && mine.Status == EventRsvpStatusEnum.Going;
                bool reveal = (going || staffOrHost) && now >= available && e.Status == EventStatusEnum.Published && view is EventView;
                view.Online = new EventOnlineView { PlatformLabel = e.OnlinePlatformLabel, JoinUrl = reveal ? e.OnlineJoinUrl : null, JoinUrlAvailableUtc = available };
            }
            view.Capacity = e.Capacity;
            view.GoingCount = e.GoingCount;
            view.InterestedCount = e.InterestedCount;
            view.WaitlistCount = e.WaitlistCount;
            view.SpotsLeft = e.Capacity == null ? null : Math.Max(0, e.Capacity.Value - e.GoingCount - e.GuestCount);
            view.WaitlistEnabled = e.WaitlistEnabled;
            view.RsvpAudience = e.RsvpAudience;
            view.RsvpOpensUtc = e.RsvpOpensUtc;
            view.RsvpClosesUtc = e.RsvpClosesUtc;
            view.AttendeeVisibility = e.AttendeeVisibility;
            view.ExternalUrl = e.ExternalUrl;
            if (!String.IsNullOrEmpty(e.CoverMediaId) && data.Covers.TryGetValue(e.CoverMediaId, out MediaView? cover)) view.Cover = cover;
            view.HostCount = e.HostUserIds.Count;
            if (!locked && mine != null)
            {
                view.MyRsvp = new EventMyRsvpView
                {
                    Id = mine.Id,
                    Status = mine.Status,
                    GuestCount = mine.GuestCount,
                    Position = mine.Position,
                    HideFromAttendeeLists = mine.HideFromAttendeeLists,
                    CheckedInUtc = mine.CheckedInUtc,
                    EventCanceled = e.Status == EventStatusEnum.Canceled,
                    UpdatedUtc = mine.UpdatedUtc
                };
            }
            view.ReminderOffsetsMinutes = new List<int>(e.ReminderOffsetsMinutes);
            view.CommentsEnabled = e.CommentsEnabled;
            view.CommentTarget = new ContentTarget { Type = TargetTypeEnum.Event.ToString(), Id = e.Id };
            view.CommentCount = e.CommentCount;
            view.ReactionTotal = e.ReactionTotal;
            if (e.PromotedFeedPostId != null) view.Promotion = new TargetPromotionView { TargetType = TargetTypeEnum.Event.ToString(), TargetId = e.Id, FeedPostId = e.PromotedFeedPostId };
            view.Audience = e.Audience;
            view.CanceledUtc = e.CanceledUtc;
            view.CancelReason = e.CancelReason;
            view.Locked = locked;
            if (e.RecurrenceId != null && data.Recurrences.TryGetValue(e.RecurrenceId, out EventRecurrence? recurrence))
            {
                EventRecurrenceRule rule = RecurrenceService.RuleOf(recurrence);
                DateTime anchor = EventTimeZones.TryParseLocal(recurrence.StartLocal, out DateTime a) ? a : EventTimeZones.ToLocal(e.StartUtc, zone);
                view.Recurrence = new EventRecurrenceView
                {
                    Id = recurrence.Id,
                    Rule = RuleView(recurrence),
                    Summary = RecurrenceSummary.Describe(rule, anchor),
                    OccurrenceIndex = e.OccurrenceIndex,
                    IsException = e.IsException,
                    OriginalStartUtc = e.OriginalStartUtc
                };
            }
            view.PublishedUtc = e.PublishedUtc;
            view.CreatedUtc = e.CreatedUtc;
            view.UpdatedUtc = e.UpdatedUtc;
            view.Viewer = Viewer(e, actor, mine, locked, now);
        }

        private EventViewerView Viewer(EventRecord e, ContentActor actor, EventRsvp? mine, bool locked, DateTime now)
        {
            if (locked) return new EventViewerView { CanOpen = false, Requires = "Membership" };
            bool signedIn = !actor.IsAnonymous && !String.IsNullOrEmpty(actor.UserId);
            bool staff = actor.IsEventStaff;
            bool host = EventAccess.IsHost(e, actor);
            bool mayRsvp = EventAccess.MayRsvp(e, actor);
            bool open = EventAccess.RsvpOpen(e, now);
            bool canRsvp = signedIn && mayRsvp && open && (!actor.IsMuted || actor.IsSystemAdmin);
            int mineParty = mine != null && mine.Status == EventRsvpStatusEnum.Going ? 1 + mine.GuestCount : 0;
            int? spots = e.Capacity == null ? null : e.Capacity.Value - e.GoingCount - e.GuestCount + mineParty;
            bool full = spots != null && spots.Value < 1;
            bool live = e.Status == EventStatusEnum.Published && e.ModerationState == ModerationStateEnum.None;
            bool participate = EventAccess.MayParticipate(e, actor);
            string? requires = null;
            if (!signedIn) requires = "SignIn";
            else if (!mayRsvp && actor.Tier < AccessTierEnum.Member) requires = "Membership";
            return new EventViewerView
            {
                CanRsvp = canRsvp,
                CanRsvpGoing = canRsvp && (!full || e.WaitlistEnabled),
                CanJoinWaitlist = canRsvp && full && e.WaitlistEnabled,
                CanComment = live && e.CommentsEnabled && participate && actor.CanWrite,
                CanReact = (live || e.Status == EventStatusEnum.Canceled) && participate && (!actor.IsMuted || actor.IsSystemAdmin),
                CanReport = signedIn,
                CanSeeAttendees = EventAccess.CanSeeAttendees(e, actor, mine),
                CanEdit = staff || host,
                CanCancel = staff && (e.Status == EventStatusEnum.Published || e.Status == EventStatusEnum.Draft),
                CanCheckIn = staff || host,
                CanPromote = live && e.PromotedFeedPostId == null && (actor.IsSystemAdmin || actor.CanPromoteToFeed),
                CanOpen = true,
                Requires = requires
            };
        }

        private async Task<EventPageData> LoadAsync(ContentActor actor, List<EventRecord> rows, CancellationToken token)
        {
            EventPageData data = new EventPageData();
            if (rows.Count == 0) return data;
            foreach (EventCategory c in await _Services.Database.Events.Categories.ListAllAsync(token).ConfigureAwait(false)) data.Categories[c.Key.ToLowerInvariant()] = c;
            data.Venues = await _Services.Database.Events.Records.ReadVenuesAsync(rows.Where(r => r.VenueId != null).Select(r => r.VenueId!), token).ConfigureAwait(false);
            data.Recurrences = await _Services.Database.Events.Recurrences.ReadManyAsync(rows.Where(r => r.RecurrenceId != null).Select(r => r.RecurrenceId!), token).ConfigureAwait(false);
            if (!actor.IsAnonymous && !String.IsNullOrEmpty(actor.UserId))
                data.MyRsvps = await _Services.Database.Events.Rsvps.ReadForUserAsync(actor.UserId, rows.Select(r => r.Id), token).ConfigureAwait(false);
            List<string> covers = rows.Where(r => !String.IsNullOrEmpty(r.CoverMediaId)).Select(r => r.CoverMediaId!).Distinct(StringComparer.Ordinal).ToList();
            if (covers.Count > 0)
            {
                foreach (MediaAttachment media in await _Services.Database.Media.ReadManyAsync(covers, token).ConfigureAwait(false))
                {
                    if (media.IsDeleted) continue;
                    data.Covers[media.Id] = await _Services.Media.ViewAsync(media, actor, token).ConfigureAwait(false);
                }
            }
            return data;
        }

        private static string Formatted(EventAddressView a)
        {
            List<string> parts = new List<string>();
            if (!String.IsNullOrWhiteSpace(a.Line1)) parts.Add(a.Line1.Trim());
            if (!String.IsNullOrWhiteSpace(a.Line2)) parts.Add(a.Line2.Trim());
            string place = String.Join(", ", new[] { a.Locality, a.Region }.Where(s => !String.IsNullOrWhiteSpace(s)).Select(s => s!.Trim()));
            if (!String.IsNullOrWhiteSpace(a.PostalCode)) place = (place + " " + a.PostalCode.Trim()).Trim();
            if (place.Length > 0) parts.Add(place);
            if (parts.Count == 0 && !String.IsNullOrWhiteSpace(a.CountryCode)) parts.Add(a.CountryCode.Trim());
            return String.Join(", ", parts);
        }

        #endregion
    }
}
