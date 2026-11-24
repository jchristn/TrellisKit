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
    /// Event reminders (docs/PHASE4_CONTRACT.md section 4.4) with a fixed clock: reminder rows per offset, the worker
    /// pass, recipients by RSVP status, the email for the largest offset only, rescheduling, the grace skip, and the
    /// idempotent fan-out.
    /// </summary>
    public static class EventReminderSuite
    {
        #region Public-Members

        /// <summary>
        /// Suite identifier.
        /// </summary>
        public const string Id = "EventReminders";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the suite.
        /// </summary>
        /// <returns>Suite.</returns>
        public static TestSuiteDescriptor Build()
        {
            return new TestSuiteDescriptor(Id, "Event reminders (" + TestEnvironment.Provider + ")", new[]
            {
                Case("Reminders.Offsets", "Each offset is a reminder row due at start minus the offset; editing the start recomputes due times and clears sent for reminders not yet due; offsets can be replaced", OffsetsAsync),
                Case("Reminders.Delivery", "At the due time the pass reminds Going members (and Waitlisted ones on the first reminder only, with a waitlist title), never Interested members by default, and emails only for the largest offset", DeliveryAsync),
                Case("Reminders.GraceAndIdempotency", "A reminder later than events.reminderGraceMinutes is skipped, not sent; canceled events skip their reminders; a repeated pass or fan-out never duplicates notices", GraceAsync)
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

        private static async Task OffsetsAsync(HttpFixture f, CancellationToken ct)
        {
            TestMember admin = await Api.AdminAsync(f, ct).ConfigureAwait(false);
            string id = await EventTestKit.PublishedAsync(f, admin.AccessToken, "Reminder offsets", ct, new { reminderOffsetsMinutes = new[] { 1440, 120 } }).ConfigureAwait(false);
            EventRecord e = await EventTestKit.RecordAsync(f, id, ct).ConfigureAwait(false);
            List<EventReminder> reminders = await f.Database.Events.Reminders.ListForEventAsync(id, ct).ConfigureAwait(false);
            Check.Equal(2, reminders.Count, "Two reminder rows");
            Check.Equal(e.StartUtc.AddMinutes(-1440), reminders[0].DueUtc, "Largest offset first");
            Check.Equal(e.StartUtc.AddMinutes(-120), reminders[1].DueUtc, "Second offset");
            await f.Raw.ExecuteQueryAsync("UPDATE eventreminders SET sentutc = now() WHERE eventid = '" + id + "';", false, ct).ConfigureAwait(false);
            await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/admin/events/" + id, admin.AccessToken, new { startLocal = EventTestKit.Local(12, 18), endLocal = EventTestKit.Local(12, 20) }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            EventRecord moved = await EventTestKit.RecordAsync(f, id, ct).ConfigureAwait(false);
            List<EventReminder> after = await f.Database.Events.Reminders.ListForEventAsync(id, ct).ConfigureAwait(false);
            Check.True(after.All(r => r.SentUtc == null), "Sent cleared for reminders not yet due");
            Check.Equal(moved.StartUtc.AddMinutes(-1440), after[0].DueUtc, "Due time recomputed");
            JsonElement replaced = await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/admin/events/" + id, admin.AccessToken, new { reminderOffsetsMinutes = new[] { 60 } }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.True(replaced.GetProperty("reminderOffsetsMinutes").EnumerateArray().Select(x => x.GetInt32()).SequenceEqual(new[] { 60 }), "Offsets replaced");
            Check.Equal(1, (await f.Database.Events.Reminders.ListForEventAsync(id, ct).ConfigureAwait(false)).Count, "One row");
            Check.Equal(1, replaced.GetProperty("reminderStatus").GetArrayLength(), "reminderStatus");
            ApiResult bad = await Api.SendAsync(f, HttpMethod.Put, "/api/v1/admin/events/" + id, admin.AccessToken, new { reminderOffsetsMinutes = new[] { 2 } }, ct).ConfigureAwait(false);
            Check.Equal(HttpStatusCode.BadRequest, bad.Status, "Offsets are 5 to 20160 minutes");
        }

        private static async Task DeliveryAsync(HttpFixture f, CancellationToken ct)
        {
            TestMember admin = await Api.AdminAsync(f, ct).ConfigureAwait(false);
            TestMember going = await SafetyHelpers.MemberAsync(f, ct, "Going Member").ConfigureAwait(false);
            TestMember waiting = await SafetyHelpers.MemberAsync(f, ct, "Waiting Member").ConfigureAwait(false);
            TestMember interested = await SafetyHelpers.MemberAsync(f, ct, "Interested Member").ConfigureAwait(false);
            string id = await EventTestKit.PublishedAsync(f, admin.AccessToken, "Reminded event", ct, new { capacity = 1, reminderOffsetsMinutes = new[] { 1440, 120 } }, 4).ConfigureAwait(false);
            await EventTestKit.RsvpAsync(f, going.AccessToken, id, "Going", ct).ConfigureAwait(false);
            await EventTestKit.RsvpAsync(f, waiting.AccessToken, id, "Going", ct).ConfigureAwait(false);
            await EventTestKit.RsvpAsync(f, interested.AccessToken, id, "Interested", ct).ConfigureAwait(false);
            EventRecord e = await EventTestKit.RecordAsync(f, id, ct).ConfigureAwait(false);
            EventTestKit.Clock(f, e.StartUtc.AddMinutes(-1439));
            int mails = f.Fakes.Email.Messages.Count;
            Check.Equal(1, await f.Services.Events.Reminders.PassAsync(ct).ConfigureAwait(false), "One reminder due");
            await EventTestKit.FanoutAsync(f, ct).ConfigureAwait(false);
            List<Notification> g = await EventTestKit.NotificationsAsync(f, going.Id, NotificationTypeEnum.EventReminder, ct).ConfigureAwait(false);
            Check.Equal(1, g.Count, "Going reminded");
            Check.True(g[0].Title.StartsWith("Reminded event starts", StringComparison.Ordinal), "Title: " + g[0].Title);
            List<Notification> w = await EventTestKit.NotificationsAsync(f, waiting.Id, NotificationTypeEnum.EventReminder, ct).ConfigureAwait(false);
            Check.True(w.Count == 1 && w[0].Title.StartsWith("You are on the waitlist", StringComparison.Ordinal), "Waitlisted get the first reminder with a waitlist title");
            Check.Equal(0, (await EventTestKit.NotificationsAsync(f, interested.Id, NotificationTypeEnum.EventReminder, ct).ConfigureAwait(false)).Count, "Interested not reminded by default");
            List<TrellisKit.Core.Integrations.Models.EmailMessage> sent = f.Fakes.Email.Messages.Skip(mails).ToList();
            Check.True(sent.Any(m => m.To == going.Email && m.Subject.StartsWith("Reminder: Reminded event", StringComparison.Ordinal)), "Email for the largest offset");
            Check.True(sent.Where(m => m.To == going.Email).All(m => m.TextBody.Contains("ics", StringComparison.Ordinal)), "Calendar link in the email");
            EventTestKit.Clock(f, e.StartUtc.AddMinutes(-119));
            mails = f.Fakes.Email.Messages.Count;
            Check.Equal(1, await f.Services.Events.Reminders.PassAsync(ct).ConfigureAwait(false), "Second reminder due");
            await EventTestKit.FanoutAsync(f, ct).ConfigureAwait(false);
            Check.Equal(2, (await EventTestKit.NotificationsAsync(f, going.Id, NotificationTypeEnum.EventReminder, ct).ConfigureAwait(false)).Count, "Going reminded twice");
            Check.Equal(1, (await EventTestKit.NotificationsAsync(f, waiting.Id, NotificationTypeEnum.EventReminder, ct).ConfigureAwait(false)).Count, "Waitlisted only once");
            Check.True(!f.Fakes.Email.Messages.Skip(mails).Any(m => m.Subject.StartsWith("Reminder:", StringComparison.Ordinal)), "No email for the smaller offset");
            f.Settings.Events.RemindInterested = true;
            EventTestKit.Clock(f, DateTime.UtcNow);
            string second = await EventTestKit.PublishedAsync(f, admin.AccessToken, "Interested too", ct, new { reminderOffsetsMinutes = new[] { 60 } }, 2).ConfigureAwait(false);
            await EventTestKit.RsvpAsync(f, interested.AccessToken, second, "Interested", ct).ConfigureAwait(false);
            EventRecord s = await EventTestKit.RecordAsync(f, second, ct).ConfigureAwait(false);
            EventTestKit.Clock(f, s.StartUtc.AddMinutes(-59));
            await f.Services.Events.Reminders.PassAsync(ct).ConfigureAwait(false);
            await EventTestKit.FanoutAsync(f, ct).ConfigureAwait(false);
            Check.Equal(1, (await EventTestKit.NotificationsAsync(f, interested.Id, NotificationTypeEnum.EventReminder, ct).ConfigureAwait(false)).Count, "events.remindInterested includes Interested members");
        }

        private static async Task GraceAsync(HttpFixture f, CancellationToken ct)
        {
            TestMember admin = await Api.AdminAsync(f, ct).ConfigureAwait(false);
            TestMember member = await SafetyHelpers.MemberAsync(f, ct).ConfigureAwait(false);
            string late = await EventTestKit.PublishedAsync(f, admin.AccessToken, "Late reminder", ct, new { reminderOffsetsMinutes = new[] { 600 } }, 3).ConfigureAwait(false);
            await EventTestKit.RsvpAsync(f, member.AccessToken, late, "Going", ct).ConfigureAwait(false);
            EventRecord e = await EventTestKit.RecordAsync(f, late, ct).ConfigureAwait(false);
            EventTestKit.Clock(f, e.StartUtc.AddMinutes(-600 + 45));
            await f.Services.Events.Reminders.PassAsync(ct).ConfigureAwait(false);
            await EventTestKit.FanoutAsync(f, ct).ConfigureAwait(false);
            Check.Equal(0, (await EventTestKit.NotificationsAsync(f, member.Id, NotificationTypeEnum.EventReminder, ct).ConfigureAwait(false)).Count, "Skipped, not sent late");
            Check.True((await f.Database.Events.Reminders.ListForEventAsync(late, ct).ConfigureAwait(false))[0].SkippedUtc != null, "Marked skipped");

            string canceled = await EventTestKit.PublishedAsync(f, admin.AccessToken, "Canceled reminder", ct, new { reminderOffsetsMinutes = new[] { 600 } }, 3).ConfigureAwait(false);
            await EventTestKit.RsvpAsync(f, member.AccessToken, canceled, "Going", ct).ConfigureAwait(false);
            await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/admin/events/" + canceled + "/cancel", admin.AccessToken, new { reason = "Weather warning", notifyAttendees = false }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            EventRecord c = await EventTestKit.RecordAsync(f, canceled, ct).ConfigureAwait(false);
            EventTestKit.Clock(f, c.StartUtc.AddMinutes(-599));
            await f.Services.Events.Reminders.PassAsync(ct).ConfigureAwait(false);
            await EventTestKit.FanoutAsync(f, ct).ConfigureAwait(false);
            Check.Equal(0, (await EventTestKit.NotificationsAsync(f, member.Id, NotificationTypeEnum.EventReminder, ct).ConfigureAwait(false)).Count, "Canceled events send no reminders");

            string onTime = await EventTestKit.PublishedAsync(f, admin.AccessToken, "On time", ct, new { reminderOffsetsMinutes = new[] { 600 } }, 3).ConfigureAwait(false);
            await EventTestKit.RsvpAsync(f, member.AccessToken, onTime, "Going", ct).ConfigureAwait(false);
            EventRecord o = await EventTestKit.RecordAsync(f, onTime, ct).ConfigureAwait(false);
            EventTestKit.Clock(f, o.StartUtc.AddMinutes(-599));
            await f.Services.Events.Reminders.PassAsync(ct).ConfigureAwait(false);
            await EventTestKit.FanoutAsync(f, ct).ConfigureAwait(false);
            await f.Raw.ExecuteQueryAsync("UPDATE eventreminders SET sentutc = NULL WHERE eventid = '" + onTime + "';", false, ct).ConfigureAwait(false);
            await f.Services.Events.Reminders.PassAsync(ct).ConfigureAwait(false);
            await EventTestKit.FanoutAsync(f, ct).ConfigureAwait(false);
            Check.Equal(1, (await EventTestKit.NotificationsAsync(f, member.Id, NotificationTypeEnum.EventReminder, ct).ConfigureAwait(false)).Count, "The fan-out key makes a repeated reminder idempotent");
            Check.Equal(1L, await EventTestKit.CountAsync(f, "SELECT COUNT(*) AS cnt FROM notificationfanouts WHERE type = 'EventReminder' AND sourceid LIKE '" + onTime + ":%';", ct).ConfigureAwait(false), "One fan-out job");
        }

        #endregion
    }
}
