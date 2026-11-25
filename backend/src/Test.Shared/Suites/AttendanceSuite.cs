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
    using TrellisKit.Server.Services;

    /// <summary>
    /// Attendance (docs/TRELLISKIT.md, Ranks, curricula, and attendance): rotating codes, the check-in window, duplicate
    /// check-ins, staff check-in, venue session check-in windows across time zones, and roster bounds.
    /// </summary>
    public static class AttendanceSuite
    {
        #region Public-Members

        /// <summary>
        /// Suite identifier.
        /// </summary>
        public const string Id = "Attendance";

        #endregion

        #region Private-Members

        private const string _CheckIn = "/api/v1/attendance/check-in";
        private const string _Code = "/api/v1/attendance/check-in-code";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the suite.
        /// </summary>
        /// <returns>Suite.</returns>
        public static TestSuiteDescriptor Build()
        {
            return new TestSuiteDescriptor(Id, "Attendance (" + TestEnvironment.Provider + ")", new[]
            {
                SuiteCases.Unit(Id, "Codes.Rotation", "Codes are six digits, differ per target and date, rotate per period, and the previous period's code is still accepted", CodesAsync),
                SuiteCases.Http(Id, "CheckIn.CodeAndDuplicate", "Staff read the rotating code; members check in with it (wrong code 400 InvalidCode); a second check-in is 409 AlreadyCheckedIn", CodeAndDuplicateAsync),
                SuiteCases.Http(Id, "CheckIn.Window", "Members check in only inside the window around the event start (422 OutsideCheckInWindow); staff may record late attendance", WindowAsync),
                SuiteCases.Http(Id, "CheckIn.Staff", "Rank awarders check members in without a code (method Staff); members cannot check others in; staff checking themselves need a code", StaffAsync),
                SuiteCases.Http(Id, "CheckIn.VenueSessionsAcrossTimeZones", "Venue sessions check in on dates whose weekday matches, with the window measured from the session start in the venue's time zone", VenueSessionsAsync),
                SuiteCases.Http(Id, "Roster.Bounds", "The staff roster and my attendance are bounded enumerations with filters, continuation, and staff-only access; staff can remove a check-in", RosterAsync)
            });
        }

        /// <summary>
        /// A published event that starts now.
        /// </summary>
        /// <param name="f">Fixture.</param>
        /// <param name="ct">Cancellation token.</param>
        /// <param name="startOffsetMinutes">Minutes from now to the start.</param>
        /// <returns>Event identifier.</returns>
        internal static async Task<string> EventNowAsync(HttpFixture f, CancellationToken ct, int startOffsetMinutes = 0)
        {
            EventTestKit.Relax(f);
            string id = await EventTestKit.PublishedAsync(f, HttpFixture.AdminToken, "Open session " + Guid.NewGuid().ToString("N").Substring(0, 6), ct).ConfigureAwait(false);
            await RankTestKit.SqlAsync(f, "UPDATE events SET startutc = NOW() + INTERVAL '" + startOffsetMinutes + " minutes', endutc = NOW() + INTERVAL '" + (startOffsetMinutes + 120) + " minutes' WHERE propertyid = '" +
                RankTestKit.Pid(f) + "' AND id = '" + id + "';", ct).ConfigureAwait(false);
            return id;
        }

        #endregion

        #region Private-Methods

        private static Task CodesAsync(CancellationToken ct)
        {
            CheckInTarget a = new CheckInTarget { TargetType = AttendanceTargetTypeEnum.Event, TargetId = "evt_a", OccurrenceDate = "2026-03-01" };
            CheckInTarget b = new CheckInTarget { TargetType = AttendanceTargetTypeEnum.Event, TargetId = "evt_a", OccurrenceDate = "2026-03-02" };
            DateTime now = new DateTime(2026, 3, 1, 12, 0, 30, DateTimeKind.Utc);
            long period = CheckInCodes.Period(now, 60);
            string code = CheckInCodes.Code("secret", "prop", a, period);
            Check.True(code.Length == 6 && code.All(Char.IsDigit), "Six digits: " + code);
            Check.True(code != CheckInCodes.Code("secret", "prop", b, period) || code != CheckInCodes.Code("secret", "prop", b, period + 1), "Differs per occurrence date");
            Check.True(code != CheckInCodes.Code("other", "prop", a, period) || code != CheckInCodes.Code("other", "prop", a, period + 1), "Differs per key");
            Check.True(CheckInCodes.Matches("secret", "prop", a, code, now, 60), "Current period accepted");
            Check.True(CheckInCodes.Matches("secret", "prop", a, code, now.AddSeconds(60), 60), "Previous period accepted");
            Check.True(!CheckInCodes.Matches("secret", "prop", a, code, now.AddSeconds(150), 60) || CheckInCodes.Code("secret", "prop", a, period + 2) == code, "Two periods later refused");
            Check.True(!CheckInCodes.Matches("secret", "prop", a, "12345", now, 60), "Wrong length refused");
            Check.True(!CheckInCodes.Matches("secret", "other-property", a, code, now, 60) || CheckInCodes.Code("secret", "other-property", a, period) == code, "Property bound");
            Check.Equal(period + 1, CheckInCodes.Period(now.AddSeconds(30), 60), "Periods advance with rotation");
            return Task.CompletedTask;
        }

        private static async Task<string> CodeAsync(HttpFixture f, TestMember staff, string targetType, string targetId, string? date, CancellationToken ct)
        {
            string query = "?targetType=" + targetType + "&targetId=" + Uri.EscapeDataString(targetId) + (date == null ? String.Empty : "&occurrenceDate=" + date);
            JsonElement code = await Api.ExpectAsync(f, HttpMethod.Get, _Code + query, staff.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            return Api.Str(code, "code");
        }

        private static async Task CodeAndDuplicateAsync(HttpFixture f, CancellationToken ct)
        {
            RankTestKit.Enable(f);
            TestMember staff = await RankTestKit.AwarderAsync(f, ct).ConfigureAwait(false);
            TestMember member = await Api.MemberAsync(f, ct).ConfigureAwait(false);
            string eventId = await EventNowAsync(f, ct, 10).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Get, _Code + "?targetType=Event&targetId=" + eventId, member.AccessToken, null, HttpStatusCode.Forbidden, "Forbidden", ct).ConfigureAwait(false);
            JsonElement shown = await Api.ExpectAsync(f, HttpMethod.Get, _Code + "?targetType=Event&targetId=" + eventId, staff.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            string code = Api.Str(shown, "code");
            Check.True(code.Length == 6, "Six-digit code");
            Check.True(Api.Str(shown, "qrPayload").Contains("code=" + code, StringComparison.Ordinal), "QR payload carries the code");
            Check.Equal(60, shown.GetProperty("rotationSeconds").GetInt32(), "Default rotation");
            Check.True(shown.GetProperty("expiresUtc").GetDateTime() > DateTime.UtcNow.AddSeconds(-1), "Expires in the future");
            string wrong = code == "000000" ? "111111" : "000000";
            await Api.ExpectErrorAsync(f, HttpMethod.Post, _CheckIn, member.AccessToken, new { targetType = "Event", targetId = eventId, code = wrong }, HttpStatusCode.BadRequest, "InvalidCode", ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Post, _CheckIn, member.AccessToken, new { targetType = "Event", targetId = eventId }, HttpStatusCode.BadRequest, "ValidationFailed", ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Post, _CheckIn, member.AccessToken, new { targetType = "Class", targetId = eventId, code }, HttpStatusCode.BadRequest, "ValidationFailed", ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Post, _CheckIn, member.AccessToken, new { targetType = "Event", targetId = "evt_unknown", code }, HttpStatusCode.NotFound, "NotFound", ct).ConfigureAwait(false);
            JsonElement record = await Api.ExpectAsync(f, HttpMethod.Post, _CheckIn, member.AccessToken, new { targetType = "Event", targetId = eventId, code, method = "QR", labels = new[] { "morning" } }, HttpStatusCode.Created, ct).ConfigureAwait(false);
            Check.Equal("QR", Api.Str(record, "method"), "Method QR");
            Check.Equal(member.Id, Api.Str(record, "userId"), "Checked in");
            await Api.ExpectErrorAsync(f, HttpMethod.Post, _CheckIn, member.AccessToken, new { targetType = "Event", targetId = eventId, code }, HttpStatusCode.Conflict, "AlreadyCheckedIn", ct).ConfigureAwait(false);
            string pid = RankTestKit.Pid(f);
            Check.Equal(1L, await RankTestKit.CountAsync(f, "SELECT COUNT(*) FROM auditrecords WHERE propertyid = '" + pid + "' AND action = 'attendance.checkIn' AND targetid = '" + member.Id + "'", ct).ConfigureAwait(false), "Audited attendance.checkIn");
        }

        private static async Task WindowAsync(HttpFixture f, CancellationToken ct)
        {
            RankTestKit.Enable(f);
            TestMember staff = await RankTestKit.AwarderAsync(f, ct).ConfigureAwait(false);
            TestMember member = await Api.MemberAsync(f, ct).ConfigureAwait(false);
            string later = await EventNowAsync(f, ct, 90).ConfigureAwait(false);
            string code = await CodeAsync(f, staff, "Event", later, null, ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Post, _CheckIn, member.AccessToken, new { targetType = "Event", targetId = later, code }, HttpStatusCode.UnprocessableEntity, "OutsideCheckInWindow", ct).ConfigureAwait(false);
            f.Settings.Attendance.CheckInWindowBeforeMinutes = 120;
            await Api.ExpectAsync(f, HttpMethod.Post, _CheckIn, member.AccessToken, new { targetType = "Event", targetId = later, code }, HttpStatusCode.Created, ct).ConfigureAwait(false);
            f.Settings.Attendance.CheckInWindowBeforeMinutes = 60;
            string past = await EventNowAsync(f, ct, -180).ConfigureAwait(false);
            code = await CodeAsync(f, staff, "Event", past, null, ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Post, _CheckIn, member.AccessToken, new { targetType = "Event", targetId = past, code }, HttpStatusCode.UnprocessableEntity, "OutsideCheckInWindow", ct).ConfigureAwait(false);
            await Api.ExpectAsync(f, HttpMethod.Post, _CheckIn, staff.AccessToken, new { targetType = "Event", targetId = past, userId = member.Id }, HttpStatusCode.Created, ct).ConfigureAwait(false);
            string future = await EventNowAsync(f, ct, 600).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Post, _CheckIn, staff.AccessToken, new { targetType = "Event", targetId = future, userId = member.Id }, HttpStatusCode.UnprocessableEntity, "OutsideCheckInWindow", ct).ConfigureAwait(false);
            EventRecordStart start = await StartAsync(f, past, ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Post, _CheckIn, staff.AccessToken, new { targetType = "Event", targetId = past, occurrenceDate = start.OtherDate, userId = member.Id }, HttpStatusCode.BadRequest, "ValidationFailed", ct).ConfigureAwait(false);
        }

        private static async Task StaffAsync(HttpFixture f, CancellationToken ct)
        {
            RankTestKit.Enable(f);
            TestMember staff = await RankTestKit.AwarderAsync(f, ct).ConfigureAwait(false);
            TestMember member = await Api.MemberAsync(f, ct).ConfigureAwait(false);
            TestMember other = await Api.MemberAsync(f, ct).ConfigureAwait(false);
            string eventId = await EventNowAsync(f, ct).ConfigureAwait(false);
            JsonElement record = await Api.ExpectAsync(f, HttpMethod.Post, _CheckIn, staff.AccessToken, new { targetType = "Event", targetId = eventId, userId = member.Id }, HttpStatusCode.Created, ct).ConfigureAwait(false);
            Check.Equal("Staff", Api.Str(record, "method"), "Method Staff");
            Check.Equal(staff.Id, Api.Str(record, "checkedInBy"), "Checked in by");
            await Api.ExpectErrorAsync(f, HttpMethod.Post, _CheckIn, staff.AccessToken, new { targetType = "Event", targetId = eventId, userId = member.Id }, HttpStatusCode.Conflict, "AlreadyCheckedIn", ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Post, _CheckIn, other.AccessToken, new { targetType = "Event", targetId = eventId, userId = member.Id }, HttpStatusCode.Forbidden, "Forbidden", ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Post, _CheckIn, staff.AccessToken, new { targetType = "Event", targetId = eventId, userId = staff.Id }, HttpStatusCode.BadRequest, "ValidationFailed", ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Post, _CheckIn, staff.AccessToken, new { targetType = "Event", targetId = eventId, userId = "usr_unknown" }, HttpStatusCode.NotFound, "NotFound", ct).ConfigureAwait(false);
            string code = await CodeAsync(f, staff, "Event", eventId, null, ct).ConfigureAwait(false);
            await Api.ExpectAsync(f, HttpMethod.Post, _CheckIn, staff.AccessToken, new { targetType = "Event", targetId = eventId, code }, HttpStatusCode.Created, ct).ConfigureAwait(false);
        }

        private static async Task<string> VenueAsync(HttpFixture f, string zoneId, List<DateTime> sessionLocals, CancellationToken ct)
        {
            DirectoryTestKit.Relax(f);
            List<object> sessions = sessionLocals.Select(local =>
            {
                DateTime end = local.AddHours(1);
                return (object)new
                {
                    dayOfWeek = ((int)local.DayOfWeek + 6) % 7,
                    startTime = local.ToString("HH:mm", CultureInfo.InvariantCulture),
                    endTime = end.ToString("HH:mm", CultureInfo.InvariantCulture),
                    endsNextDay = end.Date != local.Date,
                    name = "Open session"
                };
            }).ToList();
            return await DirectoryTestKit.VenueIdAsync(f, "Training Hall " + Guid.NewGuid().ToString("N").Substring(0, 6), ct, 21.3, -157.8,
                new { schedule = new { timeZone = zoneId, sessions } }).ConfigureAwait(false);
        }

        private static async Task VenueSessionsAsync(HttpFixture f, CancellationToken ct)
        {
            RankTestKit.Enable(f);
            TestMember staff = await RankTestKit.AwarderAsync(f, ct).ConfigureAwait(false);
            TestMember member = await Api.MemberAsync(f, ct).ConfigureAwait(false);
            foreach (string zoneId in new[] { "Pacific/Honolulu", "Asia/Tokyo" })
            {
                TimeZoneInfo zone = TimeZoneInfo.FindSystemTimeZoneById(zoneId);
                DateTime localNow = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, zone);
                DateTime soon = new DateTime(localNow.Year, localNow.Month, localNow.Day, localNow.Hour, localNow.Minute, 0).AddMinutes(-5);
                DateTime later = soon.AddHours(5);
                string venueId = await VenueAsync(f, zoneId, new List<DateTime> { soon, later }, ct).ConfigureAwait(false);
                string date = soon.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                string laterDate = later.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                string session = venueId + ":0";
                JsonElement shown = await Api.ExpectAsync(f, HttpMethod.Get, _Code + "?targetType=VenueSession&targetId=" + Uri.EscapeDataString(session) + "&occurrenceDate=" + date, staff.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                DateTime startUtc = TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(soon, DateTimeKind.Unspecified), zone);
                Check.True(Math.Abs((shown.GetProperty("windowOpensUtc").GetDateTime().ToUniversalTime() - startUtc.AddMinutes(-60)).TotalSeconds) < 1, zoneId + ": window measured from the local session start");
                string code = Api.Str(shown, "code");
                await Api.ExpectErrorAsync(f, HttpMethod.Post, _CheckIn, member.AccessToken, new { targetType = "VenueSession", targetId = session, code }, HttpStatusCode.BadRequest, "ValidationFailed", ct).ConfigureAwait(false);
                string wrongDay = soon.AddDays(1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                await Api.ExpectErrorAsync(f, HttpMethod.Post, _CheckIn, member.AccessToken, new { targetType = "VenueSession", targetId = session, occurrenceDate = wrongDay, code }, HttpStatusCode.BadRequest, "ValidationFailed", ct).ConfigureAwait(false);
                await Api.ExpectErrorAsync(f, HttpMethod.Post, _CheckIn, member.AccessToken, new { targetType = "VenueSession", targetId = venueId + ":9", occurrenceDate = date, code }, HttpStatusCode.NotFound, "NotFound", ct).ConfigureAwait(false);
                JsonElement record = await Api.ExpectAsync(f, HttpMethod.Post, _CheckIn, member.AccessToken, new { targetType = "VenueSession", targetId = session, occurrenceDate = date, code }, HttpStatusCode.Created, ct).ConfigureAwait(false);
                Check.Equal(date, Api.Str(record, "occurrenceDate"), zoneId + ": local occurrence date");
                await Api.ExpectErrorAsync(f, HttpMethod.Post, _CheckIn, member.AccessToken, new { targetType = "VenueSession", targetId = session, occurrenceDate = date, code }, HttpStatusCode.Conflict, "AlreadyCheckedIn", ct).ConfigureAwait(false);
                string laterCode = await CodeAsync(f, staff, "VenueSession", venueId + ":1", laterDate, ct).ConfigureAwait(false);
                await Api.ExpectErrorAsync(f, HttpMethod.Post, _CheckIn, member.AccessToken, new { targetType = "VenueSession", targetId = venueId + ":1", occurrenceDate = laterDate, code = laterCode }, HttpStatusCode.UnprocessableEntity, "OutsideCheckInWindow", ct).ConfigureAwait(false);
            }
        }

        private static async Task RosterAsync(HttpFixture f, CancellationToken ct)
        {
            RankTestKit.Enable(f);
            TestMember staff = await RankTestKit.AwarderAsync(f, ct).ConfigureAwait(false);
            List<TestMember> members = new List<TestMember>();
            for (int i = 0; i < 3; i++) members.Add(await Api.MemberAsync(f, ct).ConfigureAwait(false));
            string eventId = await EventNowAsync(f, ct).ConfigureAwait(false);
            string other = await EventNowAsync(f, ct).ConfigureAwait(false);
            foreach (TestMember m in members) await Api.ExpectAsync(f, HttpMethod.Post, _CheckIn, staff.AccessToken, new { targetType = "Event", targetId = eventId, userId = m.Id }, HttpStatusCode.Created, ct).ConfigureAwait(false);
            await Api.ExpectAsync(f, HttpMethod.Post, _CheckIn, staff.AccessToken, new { targetType = "Event", targetId = other, userId = members[0].Id }, HttpStatusCode.Created, ct).ConfigureAwait(false);
            JsonElement page = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/admin/attendance/enumerate", staff.AccessToken, new { maxResults = 2, filters = new { targetType = "Event", targetId = eventId } }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal(2, page.GetProperty("objects").GetArrayLength(), "Bounded page");
            Check.True(page.GetProperty("objects")[0].GetProperty("user").ValueKind == JsonValueKind.Object, "Member summaries");
            JsonElement next = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/admin/attendance/enumerate", staff.AccessToken, new { maxResults = 2, continuationToken = Api.Str(page, "continuationToken"), filters = new { targetType = "Event", targetId = eventId } }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal(1, next.GetProperty("objects").GetArrayLength(), "Continuation returns the rest");
            JsonElement got = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/admin/attendance?targetId=" + other, staff.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal(1, got.GetProperty("objects").GetArrayLength(), "GET form with a filter");
            await Api.ExpectErrorAsync(f, HttpMethod.Post, "/api/v1/admin/attendance/enumerate", staff.AccessToken, new { filters = new { occurrenceDate = "March 1" } }, HttpStatusCode.BadRequest, "ValidationFailed", ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Post, "/api/v1/admin/attendance/enumerate", members[0].AccessToken, new { }, HttpStatusCode.Forbidden, "Forbidden", ct).ConfigureAwait(false);
            JsonElement mine = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/me/attendance/enumerate", members[0].AccessToken, new { }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal(2, mine.GetProperty("objects").GetArrayLength(), "My attendance");
            string removed = Api.Str(mine.GetProperty("objects")[0], "id");
            await Api.ExpectErrorAsync(f, HttpMethod.Delete, "/api/v1/admin/attendance/" + removed, members[1].AccessToken, null, HttpStatusCode.Forbidden, "Forbidden", ct).ConfigureAwait(false);
            await Api.ExpectAsync(f, HttpMethod.Delete, "/api/v1/admin/attendance/" + removed, staff.AccessToken, null, HttpStatusCode.NoContent, ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Delete, "/api/v1/admin/attendance/" + removed, staff.AccessToken, null, HttpStatusCode.NotFound, "NotFound", ct).ConfigureAwait(false);
            mine = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/me/attendance", members[0].AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal(1, mine.GetProperty("objects").GetArrayLength(), "Removed check-in gone");
        }

        private static async Task<EventRecordStart> StartAsync(HttpFixture f, string eventId, CancellationToken ct)
        {
            TrellisKit.Core.Models.EventRecord record = await EventTestKit.RecordAsync(f, eventId, ct).ConfigureAwait(false);
            TimeZoneInfo zone = TimeZoneInfo.FindSystemTimeZoneById(record.TimeZone);
            DateTime local = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(record.StartUtc, DateTimeKind.Utc), zone);
            return new EventRecordStart { Date = local.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), OtherDate = local.AddDays(1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) };
        }

        #endregion
    }
}
