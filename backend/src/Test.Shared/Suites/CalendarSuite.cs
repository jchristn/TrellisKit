namespace Test.Shared.Suites
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Net;
    using System.Net.Http;
    using System.Text;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Touchstone.Core;
    using TrellisKit.Server.Services;

    /// <summary>
    /// Calendars (docs/PHASE4_CONTRACT.md section 4.5): the ICS writer (escaping, folding, VTIMEZONE), the single event
    /// ICS fields and SEQUENCE, all-day and canceled events, and the personal feed (token, cache header, no join URLs,
    /// rotation, deletion, and invalidation on a ban).
    /// </summary>
    public static class CalendarSuite
    {
        #region Public-Members

        /// <summary>
        /// Suite identifier.
        /// </summary>
        public const string Id = "Calendar";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the suite.
        /// </summary>
        /// <returns>Suite.</returns>
        public static TestSuiteDescriptor Build()
        {
            return new TestSuiteDescriptor(Id, "Calendars (" + TestEnvironment.Provider + ")", new[]
            {
                SuiteCases.Unit(Id, "Writer.EscapeFoldTimeZones", "TEXT values escape backslash, semicolon, comma, and newlines; lines fold at 75 octets without splitting characters; VTIMEZONE lists the real transitions, or one STANDARD observance for a zone without DST", WriterAsync),
                Case("Ics.Fields", "The event ICS carries UID, SEQUENCE, DTSTART and DTEND with TZID, the VTIMEZONE, SUMMARY, DESCRIPTION, LOCATION, URL, STATUS, and VALARM per offset; SEQUENCE increments on a time change; canceled events say STATUS:CANCELLED; all-day events use VALUE=DATE", FieldsAsync),
                SuiteCases.Http(Id, "Feed.ApiOrigin", "Absolute API links use https plus domains.api, or the website origin when it is a loopback host (a local stack proxies /api) or no API domain is set", ApiOriginAsync),
                Case("Feed.Lifecycle", "The personal feed lists Going and Waitlisted events without join links, with private caching; the same URL is returned again; rotation and deletion invalidate it; a ban disables it", FeedAsync)
            });
        }

        #endregion

        #region Private-Methods

        private static Task ApiOriginAsync(HttpFixture f, CancellationToken ct)
        {
            Check.Equal("https://api.example.test", PropertyContext.ApiOriginFor("https://www.example.test", "api.example.test"), "Public deployment");
            Check.Equal("http://127.0.0.1:8080", PropertyContext.ApiOriginFor("http://127.0.0.1:8080/", "api.example.test"), "Local stack (IPv4 loopback)");
            Check.Equal("http://localhost:5173", PropertyContext.ApiOriginFor("http://localhost:5173", "api.example.test"), "Local stack (localhost)");
            Check.Equal("https://www.example.test", PropertyContext.ApiOriginFor("https://www.example.test", null), "No API domain");
            Check.Equal(f.Services.Property.ApiOrigin, f.Services.Events.Notifier.ApiOrigin(), "Event links use the property's API origin");
            return Task.CompletedTask;
        }

        private static TestCaseDescriptor Case(string caseId, string name, Func<HttpFixture, CancellationToken, Task> body)
        {
            return SuiteCases.Http(Id, caseId, name, (f, ct) =>
            {
                EventTestKit.Relax(f);
                return body(f, ct);
            });
        }

        private static Task WriterAsync(CancellationToken ct)
        {
            Check.Equal("a\\, b\\; c\\\\ d\\ne", IcsWriter.Escape("a, b; c\\ d\r\ne"), "Escaping");
            string longLine = "DESCRIPTION:" + new string('x', 70) + "éééé" + new string('y', 80);
            List<string> folded = IcsWriter.Fold(longLine);
            Check.True(folded.Count >= 3, "Folded");
            Check.True(folded.All(l => Encoding.UTF8.GetByteCount(l) <= 75), "At most 75 octets per line");
            Check.True(folded.Skip(1).All(l => l.StartsWith(" ", StringComparison.Ordinal)), "Continuations start with a space");
            Check.Equal(longLine, String.Concat(folded.Select((l, i) => i == 0 ? l : l.Substring(1))), "Unfolds to the original");
            IcsWriter w = new IcsWriter();
            w.TimeZone(EventTimeZones.Find("America/New_York")!, new DateTime(2027, 1, 1, 0, 0, 0, DateTimeKind.Utc), new DateTime(2027, 12, 31, 0, 0, 0, DateTimeKind.Utc));
            string ny = w.ToString();
            Check.True(ny.Contains("BEGIN:DAYLIGHT\r\nDTSTART:20270314T020000\r\nTZOFFSETFROM:-0500\r\nTZOFFSETTO:-0400", StringComparison.Ordinal), "Spring 2027: " + ny);
            Check.True(ny.Contains("BEGIN:STANDARD\r\nDTSTART:20271107T020000\r\nTZOFFSETFROM:-0400\r\nTZOFFSETTO:-0500", StringComparison.Ordinal), "Fall 2027");
            IcsWriter t = new IcsWriter();
            t.TimeZone(EventTimeZones.Find("Asia/Tokyo")!, new DateTime(2027, 1, 1, 0, 0, 0, DateTimeKind.Utc), new DateTime(2027, 12, 31, 0, 0, 0, DateTimeKind.Utc));
            string tokyo = t.ToString();
            Check.True(tokyo.Contains("BEGIN:STANDARD\r\nDTSTART:19700101T000000\r\nTZOFFSETFROM:+0900\r\nTZOFFSETTO:+0900", StringComparison.Ordinal) && !tokyo.Contains("DAYLIGHT", StringComparison.Ordinal), "No DST: " + tokyo);
            return Task.CompletedTask;
        }

        private static string Unfold(string text)
        {
            return text.Replace("\r\n ", String.Empty);
        }

        private static async Task FieldsAsync(HttpFixture f, CancellationToken ct)
        {
            TestMember admin = await Api.AdminAsync(f, ct).ConfigureAwait(false);
            TestMember member = await SafetyHelpers.MemberAsync(f, ct).ConfigureAwait(false);
            JsonElement created = await EventTestKit.CreateAsync(f, admin.AccessToken, EventTestKit.Body("Calendar test, with commas; and semicolons", "2027-01-15T18:30", "2027-01-15T20:00",
                new { description = "Line one\n\nA long description " + new string('z', 120) }), ct).ConfigureAwait(false);
            string id = Api.Str(created, "id");
            await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/admin/events/" + id + "/publish", admin.AccessToken, new { }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            ApiResult ics = await Api.SendAsync(f, HttpMethod.Get, "/api/v1/events/" + id + "/ics", member.AccessToken, null, ct).ConfigureAwait(false);
            Check.Equal(HttpStatusCode.OK, ics.Status, "ICS");
            Check.True((ics.ContentType ?? String.Empty).StartsWith("text/calendar", StringComparison.Ordinal), "text/calendar");
            Check.True(ics.Text.Split("\r\n").All(l => Encoding.UTF8.GetByteCount(l) <= 75), "Folded lines");
            string text = Unfold(ics.Text);
            Check.True(text.StartsWith("BEGIN:VCALENDAR\r\nVERSION:2.0\r\n", StringComparison.Ordinal), "VCALENDAR");
            Check.True(text.Contains("UID:" + id + "@", StringComparison.Ordinal), "UID");
            Check.True(text.Contains("SEQUENCE:0\r\n", StringComparison.Ordinal), "SEQUENCE 0");
            Check.True(text.Contains("DTSTART;TZID=America/New_York:20270115T183000", StringComparison.Ordinal) && text.Contains("DTEND;TZID=America/New_York:20270115T200000", StringComparison.Ordinal), "Local times with TZID");
            Check.True(text.Contains("BEGIN:VTIMEZONE\r\nTZID:America/New_York", StringComparison.Ordinal), "VTIMEZONE");
            Check.True(text.Contains("SUMMARY:Calendar test\\, with commas\\; and semicolons", StringComparison.Ordinal), "Escaped SUMMARY");
            Check.True(text.Contains("DESCRIPTION:Line one", StringComparison.Ordinal) && text.Contains("/app/events/" + id, StringComparison.Ordinal), "DESCRIPTION with the event link");
            Check.True(text.Contains("LOCATION:Example Hall\\, 100 Example Street", StringComparison.Ordinal), "LOCATION");
            Check.True(text.Contains("STATUS:CONFIRMED", StringComparison.Ordinal), "CONFIRMED");
            Check.True(text.Contains("TRIGGER:-PT1440M", StringComparison.Ordinal) && text.Contains("TRIGGER:-PT120M", StringComparison.Ordinal), "VALARM per offset");
            await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/admin/events/" + id, admin.AccessToken, new { startLocal = "2027-01-15T19:00", endLocal = "2027-01-15T20:30" }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            string moved = Unfold((await Api.SendAsync(f, HttpMethod.Get, "/api/v1/events/" + id + "/ics", member.AccessToken, null, ct).ConfigureAwait(false)).Text);
            Check.True(moved.Contains("SEQUENCE:1\r\n", StringComparison.Ordinal), "SEQUENCE increments");
            await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/admin/events/" + id + "/cancel", admin.AccessToken, new { reason = "Snow day", notifyAttendees = false }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            string canceled = Unfold((await Api.SendAsync(f, HttpMethod.Get, "/api/v1/events/" + id + "/ics", member.AccessToken, null, ct).ConfigureAwait(false)).Text);
            Check.True(canceled.Contains("STATUS:CANCELLED", StringComparison.Ordinal) && canceled.Contains("SEQUENCE:2\r\n", StringComparison.Ordinal), "Canceled");
            JsonElement allDay = await EventTestKit.CreateAsync(f, admin.AccessToken, EventTestKit.Body("Festival", "2027-06-05", "2027-06-06", new { isAllDay = true }), ct).ConfigureAwait(false);
            await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/admin/events/" + Api.Str(allDay, "id") + "/publish", admin.AccessToken, new { }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            string day = Unfold((await Api.SendAsync(f, HttpMethod.Get, "/api/v1/events/" + Api.Str(allDay, "id") + "/ics", member.AccessToken, null, ct).ConfigureAwait(false)).Text);
            Check.True(day.Contains("DTSTART;VALUE=DATE:20270605", StringComparison.Ordinal) && day.Contains("DTEND;VALUE=DATE:20270607", StringComparison.Ordinal), "All-day dates (exclusive end): " + day);
            await Api.ExpectErrorAsync(f, HttpMethod.Get, "/api/v1/events/" + id + "/ics", null, null, HttpStatusCode.Unauthorized, "Unauthorized", ct).ConfigureAwait(false);
        }

        private static async Task FeedAsync(HttpFixture f, CancellationToken ct)
        {
            TestMember admin = await Api.AdminAsync(f, ct).ConfigureAwait(false);
            TestMember member = await SafetyHelpers.MemberAsync(f, ct, "Feed Member").ConfigureAwait(false);
            string going = await EventTestKit.PublishedAsync(f, admin.AccessToken, "Feed going", ct).ConfigureAwait(false);
            string interested = await EventTestKit.PublishedAsync(f, admin.AccessToken, "Feed interested", ct).ConfigureAwait(false);
            Dictionary<string, object?> online = EventTestKit.Body("Feed online", EventTestKit.Local(1, 0, 5), EventTestKit.Local(1, 1), new { format = "Online", location = (object?)null, online = new { joinUrl = "https://meet.example.test/secret-room" } });
            string onlineId = Api.Str(await EventTestKit.CreateAsync(f, admin.AccessToken, online, ct).ConfigureAwait(false), "id");
            await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/admin/events/" + onlineId + "/publish", admin.AccessToken, new { }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            await EventTestKit.RsvpAsync(f, member.AccessToken, going, "Going", ct).ConfigureAwait(false);
            await EventTestKit.RsvpAsync(f, member.AccessToken, interested, "Interested", ct).ConfigureAwait(false);
            await EventTestKit.RsvpAsync(f, member.AccessToken, onlineId, "Going", ct).ConfigureAwait(false);
            JsonElement feed = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/me/calendar-feed", member.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            string url = Api.Str(feed, "url");
            Check.True(url.Contains("/api/v1/calendar/cfd_", StringComparison.Ordinal) && url.EndsWith(".ics", StringComparison.Ordinal), "Feed URL: " + url);
            JsonElement again = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/me/calendar-feed", member.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal(url, Api.Str(again, "url"), "The same URL again");
            string path = new Uri(url).AbsolutePath;
            ApiResult fetched = await Api.SendAsync(f, HttpMethod.Get, path, null, null, ct).ConfigureAwait(false);
            Check.Equal(HttpStatusCode.OK, fetched.Status, "Anonymous fetch with the token");
            string cache = fetched.Headers.TryGetValue("Cache-Control", out string? c) ? c : String.Empty;
            Check.True(cache.Contains("private", StringComparison.Ordinal) && cache.Contains("max-age=900", StringComparison.Ordinal), "Cache-Control: " + cache);
            string text = Unfold(fetched.Text);
            Check.True(text.Contains("SUMMARY:Feed going", StringComparison.Ordinal) && text.Contains("SUMMARY:Feed online", StringComparison.Ordinal), "Going events");
            Check.True(!text.Contains("Feed interested", StringComparison.Ordinal), "Interested events are not in the feed");
            Check.True(!text.Contains("secret-room", StringComparison.Ordinal), "Never join URLs");
            Check.True(text.Contains("UID:" + going + "@", StringComparison.Ordinal), "UID per occurrence");
            string secretHash = (await f.Database.Events.Feeds.ReadForUserAsync(member.Id, ct).ConfigureAwait(false))!.SecretHash;
            Check.True(!url.Contains(secretHash, StringComparison.Ordinal) && secretHash.Length == 64, "Only a hash of the secret is stored");
            await Api.ExpectErrorAsync(f, HttpMethod.Get, path.Replace(".ics", "x.ics"), null, null, HttpStatusCode.NotFound, "NotFound", ct).ConfigureAwait(false);
            JsonElement rotated = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/me/calendar-feed/rotate", member.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            string newUrl = Api.Str(rotated, "url");
            Check.True(newUrl != url && rotated.GetProperty("rotatedUtc").ValueKind == JsonValueKind.String, "Rotated");
            await Api.ExpectErrorAsync(f, HttpMethod.Get, path, null, null, HttpStatusCode.NotFound, "NotFound", ct).ConfigureAwait(false);
            string newPath = new Uri(newUrl).AbsolutePath;
            await Api.ExpectAsync(f, HttpMethod.Get, newPath, null, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/admin/members/" + member.Id + "/ban", HttpFixture.AdminToken, new { reason = "Spam" }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Get, newPath, null, null, HttpStatusCode.NotFound, "NotFound", ct).ConfigureAwait(false);
            Check.True(!(await f.Database.Events.Feeds.ReadForUserAsync(member.Id, ct).ConfigureAwait(false))!.IsEnabled, "A ban disables the feed");
            Check.True(await f.Database.Events.Rsvps.ReadAsync(going, member.Id, ct).ConfigureAwait(false) == null, "A ban removes RSVPs on upcoming events");
            await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/admin/members/" + member.Id + "/unban", HttpFixture.AdminToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            TestMember other = await SafetyHelpers.MemberAsync(f, ct, "Deleter").ConfigureAwait(false);
            JsonElement otherFeed = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/me/calendar-feed", other.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            await Api.ExpectAsync(f, HttpMethod.Delete, "/api/v1/me/calendar-feed", other.AccessToken, null, HttpStatusCode.NoContent, ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Get, new Uri(Api.Str(otherFeed, "url")).AbsolutePath, null, null, HttpStatusCode.NotFound, "NotFound", ct).ConfigureAwait(false);
            f.Settings.Events.CalendarFeedEnabled = false;
            await Api.ExpectErrorAsync(f, HttpMethod.Get, "/api/v1/me/calendar-feed", other.AccessToken, null, HttpStatusCode.NotFound, "NotAvailable", ct).ConfigureAwait(false);
        }

        #endregion
    }
}
