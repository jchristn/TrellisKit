namespace Test.Shared.Suites
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Net;
    using System.Net.Http;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Touchstone.Core;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Models;

    /// <summary>
    /// Phase 4 events (docs/PHASE4_CONTRACT.md sections 2.5, 4.1, 4.6, 4.7): create, read, publish, schedule
    /// validation with DST gaps and overlaps, formats and join URL reveal, cancel, duplicate, delete drafts, hosts,
    /// categories, change notices, metrics, and the CSV export.
    /// </summary>
    public static class EventSuite
    {
        #region Public-Members

        /// <summary>
        /// Suite identifier.
        /// </summary>
        public const string Id = "Events";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the suite.
        /// </summary>
        /// <returns>Suite.</returns>
        public static TestSuiteDescriptor Build()
        {
            return new TestSuiteDescriptor(Id, "Events (" + TestEnvironment.Provider + ")", new[]
            {
                Case("Events.Lifecycle", "Event staff create a Draft (hidden from members), publish it, and members read it by id and slug and list it; local times round trip; audited", LifecycleAsync),
                Case("Events.Schedule", "Schedule validation: end after start, known IANA zone, maximum duration, spring-forward gaps rejected with a hint, fall-back overlaps resolved to the first instant, all-day dates, RSVP window inside the event, at most 3 reminders, Europe/London", ScheduleAsync),
                Case("Events.Formats", "Online and Hybrid need an https join link; the join URL is revealed only to Going members, staff, and hosts from start minus joinUrlRevealMinutes; the staff view always shows it", FormatsAsync),
                Case("Events.Cancel", "Cancel needs a reason, keeps RSVPs, stops reminders, sends EventCanceled and the Essential email even with Events email off, blocks RSVPs and most edits", CancelAsync),
                Case("Events.DuplicateDelete", "Duplicate copies a Draft without RSVPs at a new start; only drafts can be deleted", DuplicateDeleteAsync),
                Case("Events.Hosts", "EventManager holders manage events; members get 403 on admin routes; hosts edit only description, cover, labels, and tags, list RSVPs, and check people in", HostsAsync),
                CaseWith("Events.Categories", "Categories seed from property.json, appear in GET /api/v1/property, are validated on events, and SystemAdmin creates, edits, orders, and retires them", CategoriesAsync),
                Case("Events.ChangeNotices", "Time changes send EventUpdated to RSVPed members (one unread notice per event), cosmetic edits and notifyAttendees false do not, and SEQUENCE increments", NoticesAsync),
                Case("Events.SingletonOccurrenceEnumeration", "Single events enumerate their only occurrence with normal validation, filters, paging and exact totals", SingletonOccurrenceAsync),
                Case("Events.MetricsAndExport", "Metrics count events and RSVPs by day with check-in rates, and the RSVP CSV export lists attendees", MetricsAsync)
            });
        }

        #endregion

        #region Private-Methods

        private static TestCaseDescriptor Case(string caseId, string name, Func<HttpFixture, CancellationToken, Task> body)
        {
            return SuiteCases.Http(Id, caseId, name, (f, ct) =>
            {
                EventTestKit.Relax(f);
                return body(f, ct);
            });
        }

        private static TestCaseDescriptor CaseWith(string caseId, string name, Func<HttpFixture, CancellationToken, Task> body)
        {
            return SuiteCases.Http(Id, caseId, name, EventTestKit.Categories, (f, ct) =>
            {
                EventTestKit.Relax(f);
                return body(f, ct);
            });
        }

        private static async Task LifecycleAsync(HttpFixture f, CancellationToken ct)
        {
            TestMember admin = await Api.AdminAsync(f, ct).ConfigureAwait(false);
            TestMember member = await SafetyHelpers.MemberAsync(f, ct).ConfigureAwait(false);
            string start = EventTestKit.Local(12, 18, 30);
            JsonElement created = await EventTestKit.CreateAsync(f, admin.AccessToken, EventTestKit.Body("Autumn meetup", start, EventTestKit.Local(12, 21), new { capacity = 40, labels = new[] { "social" } }), ct).ConfigureAwait(false);
            string id = Api.Str(created, "id");
            Check.True(id.StartsWith("evt_", StringComparison.Ordinal), "evt_ id");
            Check.Equal("Draft", Api.Str(created, "status"), "Created as Draft");
            Check.Equal(start, Api.Str(created, "startLocal"), "Local start round trips");
            Check.Equal("autumn-meetup-exampleville-ex", Api.Str(created, "slug"), "Slug from the title, locality, and region");
            ContentTestKit.CheckMetadata(created, "event");
            Check.True(created.GetProperty("labels").EnumerateArray().Any(l => l.GetString() == "social"), "Labels stored");
            await Api.ExpectErrorAsync(f, HttpMethod.Get, "/api/v1/events/" + id, member.AccessToken, null, HttpStatusCode.NotFound, "NotFound", ct).ConfigureAwait(false);
            JsonElement before = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/events/enumerate", member.AccessToken, new { }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.True(!EventTestKit.Ids(before).Contains(id), "Drafts are not listed");
            JsonElement published = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/admin/events/" + id + "/publish", admin.AccessToken, new { }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal("Published", Api.Str(published, "status"), "Published");
            JsonElement read = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/events/" + id, member.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.True(read.GetProperty("viewer").GetProperty("canRsvp").GetBoolean(), "Members may RSVP");
            Check.Equal(40, read.GetProperty("spotsLeft").GetInt32(), "spotsLeft");
            Check.Equal("Event", Api.Str(read.GetProperty("commentTarget"), "type"), "commentTarget");
            Check.Equal("Example Hall", Api.Str(read.GetProperty("location"), "name"), "Location name");
            JsonElement bySlug = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/events/autumn-meetup-exampleville-ex", member.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal(id, Api.Str(bySlug, "id"), "Read by slug");
            JsonElement list = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/events", member.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.True(EventTestKit.Ids(list).Contains(id), "Listed after publish");
            JsonElement past = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/events/enumerate", member.AccessToken, new { filters = new { when = "Past" } }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.True(!EventTestKit.Ids(past).Contains(id), "Not past");
            JsonElement labeled = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/events/enumerate", member.AccessToken, new { labels = new[] { "nope" } }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.True(!EventTestKit.Ids(labeled).Contains(id), "Label filter applies");
            JsonElement admins = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/admin/events/enumerate", admin.AccessToken, new { }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.True(EventTestKit.Ids(admins).Contains(id), "Admin list");
            Check.True(await ModerationTestKit.AuditCountAsync(f, "event.create", id, ct).ConfigureAwait(false) >= 1, "event.create audited");
            Check.True(await ModerationTestKit.AuditCountAsync(f, "event.publish", id, ct).ConfigureAwait(false) >= 1, "event.publish audited");
            JsonElement second = await EventTestKit.CreateAsync(f, admin.AccessToken, EventTestKit.Body("Autumn meetup", EventTestKit.Local(13, 18), EventTestKit.Local(13, 20)), ct).ConfigureAwait(false);
            Check.Equal("autumn-meetup-exampleville-ex-2", Api.Str(second, "slug"), "Slugs stay unique");
        }

        private static async Task ScheduleAsync(HttpFixture f, CancellationToken ct)
        {
            TestMember admin = await Api.AdminAsync(f, ct).ConfigureAwait(false);
            string a = EventTestKit.Local(5, 18);
            await ExpectInvalidAsync(f, admin, EventTestKit.Body("Backwards", a, EventTestKit.Local(5, 17)), "endLocal", ct).ConfigureAwait(false);
            await ExpectInvalidAsync(f, admin, EventTestKit.Body("No zone", a, EventTestKit.Local(5, 20), null, "Mars/Olympus"), "timeZone", ct).ConfigureAwait(false);
            await ExpectInvalidAsync(f, admin, EventTestKit.Body("Too long", a, EventTestKit.Local(25, 20)), "endLocal", ct).ConfigureAwait(false);
            ApiResult gap = await Api.SendAsync(f, HttpMethod.Post, "/api/v1/admin/events", admin.AccessToken, EventTestKit.Body("Gap", "2027-03-14T02:30", "2027-03-14T04:00"), ct).ConfigureAwait(false);
            Check.Equal(HttpStatusCode.BadRequest, gap.Status, "Spring-forward gap rejected");
            Check.Equal("InvalidSchedule", gap.Error, "InvalidSchedule");
            Check.True(gap.Body.GetProperty("fieldErrors").GetProperty("startLocal").GetString()!.Contains("03:00"), "The hint names the next valid time: " + gap.Text);
            JsonElement overlap = await EventTestKit.CreateAsync(f, admin.AccessToken, EventTestKit.Body("Overlap", "2027-11-07T01:30", "2027-11-07T03:00"), ct).ConfigureAwait(false);
            Check.Equal(new DateTime(2027, 11, 7, 5, 30, 0, DateTimeKind.Utc), overlap.GetProperty("startUtc").GetDateTime().ToUniversalTime(), "Ambiguous 01:30 resolves to the first instant (EDT)");
            JsonElement london = await EventTestKit.CreateAsync(f, admin.AccessToken, EventTestKit.Body("London summer", "2027-07-01T19:00", "2027-07-01T21:00", null, "Europe/London"), ct).ConfigureAwait(false);
            Check.Equal(new DateTime(2027, 7, 1, 18, 0, 0, DateTimeKind.Utc), london.GetProperty("startUtc").GetDateTime().ToUniversalTime(), "BST is UTC+1");
            await ExpectInvalidAsync(f, admin, EventTestKit.Body("London gap", "2027-03-28T01:30", "2027-03-28T03:00", null, "Europe/London"), "startLocal", ct).ConfigureAwait(false);
            JsonElement allDay = await EventTestKit.CreateAsync(f, admin.AccessToken, EventTestKit.Body("Open day", "2027-06-05", "2027-06-06", new { isAllDay = true }), ct).ConfigureAwait(false);
            Check.True(allDay.GetProperty("isAllDay").GetBoolean(), "All day");
            Check.Equal(new DateTime(2027, 6, 5, 4, 0, 0, DateTimeKind.Utc), allDay.GetProperty("startUtc").GetDateTime().ToUniversalTime(), "All-day start is local midnight");
            Check.Equal(new DateTime(2027, 6, 7, 4, 0, 0, DateTimeKind.Utc), allDay.GetProperty("endUtc").GetDateTime().ToUniversalTime(), "All-day end date is inclusive");
            DateTime startUtc = DateTime.UtcNow.Date.AddDays(5).AddHours(23);
            ApiResult window = await Api.SendAsync(f, HttpMethod.Post, "/api/v1/admin/events", admin.AccessToken, EventTestKit.Body("Window", a, EventTestKit.Local(5, 20), new { rsvpClosesUtc = startUtc.AddDays(2) }), ct).ConfigureAwait(false);
            Check.Equal("InvalidSchedule", window.Error, "RSVP window outside the event");
            ApiResult reminders = await Api.SendAsync(f, HttpMethod.Post, "/api/v1/admin/events", admin.AccessToken, EventTestKit.Body("Reminders", a, EventTestKit.Local(5, 20), new { reminderOffsetsMinutes = new[] { 60, 120, 180, 240 } }), ct).ConfigureAwait(false);
            Check.Equal("InvalidSchedule", reminders.Error, "At most 3 reminders");
            JsonElement edited = await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/admin/events/" + Api.Str(london, "id"), admin.AccessToken, new { timeZone = "America/New_York" }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal("2027-07-01T19:00", Api.Str(edited, "startLocal"), "Changing the zone keeps the wall time");
            Check.Equal(new DateTime(2027, 7, 1, 23, 0, 0, DateTimeKind.Utc), edited.GetProperty("startUtc").GetDateTime().ToUniversalTime(), "UTC recomputed from the wall time");
        }

        private static async Task ExpectInvalidAsync(HttpFixture f, TestMember admin, object body, string field, CancellationToken ct)
        {
            ApiResult r = await Api.SendAsync(f, HttpMethod.Post, "/api/v1/admin/events", admin.AccessToken, body, ct).ConfigureAwait(false);
            Check.Equal(HttpStatusCode.BadRequest, r.Status, field + " status: " + r.Text);
            Check.Equal("InvalidSchedule", r.Error, field + " error");
            Check.True(r.Body.GetProperty("fieldErrors").TryGetProperty(field, out JsonElement _), field + " named: " + r.Text);
        }

        private static async Task FormatsAsync(HttpFixture f, CancellationToken ct)
        {
            TestMember admin = await Api.AdminAsync(f, ct).ConfigureAwait(false);
            TestMember going = await SafetyHelpers.MemberAsync(f, ct, "Going Member").ConfigureAwait(false);
            TestMember other = await SafetyHelpers.MemberAsync(f, ct, "Other Member").ConfigureAwait(false);
            Dictionary<string, object?> online = EventTestKit.Body("Online call", EventTestKit.Local(3, 18), EventTestKit.Local(3, 19), new { format = "Online", location = (object?)null });
            ApiResult missing = await Api.SendAsync(f, HttpMethod.Post, "/api/v1/admin/events", admin.AccessToken, online, ct).ConfigureAwait(false);
            Check.Equal(HttpStatusCode.BadRequest, missing.Status, "Online needs a join link");
            online["online"] = new { platformLabel = "Video call", joinUrl = "http://insecure.example.test/x" };
            ApiResult http = await Api.SendAsync(f, HttpMethod.Post, "/api/v1/admin/events", admin.AccessToken, online, ct).ConfigureAwait(false);
            Check.Equal(HttpStatusCode.BadRequest, http.Status, "https only");
            online["online"] = new { platformLabel = "Video call", joinUrl = "https://meet.example.test/room" };
            JsonElement created = await EventTestKit.CreateAsync(f, admin.AccessToken, online, ct).ConfigureAwait(false);
            string id = Api.Str(created, "id");
            Check.Equal("https://meet.example.test/room", Api.Str(created, "onlineJoinUrl"), "Staff view shows the join URL");
            Check.True(created.GetProperty("location").ValueKind == JsonValueKind.Null, "Online events have no location");
            await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/admin/events/" + id + "/publish", admin.AccessToken, new { }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            await EventTestKit.RsvpAsync(f, going.AccessToken, id, "Going", ct).ConfigureAwait(false);
            JsonElement early = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/events/" + id, going.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.True(early.GetProperty("online").GetProperty("joinUrl").ValueKind == JsonValueKind.Null, "Hidden before the reveal time");
            Check.True(early.GetProperty("online").GetProperty("joinUrlAvailableUtc").ValueKind == JsonValueKind.String, "joinUrlAvailableUtc");
            EventRecord e = await EventTestKit.RecordAsync(f, id, ct).ConfigureAwait(false);
            EventTestKit.Clock(f, e.StartUtc.AddMinutes(-10));
            JsonElement late = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/events/" + id, going.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal("https://meet.example.test/room", Api.Str(late.GetProperty("online"), "joinUrl"), "Revealed to Going members");
            JsonElement stranger = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/events/" + id, other.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.True(stranger.GetProperty("online").GetProperty("joinUrl").ValueKind == JsonValueKind.Null, "Not revealed to others");
            JsonElement summaries = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/events/enumerate", going.AccessToken, new { }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            JsonElement item = summaries.GetProperty("objects").EnumerateArray().First(o => Api.Str(o, "id") == id);
            Check.True(item.GetProperty("online").GetProperty("joinUrl").ValueKind == JsonValueKind.Null, "Summaries never carry the join URL");
            Dictionary<string, object?> hybrid = EventTestKit.Body("Hybrid", EventTestKit.Local(4, 18), EventTestKit.Local(4, 19), new { format = "Hybrid", online = new { joinUrl = "https://meet.example.test/h" } });
            JsonElement h = await EventTestKit.CreateAsync(f, admin.AccessToken, hybrid, ct).ConfigureAwait(false);
            Check.True(h.GetProperty("location").ValueKind == JsonValueKind.Object && h.GetProperty("online").ValueKind == JsonValueKind.Object, "Hybrid has both");
            ApiResult hybridNoPlace = await Api.SendAsync(f, HttpMethod.Post, "/api/v1/admin/events", admin.AccessToken,
                EventTestKit.Body("Hybrid2", EventTestKit.Local(4, 18), EventTestKit.Local(4, 19), new { format = "Hybrid", location = (object?)null, online = new { joinUrl = "https://meet.example.test/h" } }), ct).ConfigureAwait(false);
            Check.Equal(HttpStatusCode.BadRequest, hybridNoPlace.Status, "Hybrid needs a place");
            ApiResult geo = await Api.SendAsync(f, HttpMethod.Post, "/api/v1/admin/events", admin.AccessToken,
                EventTestKit.Body("Geo", EventTestKit.Local(4, 18), EventTestKit.Local(4, 19), new { location = new { name = "Field", geo = new { lat = 95, lng = 0 } } }), ct).ConfigureAwait(false);
            Check.Equal("InvalidGeo", geo.Error, "InvalidGeo");
        }

        private static async Task CancelAsync(HttpFixture f, CancellationToken ct)
        {
            TestMember admin = await Api.AdminAsync(f, ct).ConfigureAwait(false);
            TestMember member = await SafetyHelpers.MemberAsync(f, ct, "Planner").ConfigureAwait(false);
            string id = await EventTestKit.PublishedAsync(f, admin.AccessToken, "Canceled meetup", ct).ConfigureAwait(false);
            await EventTestKit.RsvpAsync(f, member.AccessToken, id, "Going", ct).ConfigureAwait(false);
            await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/me/notification-preferences", member.AccessToken, new { categories = new[] { new { category = "Events", email = false } } }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Post, "/api/v1/admin/events/" + id + "/cancel", admin.AccessToken, new { reason = "x" }, HttpStatusCode.BadRequest, "ValidationFailed", ct).ConfigureAwait(false);
            int sent = f.Fakes.Email.Messages.Count;
            JsonElement canceled = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/admin/events/" + id + "/cancel", admin.AccessToken, new { reason = "The hall is closed." }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal("Canceled", Api.Str(canceled, "status"), "Canceled");
            Check.Equal("The hall is closed.", Api.Str(canceled, "cancelReason"), "Reason");
            Check.True(canceled.GetProperty("reminderStatus").EnumerateArray().All(r => r.GetProperty("skippedUtc").ValueKind != JsonValueKind.Null || r.GetProperty("sentUtc").ValueKind != JsonValueKind.Null), "Reminders stopped");
            await EventTestKit.FanoutAsync(f, ct).ConfigureAwait(false);
            List<Notification> notices = await EventTestKit.NotificationsAsync(f, member.Id, NotificationTypeEnum.EventCanceled, ct).ConfigureAwait(false);
            Check.Equal(1, notices.Count, "EventCanceled notice");
            Check.True(notices[0].Title.Contains("canceled", StringComparison.Ordinal), "Title: " + notices[0].Title);
            Check.True(f.Fakes.Email.Messages.Skip(sent).Any(m => m.To == member.Email && m.Subject.Contains("Canceled meetup", StringComparison.Ordinal)), "Essential email sent even with Events email off");
            JsonElement mine = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/events/" + id, member.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.True(mine.GetProperty("myRsvp").GetProperty("eventCanceled").GetBoolean(), "RSVP kept with eventCanceled");
            await Api.ExpectErrorAsync(f, HttpMethod.Put, "/api/v1/events/" + id + "/rsvp", member.AccessToken, new { status = "Going" }, HttpStatusCode.Conflict, "EventCanceled", ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Put, "/api/v1/admin/events/" + id, admin.AccessToken, new { title = "New title" }, HttpStatusCode.Conflict, "EventCanceled", ct).ConfigureAwait(false);
            await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/admin/events/" + id, admin.AccessToken, new { labels = new[] { "archived" } }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Post, "/api/v1/admin/events/" + id + "/cancel", admin.AccessToken, new { reason = "Again please" }, HttpStatusCode.Conflict, "EventCanceled", ct).ConfigureAwait(false);
            JsonElement myEvents = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/me/events/enumerate", member.AccessToken, new { }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.True(EventTestKit.Ids(myEvents).Contains(id), "Canceled events I RSVPed to stay in my events");
        }

        private static async Task DuplicateDeleteAsync(HttpFixture f, CancellationToken ct)
        {
            TestMember admin = await Api.AdminAsync(f, ct).ConfigureAwait(false);
            TestMember member = await SafetyHelpers.MemberAsync(f, ct).ConfigureAwait(false);
            string id = await EventTestKit.PublishedAsync(f, admin.AccessToken, "Monthly social", ct, new { capacity = 10, labels = new[] { "monthly" } }).ConfigureAwait(false);
            await EventTestKit.RsvpAsync(f, member.AccessToken, id, "Going", ct).ConfigureAwait(false);
            string start = EventTestKit.Local(40, 19);
            JsonElement copy = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/admin/events/" + id + "/duplicate", admin.AccessToken, new { startLocal = start }, HttpStatusCode.Created, ct).ConfigureAwait(false);
            Check.Equal("Draft", Api.Str(copy, "status"), "Copy is a Draft");
            Check.Equal(start, Api.Str(copy, "startLocal"), "New start");
            Check.Equal(0, copy.GetProperty("goingCount").GetInt32(), "No RSVPs copied");
            Check.Equal(10, copy.GetProperty("capacity").GetInt32(), "Fields copied");
            Check.True(copy.GetProperty("labels").EnumerateArray().Any(l => l.GetString() == "monthly"), "Labels copied");
            Check.True(Api.Str(copy, "slug") != "monthly-social", "Unique slug");
            await Api.ExpectErrorAsync(f, HttpMethod.Delete, "/api/v1/admin/events/" + id, admin.AccessToken, null, HttpStatusCode.Conflict, "NotDraft", ct).ConfigureAwait(false);
            await Api.ExpectAsync(f, HttpMethod.Delete, "/api/v1/admin/events/" + Api.Str(copy, "id"), admin.AccessToken, null, HttpStatusCode.NoContent, ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Get, "/api/v1/admin/events/" + Api.Str(copy, "id"), admin.AccessToken, null, HttpStatusCode.NotFound, "NotFound", ct).ConfigureAwait(false);
        }

        private static async Task HostsAsync(HttpFixture f, CancellationToken ct)
        {
            TestMember manager = await EventTestKit.ManagerAsync(f, ct).ConfigureAwait(false);
            TestMember host = await SafetyHelpers.MemberAsync(f, ct, "Host Person").ConfigureAwait(false);
            TestMember member = await SafetyHelpers.MemberAsync(f, ct, "Plain Member").ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Post, "/api/v1/admin/events", member.AccessToken, EventTestKit.Body("Nope", EventTestKit.Local(5, 18), EventTestKit.Local(5, 19)), HttpStatusCode.Forbidden, "Forbidden", ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Post, "/api/v1/admin/events/enumerate", member.AccessToken, new { }, HttpStatusCode.Forbidden, "Forbidden", ct).ConfigureAwait(false);
            string id = await EventTestKit.PublishedAsync(f, manager.AccessToken, "Hosted session", ct, new { hostUserIds = new[] { host.Id } }).ConfigureAwait(false);
            JsonElement read = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/events/" + id, member.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal(host.Id, Api.Str(read.GetProperty("hosts")[0], "id"), "Hosts listed");
            Check.Equal(1, read.GetProperty("hostCount").GetInt32(), "hostCount");
            JsonElement asHost = await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/admin/events/" + id, host.AccessToken, new { description = "Bring water." }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal("Bring water.", Api.Str(asHost, "description"), "Hosts edit the description");
            Check.True(asHost.GetProperty("isHost").GetBoolean(), "isHost");
            await Api.ExpectErrorAsync(f, HttpMethod.Put, "/api/v1/admin/events/" + id, host.AccessToken, new { title = "Hijack" }, HttpStatusCode.Forbidden, "Forbidden", ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Post, "/api/v1/admin/events/" + id + "/cancel", host.AccessToken, new { reason = "Hosts cannot" }, HttpStatusCode.Forbidden, "Forbidden", ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Put, "/api/v1/admin/events/" + id, member.AccessToken, new { description = "x" }, HttpStatusCode.Forbidden, "Forbidden", ct).ConfigureAwait(false);
            JsonElement going = await EventTestKit.RsvpAsync(f, member.AccessToken, id, "Going", ct).ConfigureAwait(false);
            string rsvpId = Api.Str(going.GetProperty("myRsvp"), "id");
            JsonElement rsvps = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/admin/events/" + id + "/rsvps/enumerate", host.AccessToken, new { }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal(1, rsvps.GetProperty("objects").GetArrayLength(), "Hosts list RSVPs");
            JsonElement checkedIn = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/admin/events/" + id + "/rsvps/" + rsvpId + "/check-in", host.AccessToken, new { }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.True(checkedIn.GetProperty("checkedInUtc").ValueKind == JsonValueKind.String, "Checked in");
            Check.Equal(1, (await EventTestKit.RecordAsync(f, id, ct).ConfigureAwait(false)).CheckedInCount, "checkedincount");
            await Api.ExpectErrorAsync(f, HttpMethod.Post, "/api/v1/admin/events/" + id + "/rsvps/" + rsvpId + "/remove", host.AccessToken, new { }, HttpStatusCode.Forbidden, "Forbidden", ct).ConfigureAwait(false);
            await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/admin/events/" + id + "/rsvps/" + rsvpId + "/undo-check-in", host.AccessToken, new { }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal(0, (await EventTestKit.RecordAsync(f, id, ct).ConfigureAwait(false)).CheckedInCount, "undo");
            ApiResult memberPublic = await Api.SendAsync(f, HttpMethod.Post, "/api/v1/admin/events", manager.AccessToken, EventTestKit.Body("Public try", EventTestKit.Local(5, 18), EventTestKit.Local(5, 19), new { audience = "Public" }), ct).ConfigureAwait(false);
            Check.Equal(HttpStatusCode.BadRequest, memberPublic.Status, "Only a SystemAdmin makes events Public");
            JsonElement mine = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/me/events/enumerate", host.AccessToken, new { }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.True(EventTestKit.Ids(mine).Contains(id), "Hosted events are in my events");
        }

        private static async Task CategoriesAsync(HttpFixture f, CancellationToken ct)
        {
            TestMember admin = await Api.AdminAsync(f, ct).ConfigureAwait(false);
            TestMember manager = await EventTestKit.ManagerAsync(f, ct).ConfigureAwait(false);
            JsonElement property = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/property", null, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            List<string> keys = property.GetProperty("events").GetProperty("categories").EnumerateArray().Select(c => Api.Str(c, "key")).ToList();
            Check.True(keys.SequenceEqual(new[] { "meetup", "workshop" }), "Seeded categories in the property document: " + String.Join(",", keys));
            JsonElement list = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/events/categories/enumerate", manager.AccessToken, new { }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal(2, list.GetProperty("objects").GetArrayLength(), "Two categories");
            ApiResult unknown = await Api.SendAsync(f, HttpMethod.Post, "/api/v1/admin/events", manager.AccessToken, EventTestKit.Body("Bad category", EventTestKit.Local(5, 18), EventTestKit.Local(5, 19), new { category = "picnic" }), ct).ConfigureAwait(false);
            Check.Equal("UnknownOption", unknown.Error, "UnknownOption");
            JsonElement ev = await EventTestKit.CreateAsync(f, manager.AccessToken, EventTestKit.Body("Workshop day", EventTestKit.Local(5, 18), EventTestKit.Local(5, 19), new { category = "WORKSHOP" }), ct).ConfigureAwait(false);
            Check.Equal("workshop", Api.Str(ev.GetProperty("category"), "key"), "Category key normalized");
            Check.Equal("Workshop", Api.Str(ev.GetProperty("category"), "label"), "Category label");
            await Api.ExpectErrorAsync(f, HttpMethod.Post, "/api/v1/admin/events/categories", manager.AccessToken, new { key = "x", label = "X" }, HttpStatusCode.Forbidden, "Forbidden", ct).ConfigureAwait(false);
            JsonElement created = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/admin/events/categories", admin.AccessToken, new { key = "social", label = "Social", color = "#AA3300" }, HttpStatusCode.Created, ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Post, "/api/v1/admin/events/categories", admin.AccessToken, new { key = "SOCIAL", label = "Again" }, HttpStatusCode.Conflict, "KeyInUse", ct).ConfigureAwait(false);
            JsonElement renamed = await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/admin/events/categories/" + Api.Str(created, "id"), admin.AccessToken, new { label = "Socials" }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal("Socials", Api.Str(renamed, "label"), "Renamed");
            JsonElement all = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/admin/events/categories/enumerate", admin.AccessToken, new { }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            List<string> ids = EventTestKit.Ids(all);
            ids.Reverse();
            JsonElement order = await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/admin/events/categories/order", admin.AccessToken, new { categoryIds = ids }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal(3, order.GetProperty("updated").GetInt32(), "Ordered");
            JsonElement reordered = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/events/categories/enumerate", manager.AccessToken, new { }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal(ids[0], EventTestKit.Ids(reordered)[0], "Member list follows the order");
            string workshop = all.GetProperty("objects").EnumerateArray().First(c => Api.Str(c, "key") == "workshop").GetProperty("id").GetString()!;
            JsonElement retired = await Api.ExpectAsync(f, HttpMethod.Delete, "/api/v1/admin/events/categories/" + workshop, admin.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.True(retired.GetProperty("retired").GetBoolean(), "In use: retired");
            JsonElement deleted = await Api.ExpectAsync(f, HttpMethod.Delete, "/api/v1/admin/events/categories/" + Api.Str(created, "id"), admin.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.True(!deleted.GetProperty("retired").GetBoolean(), "Unused: deleted");
            ApiResult retiredUse = await Api.SendAsync(f, HttpMethod.Post, "/api/v1/admin/events", manager.AccessToken, EventTestKit.Body("Retired category", EventTestKit.Local(5, 18), EventTestKit.Local(5, 19), new { category = "workshop" }), ct).ConfigureAwait(false);
            Check.Equal("UnknownOption", retiredUse.Error, "Retired categories cannot be chosen");
            JsonElement after = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/property", null, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.True(after.GetProperty("events").GetProperty("categories").EnumerateArray().All(c => Api.Str(c, "key") != "workshop"), "Retired categories leave the property document");
            Check.True(await ModerationTestKit.AuditCountAsync(f, "event.category.create", Api.Str(created, "id"), ct).ConfigureAwait(false) >= 1, "Audited");
        }

        private static async Task NoticesAsync(HttpFixture f, CancellationToken ct)
        {
            TestMember admin = await Api.AdminAsync(f, ct).ConfigureAwait(false);
            TestMember going = await SafetyHelpers.MemberAsync(f, ct, "Going Member").ConfigureAwait(false);
            TestMember interested = await SafetyHelpers.MemberAsync(f, ct, "Interested Member").ConfigureAwait(false);
            string id = await EventTestKit.PublishedAsync(f, admin.AccessToken, "Evening class", ct).ConfigureAwait(false);
            await EventTestKit.RsvpAsync(f, going.AccessToken, id, "Going", ct).ConfigureAwait(false);
            await EventTestKit.RsvpAsync(f, interested.AccessToken, id, "Interested", ct).ConfigureAwait(false);
            await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/admin/events/" + id, admin.AccessToken, new { description = "New notes" }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            await EventTestKit.FanoutAsync(f, ct).ConfigureAwait(false);
            Check.Equal(0, (await EventTestKit.NotificationsAsync(f, going.Id, NotificationTypeEnum.EventUpdated, ct).ConfigureAwait(false)).Count, "Cosmetic edits never notify");
            JsonElement moved = await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/admin/events/" + id, admin.AccessToken, new { startLocal = EventTestKit.Local(10, 19), endLocal = EventTestKit.Local(10, 21) }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal(EventTestKit.Local(10, 19), Api.Str(moved, "startLocal"), "Moved");
            await EventTestKit.FanoutAsync(f, ct).ConfigureAwait(false);
            List<Notification> first = await EventTestKit.NotificationsAsync(f, going.Id, NotificationTypeEnum.EventUpdated, ct).ConfigureAwait(false);
            Check.Equal(1, first.Count, "Going members are told");
            Check.True(first[0].Title.Contains("moved to", StringComparison.Ordinal), "Title: " + first[0].Title);
            Check.Equal(1, (await EventTestKit.NotificationsAsync(f, interested.Id, NotificationTypeEnum.EventUpdated, ct).ConfigureAwait(false)).Count, "Interested members are told");
            await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/admin/events/" + id, admin.AccessToken, new { location = new { name = "Other Hall", address = new { locality = "Exampleville", countryCode = "US" } } }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            await EventTestKit.FanoutAsync(f, ct).ConfigureAwait(false);
            List<Notification> second = await EventTestKit.NotificationsAsync(f, going.Id, NotificationTypeEnum.EventUpdated, ct).ConfigureAwait(false);
            Check.Equal(1, second.Count, "One unread notice per event (aggregated)");
            Check.True(second[0].Title.Contains("location", StringComparison.Ordinal), "Refreshed title: " + second[0].Title);
            await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/admin/events/" + id, admin.AccessToken, new { startLocal = EventTestKit.Local(11, 19), endLocal = EventTestKit.Local(11, 21), notifyAttendees = false }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            long jobs = await EventTestKit.CountAsync(f, "SELECT COUNT(*) AS cnt FROM notificationfanouts WHERE type = 'EventUpdated' AND sourceid LIKE '" + id + "%';", ct).ConfigureAwait(false);
            Check.Equal(2L, jobs, "notifyAttendees false queues no notice");
            EventRecord e = await EventTestKit.RecordAsync(f, id, ct).ConfigureAwait(false);
            Check.Equal(3, e.IcsSequence, "SEQUENCE increments on time and location changes");
            Check.True(f.Fakes.Email.Messages.Any(m => m.To == going.Email && m.Subject.Contains("Evening class", StringComparison.Ordinal)), "event_updated email to members with Events email on");
        }

        private static async Task SingletonOccurrenceAsync(HttpFixture f, CancellationToken ct)
        {
            TestMember admin = await Api.AdminAsync(f, ct).ConfigureAwait(false);
            TestMember member = await Api.MemberAsync(f, ct).ConfigureAwait(false);
            string id = await EventTestKit.PublishedAsync(f, admin.AccessToken, "Singleton occurrence", ct, new { labels = new[] { "singleton" } }).ConfigureAwait(false);
            string path = "/api/v1/events/" + id + "/occurrences/enumerate";
            JsonElement included = await Api.ExpectAsync(f, HttpMethod.Post, path, member.AccessToken, new { maxResults = 1, labels = new[] { "singleton" }, search = "Singleton" }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal(1, included.GetProperty("objects").GetArrayLength(), "Matching singleton returned");
            Check.Equal(1L, included.GetProperty("totalRecords").GetInt64(), "Singleton exact total");
            JsonElement skipped = await Api.ExpectAsync(f, HttpMethod.Post, path, member.AccessToken, new { skip = 1, maxResults = 1 }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal(0, skipped.GetProperty("objects").GetArrayLength(), "Skip applies to singleton");
            foreach (object query in new object[] { new { labels = new[] { "absent" } }, new { createdAfterUtc = DateTime.UtcNow.AddDays(1) }, new { search = "absent-search" }, new { filters = new { when = "Past" } } })
            {
                JsonElement excluded = await Api.ExpectAsync(f, HttpMethod.Post, path, member.AccessToken, query, HttpStatusCode.OK, ct).ConfigureAwait(false);
                Check.Equal(0, excluded.GetProperty("objects").GetArrayLength(), "A singleton obeys normal source filters");
                Check.Equal(0L, excluded.GetProperty("totalRecords").GetInt64(), "Filtered total is exact");
            }
            await Api.ExpectErrorAsync(f, HttpMethod.Post, path, member.AccessToken, new { ordering = "Unsupported" }, HttpStatusCode.BadRequest, "ValidationFailed", ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Post, path, member.AccessToken, new { continuationToken = "malformed" }, HttpStatusCode.BadRequest, "InvalidContinuationToken", ct).ConfigureAwait(false);
        }

        private static async Task MetricsAsync(HttpFixture f, CancellationToken ct)
        {
            TestMember admin = await Api.AdminAsync(f, ct).ConfigureAwait(false);
            List<TestMember> members = await EventTestKit.MembersAsync(f, 2, ct).ConfigureAwait(false);
            string id = await EventTestKit.PublishedAsync(f, admin.AccessToken, "Counted event", ct, null, 2).ConfigureAwait(false);
            foreach (TestMember m in members) await EventTestKit.RsvpAsync(f, m.AccessToken, id, "Going", ct).ConfigureAwait(false);
            JsonElement metrics = await MetricEnumerationTestKit.DailyAsync(f, "/api/v1/admin/metrics/events?days=30", admin.AccessToken, ct).ConfigureAwait(false);
            Check.True(metrics.GetProperty("eventsByDay").EnumerateArray().Sum(d => d.GetProperty("count").GetInt64()) >= 1, "Events by day");
            Check.True(metrics.GetProperty("rsvpsByDay").EnumerateArray().Sum(d => d.GetProperty("count").GetInt64()) >= 2, "RSVPs by day");
            Check.True(metrics.GetProperty("byCategory").TryGetProperty("enumerate", out JsonElement _), "Bounded category breakdown");
            ApiResult csv = await Api.SendAsync(f, HttpMethod.Get, "/api/v1/admin/events/" + id + "/rsvps/export.csv", admin.AccessToken, null, ct).ConfigureAwait(false);
            Check.Equal(HttpStatusCode.OK, csv.Status, "CSV");
            Check.True(csv.Text.StartsWith("displayName,handle,status,guests", StringComparison.Ordinal), "CSV header: " + csv.Text);
            Check.Equal(3, csv.Text.Trim().Split('\n').Length, "Two rows");
            await Api.ExpectErrorAsync(f, HttpMethod.Get, "/api/v1/admin/metrics/events", members[0].AccessToken, null, HttpStatusCode.Forbidden, "Forbidden", ct).ConfigureAwait(false);
        }

        #endregion
    }
}
