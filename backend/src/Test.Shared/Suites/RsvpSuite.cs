namespace Test.Shared.Suites
{
    using System;
    using System.Collections.Generic;
    using System.Data;
    using System.Linq;
    using System.Net;
    using System.Net.Http;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Touchstone.Core;
    using TrellisKit.Core.Database;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Models;

    /// <summary>
    /// RSVPs (docs/PHASE4_CONTRACT.md section 4.3): capacity with guests, parallel Going requests on a small capacity
    /// never overbook, exact waitlist order, promotion when space opens (cancellation, capacity raise, staff actions)
    /// for the oldest parties that fit, the waitlist cutoff, the RSVP window, and the per-member rules.
    /// </summary>
    public static class RsvpSuite
    {
        #region Public-Members

        /// <summary>
        /// Suite identifier.
        /// </summary>
        public const string Id = "Rsvps";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the suite.
        /// </summary>
        /// <returns>Suite.</returns>
        public static TestSuiteDescriptor Build()
        {
            return new TestSuiteDescriptor(Id, "RSVPs (" + TestEnvironment.Provider + ")", new[]
            {
                Case("Rsvps.CapacityWithGuests", "Going plus guests count against capacity; a party that does not fit is waitlisted; Interested never counts; guests are bounded; a Going member cannot add guests beyond the spots left", CapacityAsync),
                Case("Rsvps.Concurrency", "Thirty parallel Going requests on capacity 5 leave exactly 5 Going and 25 Waitlisted with exact positions 1 to 25 in lock order; counters match the rows; cancellations promote the oldest waitlisted members exactly", ConcurrencyAsync),
                Case("Rsvps.PromotionFit", "Promotion takes the oldest waitlisted parties that fit (a smaller later party passes a larger one), on cancellation and on a capacity raise, and notifies EventWaitlistPromoted with the Essential email", PromotionAsync),
                Case("Rsvps.Cutoff", "Inside events.waitlistCutoffMinutes before the start no promotion runs and freed spots stay open for anyone", CutoffAsync),
                Case("Rsvps.Window", "RSVPs outside the window are 409 RsvpClosed; deleting an RSVP after the start is 409 RsvpClosed; a full event without a waitlist is 409 EventFull", WindowAsync),
                Case("Rsvps.Rules", "Muted members get 403 Muted, rsvpAudience Members refuses free accounts with 403 RsvpNotAllowed, events.maxActiveRsvpsPerMember applies, hideFromAttendeeLists hides members from others but not staff, attendeeVisibility applies", RulesAsync),
                Case("Rsvps.StaffActions", "Event staff promote (even beyond capacity), move to the waitlist, remove, and check in RSVPs with counters exact and actions audited", StaffAsync)
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

        private static async Task CapacityAsync(HttpFixture f, CancellationToken ct)
        {
            TestMember admin = await Api.AdminAsync(f, ct).ConfigureAwait(false);
            List<TestMember> m = await EventTestKit.MembersAsync(f, 4, ct).ConfigureAwait(false);
            string id = await EventTestKit.PublishedAsync(f, admin.AccessToken, "Small room", ct, new { capacity = 3, maxGuestsPerRsvp = 1 }).ConfigureAwait(false);
            JsonElement a = await EventTestKit.RsvpAsync(f, m[0].AccessToken, id, "Going", ct, 1).ConfigureAwait(false);
            Check.Equal("Going", EventTestKit.MyStatus(a), "A goes with a guest");
            Check.Equal(1, a.GetProperty("spotsLeft").GetInt32(), "One spot left");
            JsonElement b = await EventTestKit.RsvpAsync(f, m[1].AccessToken, id, "Going", ct, 1).ConfigureAwait(false);
            Check.Equal("Waitlisted", EventTestKit.MyStatus(b), "B's party of two does not fit");
            Check.Equal(1, b.GetProperty("myRsvp").GetProperty("position").GetInt32(), "B is first on the waitlist");
            JsonElement c = await EventTestKit.RsvpAsync(f, m[2].AccessToken, id, "Going", ct).ConfigureAwait(false);
            Check.Equal("Going", EventTestKit.MyStatus(c), "C alone fits");
            Check.Equal(0, c.GetProperty("spotsLeft").GetInt32(), "Full");
            JsonElement d = await EventTestKit.RsvpAsync(f, m[3].AccessToken, id, "Interested", ct).ConfigureAwait(false);
            Check.Equal("Interested", EventTestKit.MyStatus(d), "Interested never waitlists");
            Check.Equal(1, d.GetProperty("interestedCount").GetInt32(), "interestedCount");
            await Api.ExpectErrorAsync(f, HttpMethod.Put, "/api/v1/events/" + id + "/rsvp", m[2].AccessToken, new { status = "Going", guestCount = 1 }, HttpStatusCode.Conflict, "EventFull", ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Put, "/api/v1/events/" + id + "/rsvp", m[3].AccessToken, new { status = "Going", guestCount = 2 }, HttpStatusCode.BadRequest, "ValidationFailed", ct).ConfigureAwait(false);
            EventRecord e = await EventTestKit.RecordAsync(f, id, ct).ConfigureAwait(false);
            Check.Equal(2, e.GoingCount, "goingcount");
            Check.Equal(1, e.GuestCount, "guestcount");
            Check.Equal(1, e.WaitlistCount, "waitlistcount");
            JsonElement still = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/events/" + id, m[2].AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal(0, still.GetProperty("myRsvp").GetProperty("guestCount").GetInt32(), "The refused change left the RSVP as it was");
        }

        private static async Task ConcurrencyAsync(HttpFixture f, CancellationToken ct)
        {
            TestMember admin = await Api.AdminAsync(f, ct).ConfigureAwait(false);
            List<TestMember> members = await EventTestKit.MembersAsync(f, 30, ct).ConfigureAwait(false);
            string id = await EventTestKit.PublishedAsync(f, admin.AccessToken, "Popular class", ct, new { capacity = 5 }).ConfigureAwait(false);
            List<Task<ApiResult>> calls = members.Select(m => Api.SendAsync(f, HttpMethod.Put, "/api/v1/events/" + id + "/rsvp", m.AccessToken, new { status = "Going" }, ct)).ToList();
            ApiResult[] results = await Task.WhenAll(calls).ConfigureAwait(false);
            Check.True(results.All(r => r.Status == HttpStatusCode.OK), "Every request succeeded: " + String.Join(",", results.Select(r => (int)r.Status)));
            DataTable rows = await f.Raw.ExecuteQueryAsync("SELECT userid, status, waitlistedutc, id FROM eventrsvps WHERE eventid = '" + id + "' ORDER BY waitlistedutc NULLS FIRST, id;", false, ct).ConfigureAwait(false);
            List<DataRow> all = rows.Rows.Cast<DataRow>().ToList();
            Check.Equal(5, all.Count(r => RowReader.GetString(r, "status") == "Going"), "Exactly capacity Going");
            Check.Equal(25, all.Count(r => RowReader.GetString(r, "status") == "Waitlisted"), "The rest waitlisted");
            EventRecord e = await EventTestKit.RecordAsync(f, id, ct).ConfigureAwait(false);
            Check.Equal(5, e.GoingCount, "goingcount matches");
            Check.Equal(25, e.WaitlistCount, "waitlistcount matches");
            List<DataRow> waitlist = all.Where(r => RowReader.GetString(r, "status") == "Waitlisted").ToList();
            Dictionary<string, int> expected = new Dictionary<string, int>(StringComparer.Ordinal);
            for (int i = 0; i < waitlist.Count; i++) expected[RowReader.GetString(waitlist[i], "userid")] = i + 1;
            Check.Equal(25, waitlist.Select(r => RowReader.GetDateTime(r, "waitlistedutc")).Distinct().Count(), "Waitlist times are distinct (lock order)");
            foreach (ApiResult r in results.Where(r => Api.Str(r.Body.GetProperty("myRsvp"), "status") == "Waitlisted"))
            {
                JsonElement mine = r.Body.GetProperty("myRsvp");
                string user = members[Array.IndexOf(results, r)].Id;
                Check.Equal(expected[user], mine.GetProperty("position").GetInt32(), "Position returned to each caller matches the queue");
            }
            HashSet<string> going = new HashSet<string>(all.Where(r => RowReader.GetString(r, "status") == "Going").Select(r => RowReader.GetString(r, "userid")), StringComparer.Ordinal);
            List<TestMember> leaving = members.Where(m => going.Contains(m.Id)).Take(2).ToList();
            List<string> nextTwo = waitlist.Take(2).Select(r => RowReader.GetString(r, "userid")).ToList();
            await Task.WhenAll(leaving.Select(m => Api.ExpectAsync(f, HttpMethod.Delete, "/api/v1/events/" + id + "/rsvp", m.AccessToken, null, HttpStatusCode.OK, ct))).ConfigureAwait(false);
            DataTable after = await f.Raw.ExecuteQueryAsync("SELECT userid, status FROM eventrsvps WHERE eventid = '" + id + "';", false, ct).ConfigureAwait(false);
            HashSet<string> nowGoing = new HashSet<string>(after.Rows.Cast<DataRow>().Where(r => RowReader.GetString(r, "status") == "Going").Select(r => RowReader.GetString(r, "userid")), StringComparer.Ordinal);
            Check.Equal(5, nowGoing.Count, "Back to capacity");
            Check.True(nextTwo.All(nowGoing.Contains), "The two oldest waitlisted members were promoted");
            TestMember third = members.First(m => m.Id == RowReader.GetString(waitlist[2], "userid"));
            JsonElement view = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/events/" + id, third.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal(1, view.GetProperty("myRsvp").GetProperty("position").GetInt32(), "Positions renumber after promotion");
            Check.Equal(23, (await EventTestKit.RecordAsync(f, id, ct).ConfigureAwait(false)).WaitlistCount, "waitlistcount after promotion");
            Check.Equal(1, (await EventTestKit.NotificationsAsync(f, nextTwo[0], NotificationTypeEnum.EventWaitlistPromoted, ct).ConfigureAwait(false)).Count, "Promoted members are told");
        }

        private static async Task PromotionAsync(HttpFixture f, CancellationToken ct)
        {
            TestMember admin = await Api.AdminAsync(f, ct).ConfigureAwait(false);
            List<TestMember> m = await EventTestKit.MembersAsync(f, 5, ct).ConfigureAwait(false);
            string id = await EventTestKit.PublishedAsync(f, admin.AccessToken, "Party sizes", ct, new { capacity = 2, maxGuestsPerRsvp = 2 }).ConfigureAwait(false);
            await EventTestKit.RsvpAsync(f, m[0].AccessToken, id, "Going", ct).ConfigureAwait(false);
            await EventTestKit.RsvpAsync(f, m[1].AccessToken, id, "Going", ct).ConfigureAwait(false);
            JsonElement big = await EventTestKit.RsvpAsync(f, m[2].AccessToken, id, "Going", ct, 2).ConfigureAwait(false);
            Check.Equal("Waitlisted", EventTestKit.MyStatus(big), "Party of three waits");
            JsonElement small = await EventTestKit.RsvpAsync(f, m[3].AccessToken, id, "Going", ct).ConfigureAwait(false);
            Check.Equal(2, small.GetProperty("myRsvp").GetProperty("position").GetInt32(), "Second in line");
            int mails = f.Fakes.Email.Messages.Count;
            await Api.ExpectAsync(f, HttpMethod.Delete, "/api/v1/events/" + id + "/rsvp", m[0].AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            JsonElement smallAfter = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/events/" + id, m[3].AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal("Going", EventTestKit.MyStatus(smallAfter), "The smaller party that fits is promoted");
            JsonElement bigAfter = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/events/" + id, m[2].AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal("Waitlisted", EventTestKit.MyStatus(bigAfter), "The larger party keeps waiting");
            Check.Equal(1, bigAfter.GetProperty("myRsvp").GetProperty("position").GetInt32(), "Now first");
            Check.True(f.Fakes.Email.Messages.Skip(mails).Any(x => x.To == m[3].Email), "Essential promotion email");
            List<Notification> promoted = await EventTestKit.NotificationsAsync(f, m[3].Id, NotificationTypeEnum.EventWaitlistPromoted, ct).ConfigureAwait(false);
            Check.True(promoted.Count == 1 && promoted[0].Title.StartsWith("A spot opened", StringComparison.Ordinal), "EventWaitlistPromoted");
            await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/admin/events/" + id, admin.AccessToken, new { capacity = 5 }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            JsonElement raised = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/events/" + id, m[2].AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal("Going", EventTestKit.MyStatus(raised), "A capacity raise promotes");
            Check.Equal(0, raised.GetProperty("spotsLeft").GetInt32(), "5 of 5 used");
            await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/admin/events/" + id, admin.AccessToken, new { capacity = (int?)null }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            JsonElement unlimited = await EventTestKit.RsvpAsync(f, m[4].AccessToken, id, "Going", ct, 2).ConfigureAwait(false);
            Check.Equal("Going", EventTestKit.MyStatus(unlimited), "Unlimited capacity");
            Check.True(unlimited.GetProperty("spotsLeft").ValueKind == JsonValueKind.Null, "spotsLeft null when unlimited");
        }

        private static async Task CutoffAsync(HttpFixture f, CancellationToken ct)
        {
            TestMember admin = await Api.AdminAsync(f, ct).ConfigureAwait(false);
            List<TestMember> m = await EventTestKit.MembersAsync(f, 3, ct).ConfigureAwait(false);
            string id = await EventTestKit.PublishedAsync(f, admin.AccessToken, "Late changes", ct, new { capacity = 1 }).ConfigureAwait(false);
            await EventTestKit.RsvpAsync(f, m[0].AccessToken, id, "Going", ct).ConfigureAwait(false);
            await EventTestKit.RsvpAsync(f, m[1].AccessToken, id, "Going", ct).ConfigureAwait(false);
            EventRecord e = await EventTestKit.RecordAsync(f, id, ct).ConfigureAwait(false);
            EventTestKit.Clock(f, e.StartUtc.AddMinutes(-30));
            await Api.ExpectAsync(f, HttpMethod.Delete, "/api/v1/events/" + id + "/rsvp", m[0].AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            JsonElement waiting = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/events/" + id, m[1].AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal("Waitlisted", EventTestKit.MyStatus(waiting), "No promotion inside the cutoff");
            JsonElement walkIn = await EventTestKit.RsvpAsync(f, m[2].AccessToken, id, "Going", ct).ConfigureAwait(false);
            Check.Equal("Going", EventTestKit.MyStatus(walkIn), "The freed spot is open for anyone");
        }

        private static async Task WindowAsync(HttpFixture f, CancellationToken ct)
        {
            TestMember admin = await Api.AdminAsync(f, ct).ConfigureAwait(false);
            TestMember member = await SafetyHelpers.MemberAsync(f, ct).ConfigureAwait(false);
            TestMember other = await SafetyHelpers.MemberAsync(f, ct, "Second").ConfigureAwait(false);
            DateTime opens = DateTime.UtcNow.AddDays(2);
            string later = await EventTestKit.PublishedAsync(f, admin.AccessToken, "Opens later", ct, new { rsvpOpensUtc = opens }).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Put, "/api/v1/events/" + later + "/rsvp", member.AccessToken, new { status = "Going" }, HttpStatusCode.Conflict, "RsvpClosed", ct).ConfigureAwait(false);
            JsonElement view = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/events/" + later, member.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.True(!view.GetProperty("viewer").GetProperty("canRsvp").GetBoolean(), "viewer.canRsvp false before the window");
            EventTestKit.Clock(f, opens.AddMinutes(1));
            await EventTestKit.RsvpAsync(f, member.AccessToken, later, "Going", ct).ConfigureAwait(false);
            EventRecord e = await EventTestKit.RecordAsync(f, later, ct).ConfigureAwait(false);
            EventTestKit.Clock(f, e.StartUtc.AddMinutes(5));
            await Api.ExpectErrorAsync(f, HttpMethod.Put, "/api/v1/events/" + later + "/rsvp", other.AccessToken, new { status = "Going" }, HttpStatusCode.Conflict, "RsvpClosed", ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Delete, "/api/v1/events/" + later + "/rsvp", member.AccessToken, null, HttpStatusCode.Conflict, "RsvpClosed", ct).ConfigureAwait(false);
            EventTestKit.Clock(f, DateTime.UtcNow);
            string noWaitlist = await EventTestKit.PublishedAsync(f, admin.AccessToken, "No waitlist", ct, new { capacity = 1, waitlistEnabled = false }).ConfigureAwait(false);
            await EventTestKit.RsvpAsync(f, member.AccessToken, noWaitlist, "Going", ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Put, "/api/v1/events/" + noWaitlist + "/rsvp", other.AccessToken, new { status = "Going" }, HttpStatusCode.Conflict, "EventFull", ct).ConfigureAwait(false);
            JsonElement full = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/events/" + noWaitlist, other.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.True(full.GetProperty("viewer").GetProperty("canRsvp").GetBoolean() && !full.GetProperty("viewer").GetProperty("canRsvpGoing").GetBoolean(), "canRsvpGoing false when full without a waitlist");
        }

        private static async Task RulesAsync(HttpFixture f, CancellationToken ct)
        {
            await EventTestKit.PartiallyGatedAsync(f, ct).ConfigureAwait(false);
            TestMember admin = await Api.AdminAsync(f, ct).ConfigureAwait(false);
            TestMember member = await SafetyHelpers.MemberAsync(f, ct, "Shy Member").ConfigureAwait(false);
            TestMember other = await SafetyHelpers.MemberAsync(f, ct, "Curious Member").ConfigureAwait(false);
            TestMember free = await Api.RegisterAsync(f, ct).ConfigureAwait(false);
            string id = await EventTestKit.PublishedAsync(f, admin.AccessToken, "Members only RSVPs", ct, new { rsvpAudience = "Members", attendeeVisibility = "Everyone" }).ConfigureAwait(false);
            JsonElement asFree = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/events/" + id, free.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.True(!asFree.GetProperty("viewer").GetProperty("canRsvp").GetBoolean(), "Free accounts cannot RSVP");
            Check.Equal("Membership", Api.Str(asFree.GetProperty("viewer"), "requires"), "requires Membership");
            await Api.ExpectErrorAsync(f, HttpMethod.Put, "/api/v1/events/" + id + "/rsvp", free.AccessToken, new { status = "Going" }, HttpStatusCode.Forbidden, "RsvpNotAllowed", ct).ConfigureAwait(false);
            await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/events/" + id + "/rsvp", member.AccessToken, new { status = "Going", hideFromAttendeeLists = true }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            await EventTestKit.RsvpAsync(f, other.AccessToken, id, "Going", ct).ConfigureAwait(false);
            JsonElement seen = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/events/" + id + "/attendees/enumerate", other.AccessToken, new { }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            List<string> users = seen.GetProperty("objects").EnumerateArray().Select(o => Api.Str(o.GetProperty("user"), "id")).ToList();
            Check.True(users.Contains(other.Id) && !users.Contains(member.Id), "Hidden members are not listed to others");
            JsonElement preview = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/events/" + id, other.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal(1L, preview.GetProperty("attendeePreview").GetProperty("totalRecords").GetInt64(), "Preview total excludes hidden members");
            JsonElement staff = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/events/" + id + "/attendees/enumerate", admin.AccessToken, new { }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal(2, staff.GetProperty("objects").GetArrayLength(), "Staff see everyone");
            await Api.ExpectErrorAsync(f, HttpMethod.Post, "/api/v1/events/" + id + "/attendees/enumerate", other.AccessToken, new { filters = new { status = "Interested" } }, HttpStatusCode.BadRequest, "ValidationFailed", ct).ConfigureAwait(false);
            string attendeesOnly = await EventTestKit.PublishedAsync(f, admin.AccessToken, "Private list", ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Post, "/api/v1/events/" + attendeesOnly + "/attendees/enumerate", other.AccessToken, new { }, HttpStatusCode.NotFound, "NotFound", ct).ConfigureAwait(false);
            await EventTestKit.RsvpAsync(f, other.AccessToken, attendeesOnly, "Going", ct).ConfigureAwait(false);
            await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/events/" + attendeesOnly + "/attendees/enumerate", other.AccessToken, new { }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            await SafetyHelpers.UpdateUserAsync(f, other.Id, u => { u.IsMuted = true; u.MutedUntilUtc = DateTime.UtcNow.AddDays(1); }, ct).ConfigureAwait(false);
            f.Services.Tokens.InvalidateUser(other.Id);
            await Api.ExpectErrorAsync(f, HttpMethod.Put, "/api/v1/events/" + attendeesOnly + "/rsvp", other.AccessToken, new { status = "Interested" }, HttpStatusCode.Forbidden, "Muted", ct).ConfigureAwait(false);
            f.Settings.Events.MaxActiveRsvpsPerMember = 1;
            string third = await EventTestKit.PublishedAsync(f, admin.AccessToken, "One too many", ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Put, "/api/v1/events/" + third + "/rsvp", member.AccessToken, new { status = "Going" }, HttpStatusCode.Conflict, "RsvpLimitReached", ct).ConfigureAwait(false);
        }

        private static async Task StaffAsync(HttpFixture f, CancellationToken ct)
        {
            TestMember manager = await EventTestKit.ManagerAsync(f, ct).ConfigureAwait(false);
            List<TestMember> m = await EventTestKit.MembersAsync(f, 3, ct).ConfigureAwait(false);
            string id = await EventTestKit.PublishedAsync(f, manager.AccessToken, "Staff managed", ct, new { capacity = 1 }).ConfigureAwait(false);
            await EventTestKit.RsvpAsync(f, m[0].AccessToken, id, "Going", ct).ConfigureAwait(false);
            JsonElement waiting = await EventTestKit.RsvpAsync(f, m[1].AccessToken, id, "Going", ct).ConfigureAwait(false);
            string waitingId = Api.Str(waiting.GetProperty("myRsvp"), "id");
            JsonElement list = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/admin/events/" + id + "/rsvps/enumerate", manager.AccessToken, new { filters = new { status = "Waitlisted" } }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal(waitingId, EventTestKit.Ids(list).Single(), "Filter by status");
            JsonElement promoted = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/admin/events/" + id + "/rsvps/" + waitingId + "/promote", manager.AccessToken, new { reason = "VIP" }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal("Going", Api.Str(promoted, "status"), "Promoted beyond capacity");
            Check.Equal(2, (await EventTestKit.RecordAsync(f, id, ct).ConfigureAwait(false)).GoingCount, "Two going");
            Check.Equal(1, (await EventTestKit.NotificationsAsync(f, m[1].Id, NotificationTypeEnum.EventWaitlistPromoted, ct).ConfigureAwait(false)).Count, "Told");
            JsonElement first = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/events/" + id, m[0].AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            string firstId = Api.Str(first.GetProperty("myRsvp"), "id");
            JsonElement moved = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/admin/events/" + id + "/rsvps/" + firstId + "/move-to-waitlist", manager.AccessToken, new { }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal("Waitlisted", Api.Str(moved, "status"), "Moved to the waitlist");
            await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/admin/events/" + id + "/rsvps/" + waitingId + "/remove", manager.AccessToken, new { }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            JsonElement back = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/events/" + id, m[0].AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal("Going", EventTestKit.MyStatus(back), "Removal promotes the waitlist");
            JsonElement none = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/events/" + id, m[1].AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal("None", EventTestKit.MyStatus(none), "Removed");
            await Api.ExpectErrorAsync(f, HttpMethod.Post, "/api/v1/admin/events/" + id + "/rsvps/rsv_missing/remove", manager.AccessToken, new { }, HttpStatusCode.NotFound, "NotFound", ct).ConfigureAwait(false);
            Check.True(await ModerationTestKit.AuditCountAsync(f, "event.rsvp.promote", waitingId, ct).ConfigureAwait(false) >= 1, "Promotion audited");
            Check.True(await ModerationTestKit.AuditCountAsync(f, "event.rsvp.remove", waitingId, ct).ConfigureAwait(false) >= 1, "Removal audited");
        }

        #endregion
    }
}
