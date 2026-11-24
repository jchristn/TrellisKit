namespace Test.Shared.Suites
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;
    using System.Net;
    using System.Net.Http;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Touchstone.Core;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Models;
    using TrellisKit.Server.Services;

    /// <summary>
    /// Recurring events (resolved decision 11): expansion for each frequency across DST transitions (America/New_York,
    /// Europe/London, and a zone without DST), last day and ordinal weekday rules, count and until, the horizon worker,
    /// edits with scope One, Following, and All that keep RSVPs, rule changes that cancel removed dates, cancellation
    /// scopes, draft deletion, and the series ICS (RRULE, EXDATE, RECURRENCE-ID).
    /// </summary>
    public static class RecurrenceSuite
    {
        #region Public-Members

        /// <summary>
        /// Suite identifier.
        /// </summary>
        public const string Id = "Recurrence";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the suite.
        /// </summary>
        /// <returns>Suite.</returns>
        public static TestSuiteDescriptor Build()
        {
            return new TestSuiteDescriptor(Id, "Recurring events (" + TestEnvironment.Provider + ")", new[]
            {
                SuiteCases.Unit(Id, "Expand.Weekly.NewYorkDst", "Weekly on Tuesday and Thursday keeps 18:00 local across the March transition in America/New_York; UTC shifts by an hour", WeeklyDstAsync),
                SuiteCases.Unit(Id, "Expand.Daily.LondonAndTokyo", "Daily in Europe/London shifts UTC on the last Sunday of March; weekly in Asia/Tokyo keeps UTC constant", DailyAsync),
                SuiteCases.Unit(Id, "Expand.Monthly", "Monthly on the last day, on day 31 (months without it skipped), on the second Tuesday, and on the last Friday", MonthlyAsync),
                SuiteCases.Unit(Id, "Expand.YearlyCountUntilInterval", "Yearly on February 29 skips non-leap years; count, until, and interval bound the series; a wall time in the gap moves forward by the gap", YearlyAsync),
                SuiteCases.Unit(Id, "Summary.AndRrule", "Plain English summaries and RRULE values for each rule shape", SummaryAsync),
                Case("Series.Create", "A recurring event materializes one events row per occurrence (own ids, RSVPs, reminders) linked by recurrenceId; occurrences and recurrenceId filters list them; the first occurrence must fall on the rule; publishing publishes the series", CreateAsync),
                Case("Series.Horizon", "Open-ended rules materialize up to events.recurrenceHorizonDays; the recurrence worker extends the horizon as time passes, bounded by events.maxOccurrences", HorizonAsync),
                Case("Series.EditOneAndAll", "Scope One makes an exception; scope All changes every not-yet-started occurrence that is not an exception and the series template", EditOneAllAsync),
                Case("Series.EditAllTime", "A time change with scope All keeps every RSVP on its occurrence, moves the wall time of each date, increments SEQUENCE, and sends one aggregated EventUpdated per member", EditAllTimeAsync),
                Case("Series.EditFollowing", "Scope Following ends the old rule before the occurrence and starts a new recurrence there; RSVPs stay on their occurrences", EditFollowingAsync),
                Case("Series.RuleChange", "Changing Tuesday and Thursday to Tuesday cancels the Thursdays (EventCanceled to their RSVPs) and keeps the Tuesday RSVPs", RuleChangeAsync),
                Case("Series.CancelAndDelete", "Cancel One, Following, and All; deleted draft occurrences are never materialized again; scope All deletes a draft series", CancelDeleteAsync),
                Case("Series.Search", "Global search collapses a recurring series into one result that opens the next upcoming occurrence that is not canceled (with the rule summary and the occurrence count), and marks canceled events", SearchAsync),
                Case("Series.Ics", "The series ICS carries the master VEVENT with RRULE, EXDATE for canceled occurrences, an override VEVENT with RECURRENCE-ID for an edited one, and a VTIMEZONE; an occurrence ICS carries the series UID and RECURRENCE-ID", IcsAsync)
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

        private static DateTime L(string text)
        {
            return DateTime.ParseExact(text, "yyyy-MM-ddTHH:mm", CultureInfo.InvariantCulture);
        }

        private static DateTime U(string text)
        {
            return DateTime.SpecifyKind(DateTime.ParseExact(text, "yyyy-MM-ddTHH:mm", CultureInfo.InvariantCulture), DateTimeKind.Utc);
        }

        private static List<EventOccurrenceSlot> Expand(EventRecurrenceRule rule, string start, string zone, int limit = 50)
        {
            return RecurrenceExpander.Expand(rule, L(start), EventTimeZones.Find(zone)!, 60, new DateTime(2040, 1, 1, 0, 0, 0, DateTimeKind.Utc), limit);
        }

        private static Task WeeklyDstAsync(CancellationToken ct)
        {
            EventRecurrenceRule rule = new EventRecurrenceRule { Frequency = EventRecurrenceFrequencyEnum.Weekly, Weekdays = new List<DayOfWeek> { DayOfWeek.Tuesday, DayOfWeek.Thursday }, Count = 6 };
            List<EventOccurrenceSlot> slots = Expand(rule, "2027-03-02T18:00", "America/New_York");
            Check.Equal(6, slots.Count, "Six occurrences");
            Check.True(slots.Select(s => s.StartLocal.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)).SequenceEqual(new[] { "2027-03-02", "2027-03-04", "2027-03-09", "2027-03-11", "2027-03-16", "2027-03-18" }), "Dates");
            Check.True(slots.All(s => s.StartLocal.Hour == 18), "Local 18:00 kept");
            Check.Equal(U("2027-03-11T23:00"), slots[3].StartUtc, "EST: 23:00 UTC");
            Check.Equal(U("2027-03-16T22:00"), slots[4].StartUtc, "EDT: 22:00 UTC");
            Check.True(slots.Select(s => s.Index).SequenceEqual(Enumerable.Range(0, 6)), "Indexes");
            Check.Equal(U("2027-03-16T23:00"), slots[4].EndUtc, "Duration kept");
            return Task.CompletedTask;
        }

        private static Task DailyAsync(CancellationToken ct)
        {
            List<EventOccurrenceSlot> london = Expand(new EventRecurrenceRule { Frequency = EventRecurrenceFrequencyEnum.Daily, Count = 4 }, "2027-03-26T09:00", "Europe/London");
            Check.True(london.Select(s => s.StartUtc).SequenceEqual(new[] { U("2027-03-26T09:00"), U("2027-03-27T09:00"), U("2027-03-28T08:00"), U("2027-03-29T08:00") }), "London UTC shifts with BST");
            List<EventOccurrenceSlot> tokyo = Expand(new EventRecurrenceRule { Frequency = EventRecurrenceFrequencyEnum.Weekly, Count = 3 }, "2027-03-01T10:00", "Asia/Tokyo");
            Check.True(tokyo.All(s => s.StartUtc.Hour == 1), "Tokyo keeps 01:00 UTC");
            Check.True(tokyo.All(s => s.StartLocal.DayOfWeek == DayOfWeek.Monday), "Weekly defaults to the start's weekday");
            return Task.CompletedTask;
        }

        private static Task MonthlyAsync(CancellationToken ct)
        {
            List<string> last = Expand(new EventRecurrenceRule { Frequency = EventRecurrenceFrequencyEnum.Monthly, MonthDay = -1, Count = 4 }, "2027-01-31T12:00", "America/New_York").Select(D).ToList();
            Check.True(last.SequenceEqual(new[] { "2027-01-31", "2027-02-28", "2027-03-31", "2027-04-30" }), "Last day: " + String.Join(",", last));
            List<string> day31 = Expand(new EventRecurrenceRule { Frequency = EventRecurrenceFrequencyEnum.Monthly, MonthDay = 31, Count = 4 }, "2027-01-31T12:00", "America/New_York").Select(D).ToList();
            Check.True(day31.SequenceEqual(new[] { "2027-01-31", "2027-03-31", "2027-05-31", "2027-07-31" }), "Day 31 skips short months: " + String.Join(",", day31));
            List<string> second = Expand(new EventRecurrenceRule { Frequency = EventRecurrenceFrequencyEnum.Monthly, SetPosition = 2, Weekdays = new List<DayOfWeek> { DayOfWeek.Tuesday }, Count = 3 }, "2027-01-12T19:00", "America/New_York").Select(D).ToList();
            Check.True(second.SequenceEqual(new[] { "2027-01-12", "2027-02-09", "2027-03-09" }), "Second Tuesday: " + String.Join(",", second));
            List<string> lastFriday = Expand(new EventRecurrenceRule { Frequency = EventRecurrenceFrequencyEnum.Monthly, SetPosition = -1, Weekdays = new List<DayOfWeek> { DayOfWeek.Friday }, Count = 3 }, "2027-01-29T19:00", "Europe/London").Select(D).ToList();
            Check.True(lastFriday.SequenceEqual(new[] { "2027-01-29", "2027-02-26", "2027-03-26" }), "Last Friday: " + String.Join(",", lastFriday));
            EventRecurrenceRule fifth = new EventRecurrenceRule { Frequency = EventRecurrenceFrequencyEnum.Monthly, SetPosition = 4, Weekdays = new List<DayOfWeek> { DayOfWeek.Monday } };
            Check.True(RecurrenceExpander.Matches(fifth, new DateTime(2027, 1, 25), new DateTime(2027, 2, 22)), "Fourth Monday matches");
            Check.True(!RecurrenceExpander.Matches(fifth, new DateTime(2027, 1, 25), new DateTime(2027, 2, 15)), "Third Monday does not");
            return Task.CompletedTask;
        }

        private static Task YearlyAsync(CancellationToken ct)
        {
            List<string> leap = Expand(new EventRecurrenceRule { Frequency = EventRecurrenceFrequencyEnum.Yearly, Count = 2 }, "2028-02-29T10:00", "America/New_York").Select(D).ToList();
            Check.True(leap.SequenceEqual(new[] { "2028-02-29", "2032-02-29" }), "February 29 only in leap years: " + String.Join(",", leap));
            List<EventOccurrenceSlot> until = Expand(new EventRecurrenceRule { Frequency = EventRecurrenceFrequencyEnum.Weekly, UntilDate = new DateTime(2027, 1, 25) }, "2027-01-04T18:00", "America/New_York");
            Check.Equal(4, until.Count, "Until is inclusive");
            List<string> every2 = Expand(new EventRecurrenceRule { Frequency = EventRecurrenceFrequencyEnum.Weekly, Interval = 2, Count = 3 }, "2027-01-04T18:00", "America/New_York").Select(D).ToList();
            Check.True(every2.SequenceEqual(new[] { "2027-01-04", "2027-01-18", "2027-02-01" }), "Interval 2");
            List<EventOccurrenceSlot> gap = Expand(new EventRecurrenceRule { Frequency = EventRecurrenceFrequencyEnum.Weekly, Count = 2 }, "2027-03-07T02:30", "America/New_York");
            Check.Equal(U("2027-03-14T07:30"), gap[1].StartUtc, "A gap time moves forward (03:30 EDT)");
            List<EventOccurrenceSlot> capped = Expand(new EventRecurrenceRule { Frequency = EventRecurrenceFrequencyEnum.Daily }, "2027-01-01T08:00", "UTC", 10);
            Check.Equal(10, capped.Count, "The limit bounds an open-ended rule");
            List<EventOccurrenceSlot> horizon = RecurrenceExpander.Expand(new EventRecurrenceRule { Frequency = EventRecurrenceFrequencyEnum.Daily }, L("2027-01-01T08:00"), TimeZoneInfo.Utc, 60, U("2027-01-05T00:00"), 100);
            Check.Equal(4, horizon.Count, "The horizon bounds an open-ended rule");
            return Task.CompletedTask;
        }

        private static Task SummaryAsync(CancellationToken ct)
        {
            Check.Equal("Every week on Tuesday and Thursday, 12 times", RecurrenceSummary.Describe(new EventRecurrenceRule { Frequency = EventRecurrenceFrequencyEnum.Weekly, Weekdays = new List<DayOfWeek> { DayOfWeek.Thursday, DayOfWeek.Tuesday }, Count = 12 }, L("2027-01-05T18:00")), "Weekly");
            Check.Equal("Every month on the last Friday, until June 30, 2027", RecurrenceSummary.Describe(new EventRecurrenceRule { Frequency = EventRecurrenceFrequencyEnum.Monthly, SetPosition = -1, Weekdays = new List<DayOfWeek> { DayOfWeek.Friday }, UntilDate = new DateTime(2027, 6, 30) }, L("2027-01-29T18:00")), "Last Friday");
            Check.Equal("Every 2 weeks on Monday", RecurrenceSummary.Describe(new EventRecurrenceRule { Frequency = EventRecurrenceFrequencyEnum.Weekly, Interval = 2 }, L("2027-01-04T18:00")), "Every 2 weeks");
            Check.Equal("Every month on the last day", RecurrenceSummary.Describe(new EventRecurrenceRule { Frequency = EventRecurrenceFrequencyEnum.Monthly, MonthDay = -1 }, L("2027-01-31T18:00")), "Last day");
            Check.Equal("Every 3 days, once", RecurrenceSummary.Describe(new EventRecurrenceRule { Frequency = EventRecurrenceFrequencyEnum.Daily, Interval = 3, Count = 1 }, L("2027-01-31T18:00")), "Daily");
            Check.Equal("Every year on February 29", RecurrenceSummary.Describe(new EventRecurrenceRule { Frequency = EventRecurrenceFrequencyEnum.Yearly }, L("2028-02-29T18:00")), "Yearly");
            TimeZoneInfo ny = EventTimeZones.Find("America/New_York")!;
            EventRecurrence weekly = new EventRecurrence { Frequency = EventRecurrenceFrequencyEnum.Weekly, Interval = 1, Weekdays = new List<DayOfWeek> { DayOfWeek.Thursday, DayOfWeek.Tuesday }, Count = 12 };
            Check.Equal("FREQ=WEEKLY;INTERVAL=1;BYDAY=TU,TH;COUNT=12", CalendarService.Rrule(weekly, ny, L("2027-01-05T18:00"), 200), "Weekly RRULE");
            EventRecurrence lastFriday = new EventRecurrence { Frequency = EventRecurrenceFrequencyEnum.Monthly, Interval = 1, SetPosition = -1, Weekdays = new List<DayOfWeek> { DayOfWeek.Friday }, UntilLocalDate = "2027-06-25" };
            Check.Equal("FREQ=MONTHLY;INTERVAL=1;BYDAY=-1FR;UNTIL=20270625T220000Z", CalendarService.Rrule(lastFriday, ny, L("2027-01-29T18:00"), 200), "Ordinal BYDAY with UNTIL in UTC");
            EventRecurrence lastDay = new EventRecurrence { Frequency = EventRecurrenceFrequencyEnum.Monthly, Interval = 2, MonthDay = -1 };
            Check.Equal("FREQ=MONTHLY;INTERVAL=2;BYMONTHDAY=-1;COUNT=200", CalendarService.Rrule(lastDay, ny, L("2027-01-31T18:00"), 200), "BYMONTHDAY, open-ended stops at maxOccurrences");
            return Task.CompletedTask;
        }

        private static string D(EventOccurrenceSlot s)
        {
            return s.StartLocal.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }

        private static async Task<JsonElement> SeriesAsync(HttpFixture f, string bearer, string title, CancellationToken ct, object? recurrence = null, string start = "2027-03-02T18:00", string end = "2027-03-02T19:30", bool publish = true)
        {
            Dictionary<string, object?> body = EventTestKit.Body(title, start, end);
            body["recurrence"] = recurrence ?? new { frequency = "Weekly", weekdays = new[] { "Tue", "Thu" }, count = 6 };
            JsonElement created = await EventTestKit.CreateAsync(f, bearer, body, ct).ConfigureAwait(false);
            if (publish) created = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/admin/events/" + Api.Str(created, "id") + "/publish", bearer, new { }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            return created;
        }

        private static async Task<List<JsonElement>> ListAsync(HttpFixture f, string bearer, string eventId, CancellationToken ct)
        {
            JsonElement page = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/events/" + eventId + "/occurrences/enumerate", bearer, new { maxResults = 100 }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            return page.GetProperty("objects").EnumerateArray().ToList();
        }

        private static async Task CreateAsync(HttpFixture f, CancellationToken ct)
        {
            TestMember admin = await Api.AdminAsync(f, ct).ConfigureAwait(false);
            TestMember member = await SafetyHelpers.MemberAsync(f, ct).ConfigureAwait(false);
            Dictionary<string, object?> wrong = EventTestKit.Body("Off rule", "2027-03-01T18:00", "2027-03-01T19:00");
            wrong["recurrence"] = new { frequency = "Weekly", weekdays = new[] { "Tue", "Thu" }, count = 4 };
            ApiResult off = await Api.SendAsync(f, HttpMethod.Post, "/api/v1/admin/events", admin.AccessToken, wrong, ct).ConfigureAwait(false);
            Check.Equal("InvalidSchedule", off.Error, "The first occurrence must fall on the rule");
            wrong["startLocal"] = "2027-03-02T18:00";
            wrong["endLocal"] = "2027-03-02T19:00";
            wrong["recurrence"] = new { frequency = "Weekly", count = 500 };
            ApiResult tooMany = await Api.SendAsync(f, HttpMethod.Post, "/api/v1/admin/events", admin.AccessToken, wrong, ct).ConfigureAwait(false);
            Check.Equal(HttpStatusCode.BadRequest, tooMany.Status, "count above events.maxOccurrences");
            wrong["recurrence"] = new { frequency = "Weekly", count = 3, untilDate = "2027-05-01" };
            Check.Equal(HttpStatusCode.BadRequest, (await Api.SendAsync(f, HttpMethod.Post, "/api/v1/admin/events", admin.AccessToken, wrong, ct).ConfigureAwait(false)).Status, "count and untilDate together");
            JsonElement draft = await SeriesAsync(f, admin.AccessToken, "Lunch run", ct, null, "2027-03-02T18:00", "2027-03-02T19:30", false).ConfigureAwait(false);
            string first = Api.Str(draft, "id");
            JsonElement rec = draft.GetProperty("recurrence");
            Check.Equal("Every week on Tuesday and Thursday, 6 times", Api.Str(rec, "summary"), "Summary");
            Check.Equal(0, rec.GetProperty("occurrenceIndex").GetInt32(), "First occurrence");
            Check.True(rec.GetProperty("rule").GetProperty("weekdays").EnumerateArray().Select(x => x.GetString()).SequenceEqual(new[] { "Tue", "Thu" }), "Weekdays");
            string recurrenceId = Api.Str(rec, "id");
            List<EventRecord> rows = await EventTestKit.OccurrencesAsync(f, recurrenceId, ct).ConfigureAwait(false);
            Check.Equal(6, rows.Count, "Six occurrence rows");
            Check.True(rows.All(r => r.Status == EventStatusEnum.Draft), "Drafts until published");
            Check.True(rows.Select(r => r.Id).Distinct().Count() == 6 && rows.All(r => r.ReminderOffsetsMinutes.Count == 2), "Own ids and reminders");
            Check.Equal(12L, await EventTestKit.CountAsync(f, "SELECT COUNT(*) AS cnt FROM eventreminders WHERE eventid IN ('" + String.Join("','", rows.Select(r => r.Id)) + "');", ct).ConfigureAwait(false), "Reminder rows per occurrence");
            await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/admin/events/" + first + "/publish", admin.AccessToken, new { }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            List<JsonElement> listed = await ListAsync(f, member.AccessToken, rows[3].Id, ct).ConfigureAwait(false);
            Check.Equal(6, listed.Count, "Members list the published occurrences from any of them");
            Check.True(listed.All(o => Api.Str(o, "startLocal").EndsWith("T18:00", StringComparison.Ordinal)), "Every occurrence at 18:00 local");
            Check.Equal(U("2027-03-16T22:00"), listed[4].GetProperty("startUtc").GetDateTime().ToUniversalTime(), "UTC follows DST");
            JsonElement filtered = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/events/enumerate", member.AccessToken, new { maxResults = 50, filters = new { recurrenceId } }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal(6, filtered.GetProperty("objects").GetArrayLength(), "filters.recurrenceId");
            long templates = await EventTestKit.CountAsync(f, "SELECT COUNT(*) AS cnt FROM events WHERE recurrenceid = '" + recurrenceId + "' AND status = 'Template';", ct).ConfigureAwait(false);
            Check.Equal(1L, templates, "One hidden series template");
            string templateId = (await f.Database.Events.Recurrences.ReadAsync(recurrenceId, ct).ConfigureAwait(false))!.TemplateEventId!;
            await Api.ExpectErrorAsync(f, HttpMethod.Get, "/api/v1/events/" + templateId, member.AccessToken, null, HttpStatusCode.NotFound, "NotFound", ct).ConfigureAwait(false);
            JsonElement rsvp = await EventTestKit.RsvpAsync(f, member.AccessToken, rows[2].Id, "Going", ct).ConfigureAwait(false);
            Check.Equal(1, rsvp.GetProperty("goingCount").GetInt32(), "RSVPs are per occurrence");
            Check.Equal(0, (await EventTestKit.RecordAsync(f, rows[3].Id, ct).ConfigureAwait(false)).GoingCount, "Other occurrences unaffected");
            Dictionary<string, object?> proposal = EventTestKit.Body("Recurring proposal", "2027-03-02T18:00", "2027-03-02T19:00");
            proposal["recurrence"] = new { frequency = "Weekly", count = 3 };
            ApiResult refused = await Api.SendAsync(f, HttpMethod.Post, "/api/v1/events/proposals", member.AccessToken, proposal, ct).ConfigureAwait(false);
            Check.Equal(HttpStatusCode.BadRequest, refused.Status, "Recurring proposals are refused");
        }

        private static async Task HorizonAsync(HttpFixture f, CancellationToken ct)
        {
            TestMember admin = await Api.AdminAsync(f, ct).ConfigureAwait(false);
            f.Settings.Events.RecurrenceHorizonDays = 30;
            f.Settings.Events.MaxOccurrences = 12;
            string start = EventTestKit.Local(3, 18);
            Dictionary<string, object?> body = EventTestKit.Body("Weekly open", start, EventTestKit.Local(3, 19));
            body["recurrence"] = new { frequency = "Weekly" };
            JsonElement created = await EventTestKit.CreateAsync(f, admin.AccessToken, body, ct).ConfigureAwait(false);
            string recurrenceId = Api.Str(created.GetProperty("recurrence"), "id");
            int initial = (await EventTestKit.OccurrencesAsync(f, recurrenceId, ct).ConfigureAwait(false)).Count;
            Check.True(initial >= 4 && initial <= 5, "Up to 30 days ahead: " + initial);
            EventTestKit.Clock(f, DateTime.UtcNow.AddDays(40));
            int added = await f.Services.Events.Recurrence.HorizonPassAsync(ct).ConfigureAwait(false);
            int later = (await EventTestKit.OccurrencesAsync(f, recurrenceId, ct).ConfigureAwait(false)).Count;
            Check.True(added > 0 && later == initial + added, "The worker extended the horizon (" + initial + " to " + later + ")");
            EventTestKit.Clock(f, DateTime.UtcNow.AddDays(400));
            await f.Services.Events.Recurrence.HorizonPassAsync(ct).ConfigureAwait(false);
            Check.Equal(12, (await EventTestKit.OccurrencesAsync(f, recurrenceId, ct).ConfigureAwait(false)).Count, "Bounded by events.maxOccurrences");
            EventRecurrence r = (await f.Database.Events.Recurrences.ReadAsync(recurrenceId, ct).ConfigureAwait(false))!;
            Check.Equal(12, r.MaterializedCount, "materializedcount");
            Check.Equal(0, await f.Services.Events.Recurrence.HorizonPassAsync(ct).ConfigureAwait(false), "Idempotent");
        }

        private static async Task EditOneAllAsync(HttpFixture f, CancellationToken ct)
        {
            TestMember admin = await Api.AdminAsync(f, ct).ConfigureAwait(false);
            JsonElement first = await SeriesAsync(f, admin.AccessToken, "Club night", ct).ConfigureAwait(false);
            string recurrenceId = Api.Str(first.GetProperty("recurrence"), "id");
            List<EventRecord> rows = await EventTestKit.OccurrencesAsync(f, recurrenceId, ct).ConfigureAwait(false);
            JsonElement one = await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/admin/events/" + rows[2].Id, admin.AccessToken, new { title = "Club night special" }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.True(one.GetProperty("recurrence").GetProperty("isException").GetBoolean(), "Scope One makes an exception");
            Check.Equal(1L, await EventTestKit.CountAsync(f, "SELECT COUNT(*) AS cnt FROM eventrecurrenceexceptions WHERE recurrenceid = '" + recurrenceId + "' AND kind = 'Edited';", ct).ConfigureAwait(false), "Edited exception row");
            Check.Equal("Club night", (await EventTestKit.RecordAsync(f, rows[3].Id, ct).ConfigureAwait(false)).Title, "Other occurrences unchanged");
            await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/admin/events/" + rows[1].Id, admin.AccessToken, new { title = "Club evening", capacity = 30, scope = "All" }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            List<EventRecord> after = await EventTestKit.OccurrencesAsync(f, recurrenceId, ct).ConfigureAwait(false);
            Check.Equal("Club night special", after.First(o => o.Id == rows[2].Id).Title, "The exception is untouched");
            Check.True(after.Where(o => o.Id != rows[2].Id).All(o => o.Title == "Club evening" && o.Capacity == 30), "Every other occurrence changed");
            EventRecurrence r = (await f.Database.Events.Recurrences.ReadAsync(recurrenceId, ct).ConfigureAwait(false))!;
            EventRecord template = (await f.Database.Events.Records.ReadAsync(r.TemplateEventId!, ct).ConfigureAwait(false))!;
            Check.Equal("Club evening", template.Title, "The series template changed (later occurrences copy it)");
        }

        private static async Task EditAllTimeAsync(HttpFixture f, CancellationToken ct)
        {
            TestMember admin = await Api.AdminAsync(f, ct).ConfigureAwait(false);
            TestMember member = await SafetyHelpers.MemberAsync(f, ct, "Regular").ConfigureAwait(false);
            JsonElement first = await SeriesAsync(f, admin.AccessToken, "Run club", ct).ConfigureAwait(false);
            string recurrenceId = Api.Str(first.GetProperty("recurrence"), "id");
            List<EventRecord> rows = await EventTestKit.OccurrencesAsync(f, recurrenceId, ct).ConfigureAwait(false);
            string a = Api.Str((await EventTestKit.RsvpAsync(f, member.AccessToken, rows[1].Id, "Going", ct).ConfigureAwait(false)).GetProperty("myRsvp"), "id");
            string b = Api.Str((await EventTestKit.RsvpAsync(f, member.AccessToken, rows[4].Id, "Going", ct).ConfigureAwait(false)).GetProperty("myRsvp"), "id");
            await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/admin/events/" + rows[2].Id, admin.AccessToken, new { startLocal = "2027-03-09T19:00", endLocal = "2027-03-09T20:30", scope = "All" }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            List<EventRecord> after = await EventTestKit.OccurrencesAsync(f, recurrenceId, ct).ConfigureAwait(false);
            Check.Equal(6, after.Count, "Same occurrences");
            Check.True(after.Select(o => o.Id).SequenceEqual(rows.Select(o => o.Id)), "Rows kept (RSVPs stay on their occurrences)");
            TimeZoneInfo ny = EventTimeZones.Find("America/New_York")!;
            Check.True(after.All(o => EventTimeZones.ToLocal(o.StartUtc, ny).Hour == 19), "Every date moved to 19:00 local");
            Check.True(after.All(o => o.IcsSequence >= 1), "SEQUENCE incremented");
            Check.Equal(a, (await f.Database.Events.Rsvps.ReadAsync(rows[1].Id, member.Id, ct).ConfigureAwait(false))!.Id, "RSVP kept on occurrence 1");
            Check.Equal(b, (await f.Database.Events.Rsvps.ReadAsync(rows[4].Id, member.Id, ct).ConfigureAwait(false))!.Id, "RSVP kept on occurrence 4");
            EventRecurrence r = (await f.Database.Events.Recurrences.ReadAsync(recurrenceId, ct).ConfigureAwait(false))!;
            Check.Equal("2027-03-02T19:00", r.StartLocal, "The series start moved to 19:00");
            await EventTestKit.FanoutAsync(f, ct).ConfigureAwait(false);
            List<Notification> notices = await EventTestKit.NotificationsAsync(f, member.Id, NotificationTypeEnum.EventUpdated, ct).ConfigureAwait(false);
            Check.Equal(1, notices.Count, "One aggregated EventUpdated per member");
        }

        private static async Task EditFollowingAsync(HttpFixture f, CancellationToken ct)
        {
            TestMember admin = await Api.AdminAsync(f, ct).ConfigureAwait(false);
            TestMember member = await SafetyHelpers.MemberAsync(f, ct, "Later Member").ConfigureAwait(false);
            JsonElement first = await SeriesAsync(f, admin.AccessToken, "Study group", ct).ConfigureAwait(false);
            string oldRecurrence = Api.Str(first.GetProperty("recurrence"), "id");
            List<EventRecord> rows = await EventTestKit.OccurrencesAsync(f, oldRecurrence, ct).ConfigureAwait(false);
            await EventTestKit.RsvpAsync(f, member.AccessToken, rows[4].Id, "Going", ct).ConfigureAwait(false);
            JsonElement split = await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/admin/events/" + rows[3].Id, admin.AccessToken,
                new { title = "Study group (new room)", startLocal = "2027-03-11T17:30", endLocal = "2027-03-11T19:00", scope = "Following" }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            string newRecurrence = Api.Str(split.GetProperty("recurrence"), "id");
            Check.True(newRecurrence != oldRecurrence, "A new recurrence starts here");
            Check.Equal(0, split.GetProperty("recurrence").GetProperty("occurrenceIndex").GetInt32(), "Re-indexed from 0");
            EventRecurrence old = (await f.Database.Events.Recurrences.ReadAsync(oldRecurrence, ct).ConfigureAwait(false))!;
            Check.Equal(3, old.Count, "The old rule ends before this occurrence");
            List<EventRecord> before = await EventTestKit.OccurrencesAsync(f, oldRecurrence, ct).ConfigureAwait(false);
            List<EventRecord> afterRows = await EventTestKit.OccurrencesAsync(f, newRecurrence, ct).ConfigureAwait(false);
            Check.Equal(3, before.Count, "Three stay on the old rule");
            Check.Equal(3, afterRows.Count, "Three move to the new rule");
            Check.True(before.All(o => o.Title == "Study group"), "Earlier occurrences unchanged");
            Check.True(afterRows.All(o => o.Title == "Study group (new room)"), "Edits apply from here on");
            Check.True(afterRows.Select(o => o.Id).Contains(rows[4].Id), "Occurrence rows kept");
            Check.True((await f.Database.Events.Rsvps.ReadAsync(rows[4].Id, member.Id, ct).ConfigureAwait(false)) != null, "RSVP stays on its occurrence");
            TimeZoneInfo ny = EventTimeZones.Find("America/New_York")!;
            Check.True(afterRows.All(o => EventTimeZones.ToLocal(o.StartUtc, ny).Hour == 17 && EventTimeZones.ToLocal(o.StartUtc, ny).Minute == 30), "New time from here on");
            List<JsonElement> listedOld = await ListAsync(f, member.AccessToken, rows[0].Id, ct).ConfigureAwait(false);
            Check.Equal(3, listedOld.Count, "Occurrences of the old series");
        }

        private static async Task RuleChangeAsync(HttpFixture f, CancellationToken ct)
        {
            TestMember admin = await Api.AdminAsync(f, ct).ConfigureAwait(false);
            TestMember tuesday = await SafetyHelpers.MemberAsync(f, ct, "Tuesday Member").ConfigureAwait(false);
            TestMember thursday = await SafetyHelpers.MemberAsync(f, ct, "Thursday Member").ConfigureAwait(false);
            JsonElement first = await SeriesAsync(f, admin.AccessToken, "Training", ct).ConfigureAwait(false);
            string recurrenceId = Api.Str(first.GetProperty("recurrence"), "id");
            List<EventRecord> rows = await EventTestKit.OccurrencesAsync(f, recurrenceId, ct).ConfigureAwait(false);
            await EventTestKit.RsvpAsync(f, tuesday.AccessToken, rows[2].Id, "Going", ct).ConfigureAwait(false);
            await EventTestKit.RsvpAsync(f, thursday.AccessToken, rows[3].Id, "Going", ct).ConfigureAwait(false);
            await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/admin/events/" + rows[0].Id, admin.AccessToken, new { recurrence = new { frequency = "Weekly", weekdays = new[] { "Tue" }, count = 3 }, scope = "All" }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            List<EventRecord> after = await EventTestKit.OccurrencesAsync(f, recurrenceId, ct).ConfigureAwait(false);
            List<EventRecord> live = after.Where(o => o.Status == EventStatusEnum.Published).ToList();
            Check.Equal(3, live.Count, "Three Tuesdays remain");
            Check.True(live.All(o => o.StartUtc.DayOfWeek == DayOfWeek.Tuesday || o.StartUtc.DayOfWeek == DayOfWeek.Wednesday), "Tuesdays (UTC may be the next day)");
            Check.Equal(EventStatusEnum.Canceled, after.First(o => o.Id == rows[3].Id).Status, "The Thursday was canceled");
            Check.Equal(EventStatusEnum.Published, after.First(o => o.Id == rows[2].Id).Status, "The Tuesday stays");
            Check.True((await f.Database.Events.Rsvps.ReadAsync(rows[2].Id, tuesday.Id, ct).ConfigureAwait(false)) != null, "Tuesday RSVP kept");
            await EventTestKit.FanoutAsync(f, ct).ConfigureAwait(false);
            Check.Equal(1, (await EventTestKit.NotificationsAsync(f, thursday.Id, NotificationTypeEnum.EventCanceled, ct).ConfigureAwait(false)).Count, "EventCanceled to the Thursday RSVP");
            Check.Equal(0, (await EventTestKit.NotificationsAsync(f, tuesday.Id, NotificationTypeEnum.EventCanceled, ct).ConfigureAwait(false)).Count, "Nothing canceled for the Tuesday RSVP");
        }

        private static async Task SearchAsync(HttpFixture f, CancellationToken ct)
        {
            TestMember admin = await Api.AdminAsync(f, ct).ConfigureAwait(false);
            TestMember member = await SafetyHelpers.MemberAsync(f, ct).ConfigureAwait(false);
            JsonElement first = await SeriesAsync(f, admin.AccessToken, "Zephyrine drills", ct).ConfigureAwait(false);
            List<EventRecord> rows = await EventTestKit.OccurrencesAsync(f, Api.Str(first.GetProperty("recurrence"), "id"), ct).ConfigureAwait(false);
            await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/admin/events/" + rows[0].Id + "/cancel", admin.AccessToken, new { reason = "Holiday" }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            string single = await EventTestKit.PublishedAsync(f, admin.AccessToken, "Zephyrine seminar", ct).ConfigureAwait(false);
            await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/admin/events/" + single + "/cancel", admin.AccessToken, new { reason = "Instructor ill" }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            await ForumTestKit.DrainSearchAsync(f, ct).ConfigureAwait(false);
            JsonElement page = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/search/enumerate", member.AccessToken, new { search = "zephyrine", filters = new { types = new[] { "Event" } } }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            List<JsonElement> events = page.GetProperty("objects").EnumerateArray().Where(o => Api.Str(o, "type") == "Event").ToList();
            Check.Equal(2, events.Count, "One result for the series and one for the single event (" + page + ")");
            JsonElement series = events.First(o => o.GetProperty("event").GetProperty("recurrenceId").ValueKind == JsonValueKind.String);
            Check.Equal(rows[1].Id, Api.Str(series, "id"), "The series opens the next occurrence that is not canceled");
            Check.Equal(6, series.GetProperty("event").GetProperty("occurrenceCount").GetInt32(), "Six occurrences collapsed");
            Check.True(!series.GetProperty("event").GetProperty("isCanceled").GetBoolean(), "The chosen occurrence is not canceled");
            Check.True(Api.Str(series.GetProperty("event"), "recurrenceSummary").StartsWith("Every week", StringComparison.Ordinal), "Rule summary");
            JsonElement canceled = events.First(o => Api.Str(o, "id") == single);
            Check.True(canceled.GetProperty("event").GetProperty("isCanceled").GetBoolean(), "A canceled event is marked");
        }

        private static async Task CancelDeleteAsync(HttpFixture f, CancellationToken ct)
        {
            TestMember admin = await Api.AdminAsync(f, ct).ConfigureAwait(false);
            JsonElement first = await SeriesAsync(f, admin.AccessToken, "Cancel series", ct).ConfigureAwait(false);
            string recurrenceId = Api.Str(first.GetProperty("recurrence"), "id");
            List<EventRecord> rows = await EventTestKit.OccurrencesAsync(f, recurrenceId, ct).ConfigureAwait(false);
            await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/admin/events/" + rows[1].Id + "/cancel", admin.AccessToken, new { reason = "Holiday break" }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            List<EventRecord> afterOne = await EventTestKit.OccurrencesAsync(f, recurrenceId, ct).ConfigureAwait(false);
            Check.Equal(1, afterOne.Count(o => o.Status == EventStatusEnum.Canceled), "Only one canceled");
            Check.True(afterOne.First(o => o.Id == rows[1].Id).IsException, "A canceled exception");
            await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/admin/events/" + rows[4].Id + "/cancel", admin.AccessToken, new { reason = "Season ends early", scope = "Following" }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            List<EventRecord> afterFollowing = await EventTestKit.OccurrencesAsync(f, recurrenceId, ct).ConfigureAwait(false);
            Check.True(afterFollowing.Where(o => o.OccurrenceIndex >= 4).All(o => o.Status == EventStatusEnum.Canceled), "Following canceled");
            Check.Equal(EventStatusEnum.Published, afterFollowing.First(o => o.Id == rows[3].Id).Status, "Earlier kept");
            Check.Equal(4, (await f.Database.Events.Recurrences.ReadAsync(recurrenceId, ct).ConfigureAwait(false))!.Count, "The rule ends before the canceled occurrence");
            await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/admin/events/" + rows[0].Id + "/cancel", admin.AccessToken, new { reason = "No more sessions", scope = "All" }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.True((await EventTestKit.OccurrencesAsync(f, recurrenceId, ct).ConfigureAwait(false)).All(o => o.Status == EventStatusEnum.Canceled), "All canceled");

            JsonElement draft = await SeriesAsync(f, admin.AccessToken, "Draft series", ct, new { frequency = "Weekly", weekdays = new[] { "Tue" } }, "2027-03-02T18:00", "2027-03-02T19:00", false).ConfigureAwait(false);
            string draftRecurrence = Api.Str(draft.GetProperty("recurrence"), "id");
            List<EventRecord> drafts = await EventTestKit.OccurrencesAsync(f, draftRecurrence, ct).ConfigureAwait(false);
            int count = drafts.Count;
            await Api.ExpectAsync(f, HttpMethod.Delete, "/api/v1/admin/events/" + drafts[1].Id, admin.AccessToken, null, HttpStatusCode.NoContent, ct).ConfigureAwait(false);
            EventRecurrence dr = (await f.Database.Events.Recurrences.ReadAsync(draftRecurrence, ct).ConfigureAwait(false))!;
            dr.MaterializedThroughUtc = null;
            await f.Database.Events.Recurrences.UpdateAsync(dr, ct).ConfigureAwait(false);
            await f.Services.Events.Recurrence.HorizonPassAsync(ct).ConfigureAwait(false);
            List<EventRecord> remaining = await EventTestKit.OccurrencesAsync(f, draftRecurrence, ct).ConfigureAwait(false);
            Check.Equal(count - 1, remaining.Count, "A deleted draft occurrence is never materialized again");
            await Api.ExpectAsync(f, HttpMethod.Delete, "/api/v1/admin/events/" + drafts[0].Id + "?scope=All", admin.AccessToken, null, HttpStatusCode.NoContent, ct).ConfigureAwait(false);
            Check.Equal(0, (await EventTestKit.OccurrencesAsync(f, draftRecurrence, ct).ConfigureAwait(false)).Count, "Scope All deletes the draft series");
            Check.True(await f.Database.Events.Recurrences.ReadAsync(draftRecurrence, ct).ConfigureAwait(false) == null, "The recurrence is deleted");
        }

        private static async Task IcsAsync(HttpFixture f, CancellationToken ct)
        {
            TestMember admin = await Api.AdminAsync(f, ct).ConfigureAwait(false);
            TestMember member = await SafetyHelpers.MemberAsync(f, ct).ConfigureAwait(false);
            JsonElement first = await SeriesAsync(f, admin.AccessToken, "Series calendar", ct).ConfigureAwait(false);
            string recurrenceId = Api.Str(first.GetProperty("recurrence"), "id");
            List<EventRecord> rows = await EventTestKit.OccurrencesAsync(f, recurrenceId, ct).ConfigureAwait(false);
            await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/admin/events/" + rows[2].Id + "/cancel", admin.AccessToken, new { reason = "Room booked" }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/admin/events/" + rows[4].Id, admin.AccessToken, new { title = "Series calendar extra", startLocal = "2027-03-16T20:00", endLocal = "2027-03-16T21:00" }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            ApiResult series = await Api.SendAsync(f, HttpMethod.Get, "/api/v1/events/" + rows[0].Id + "/ics?scope=Series", member.AccessToken, null, ct).ConfigureAwait(false);
            Check.Equal(HttpStatusCode.OK, series.Status, "Series ICS");
            string text = Unfold(series.Text);
            Check.True(text.Contains("RRULE:FREQ=WEEKLY;INTERVAL=1;BYDAY=TU,TH;COUNT=6\r\n", StringComparison.Ordinal), "RRULE: " + text);
            Check.True(text.Contains("DTSTART;TZID=America/New_York:20270302T180000", StringComparison.Ordinal), "Master DTSTART");
            Check.True(text.Contains("EXDATE;TZID=America/New_York:20270309T180000", StringComparison.Ordinal), "EXDATE for the canceled occurrence");
            Check.True(text.Contains("RECURRENCE-ID;TZID=America/New_York:20270316T180000", StringComparison.Ordinal), "Override keyed by the original start");
            Check.True(text.Contains("DTSTART;TZID=America/New_York:20270316T200000", StringComparison.Ordinal), "Override start");
            Check.True(text.Contains("SUMMARY:Series calendar extra", StringComparison.Ordinal), "Override fields");
            Check.True(text.Contains("BEGIN:VTIMEZONE\r\nTZID:America/New_York", StringComparison.Ordinal), "VTIMEZONE");
            Check.True(text.Contains("BEGIN:DAYLIGHT", StringComparison.Ordinal) && text.Contains("DTSTART:20270314T020000", StringComparison.Ordinal), "The March 2027 transition");
            Check.Equal(2, CountOf(text, "BEGIN:VEVENT"), "Master and one override");
            Check.Equal(2, CountOf(text, "UID:" + recurrenceId + "@"), "The master and the override share the series UID");
            ApiResult single = await Api.SendAsync(f, HttpMethod.Get, "/api/v1/events/" + rows[1].Id + "/ics", member.AccessToken, null, ct).ConfigureAwait(false);
            string one = Unfold(single.Text);
            Check.True(one.Contains("UID:" + recurrenceId + "@", StringComparison.Ordinal), "Occurrence ICS uses the series UID");
            Check.True(one.Contains("RECURRENCE-ID;TZID=America/New_York:20270304T180000", StringComparison.Ordinal), "RECURRENCE-ID: " + one);
            Check.True(single.Headers.ContainsKey("Content-Disposition") || (single.ContentType ?? String.Empty).Contains("text/calendar"), "text/calendar");
        }

        private static string Unfold(string text)
        {
            return text.Replace("\r\n ", String.Empty);
        }

        private static int CountOf(string text, string part)
        {
            int count = 0;
            int at = 0;
            while ((at = text.IndexOf(part, at, StringComparison.Ordinal)) >= 0)
            {
                count++;
                at += part.Length;
            }
            return count;
        }

        #endregion
    }
}
