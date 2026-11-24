namespace Test.Shared.Suites
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Net;
    using System.Net.Http;
    using System.Text;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Touchstone.Core;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Models;
    using TrellisKit.Server.Services;

    /// <summary>
    /// Event access (docs/PHASE4_CONTRACT.md section 0.4): each access model, gated and not, locked stubs,
    /// rsvpAudience, the public routes, the feature switch and kill switch, comments and reactions on events, the
    /// account lifecycle, and the account export sections.
    /// </summary>
    public static class EventAccessSuite
    {
        #region Public-Members

        /// <summary>
        /// Suite identifier.
        /// </summary>
        public const string Id = "EventAccess";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the suite.
        /// </summary>
        /// <returns>Suite.</returns>
        public static TestSuiteDescriptor Build()
        {
            return new TestSuiteDescriptor(Id, "Event access (" + TestEnvironment.Provider + ")", new[]
            {
                Case("Access.PartiallyGated.EventsGated", "Events gated: free accounts list Members events as locked stubs (no description, hosts, online, or RSVP) and get 402 on RSVP; members RSVP; visitors see only Public events", GatedAsync),
                Case("Access.PartiallyGated.NotGated", "Events not gated: free accounts read and RSVP to SignedIn events; Members events are locked stubs; rsvpAudience Members refuses free accounts; visitors read Public events and are asked to sign in", NotGatedAsync),
                Case("Access.FullyGatedAndOpen", "FullyGated: free accounts get 402 and visitors 401 on event routes; Open: Members events are SignedIn content for every account", ModelsAsync),
                Case("Access.PublicRoutes", "Public events are 404 NotAvailable until events.publicListingsEnabled; then upcoming Public events are listed and readable anonymously with public caching, without RSVP or viewer data", PublicAsync),
                SuiteCases.Http(Id, "Access.FeatureOff", "features.events false: member, admin, me, public, and calendar routes answer 404 NotAvailable", p => p.Features.Events = false, FeatureOffAsync),
                Case("Access.KillSwitch", "The events kill switch blocks member event routes with 503 FeatureDisabled while admin routes stay open", KillSwitchAsync),
                Case("Access.Conversation", "Comments and reactions on events go through the provider: hosts get PostComment, commentsEnabled false is 403 CommentsDisabled, canceled events take no new comments (409 EventCanceled) but keep reactions; GET and POST comment trees both work", ConversationAsync),
                Case("Access.LifecycleAndExport", "Account deletion removes RSVPs (the waitlist is promoted), the calendar feed, and host rows; the export carries rsvps and calendarFeed", LifecycleAsync)
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

        private static async Task GatedAsync(HttpFixture f, CancellationToken ct)
        {
            await EventTestKit.PartiallyGatedAsync(f, ct, "events").ConfigureAwait(false);
            TestMember admin = await Api.AdminAsync(f, ct).ConfigureAwait(false);
            TestMember free = await Api.RegisterAsync(f, ct).ConfigureAwait(false);
            TestMember member = await Api.MemberAsync(f, ct).ConfigureAwait(false);
            string members = await EventTestKit.PublishedAsync(f, admin.AccessToken, "Members session", ct, new { hostUserIds = new[] { admin.Id } }).ConfigureAwait(false);
            Check.Equal(ContentAudienceEnum.Members, (await EventTestKit.RecordAsync(f, members, ct).ConfigureAwait(false)).Audience, "Gated: new events default to Members");
            string open = await EventTestKit.PublishedAsync(f, admin.AccessToken, "Open house", ct, new { audience = "Public" }).ConfigureAwait(false);
            JsonElement list = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/events/enumerate", free.AccessToken, new { }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            JsonElement stub = list.GetProperty("objects").EnumerateArray().First(o => Api.Str(o, "id") == members);
            Check.True(stub.GetProperty("locked").GetBoolean(), "Locked stub in the list");
            Check.True(!stub.GetProperty("viewer").GetProperty("canOpen").GetBoolean(), "canOpen false");
            Check.Equal("Membership", Api.Str(stub.GetProperty("viewer"), "requires"), "requires Membership");
            JsonElement detail = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/events/" + members, free.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.True(detail.GetProperty("locked").GetBoolean() && detail.GetProperty("description").ValueKind == JsonValueKind.Null, "Detail stub without the description");
            Check.Equal(0, detail.GetProperty("hosts").GetArrayLength(), "Stub without hosts");
            ApiResult rsvp = await Api.SendAsync(f, HttpMethod.Put, "/api/v1/events/" + members + "/rsvp", free.AccessToken, new { status = "Going" }, ct).ConfigureAwait(false);
            Check.Equal((HttpStatusCode)402, rsvp.Status, "RSVPs need a membership when events are gated");
            await EventTestKit.RsvpAsync(f, member.AccessToken, members, "Going", ct).ConfigureAwait(false);
            JsonElement anon = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/events/enumerate", null, new { }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.True(EventTestKit.Ids(anon).SequenceEqual(new[] { open }), "Visitors list only Public events");
            await Api.ExpectErrorAsync(f, HttpMethod.Get, "/api/v1/events/" + members, null, null, HttpStatusCode.NotFound, "NotFound", ct).ConfigureAwait(false);
        }

        private static async Task NotGatedAsync(HttpFixture f, CancellationToken ct)
        {
            await EventTestKit.PartiallyGatedAsync(f, ct).ConfigureAwait(false);
            TestMember admin = await Api.AdminAsync(f, ct).ConfigureAwait(false);
            TestMember free = await Api.RegisterAsync(f, ct).ConfigureAwait(false);
            string signedIn = await EventTestKit.PublishedAsync(f, admin.AccessToken, "Signed in event", ct).ConfigureAwait(false);
            Check.Equal(ContentAudienceEnum.SignedIn, (await EventTestKit.RecordAsync(f, signedIn, ct).ConfigureAwait(false)).Audience, "Default audience");
            string members = await EventTestKit.PublishedAsync(f, admin.AccessToken, "Members event", ct, new { audience = "Members" }).ConfigureAwait(false);
            string publicEvent = await EventTestKit.PublishedAsync(f, admin.AccessToken, "Public talk", ct, new { audience = "Public", rsvpAudience = "Members" }).ConfigureAwait(false);
            await EventTestKit.RsvpAsync(f, free.AccessToken, signedIn, "Going", ct).ConfigureAwait(false);
            JsonElement locked = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/events/" + members, free.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.True(locked.GetProperty("locked").GetBoolean(), "Members events are locked stubs for free accounts");
            await Api.ExpectErrorAsync(f, HttpMethod.Put, "/api/v1/events/" + members + "/rsvp", free.AccessToken, new { status = "Going" }, HttpStatusCode.Forbidden, "RsvpNotAllowed", ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Put, "/api/v1/events/" + publicEvent + "/rsvp", free.AccessToken, new { status = "Going" }, HttpStatusCode.Forbidden, "RsvpNotAllowed", ct).ConfigureAwait(false);
            JsonElement visitor = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/events/" + publicEvent, null, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.True(!visitor.GetProperty("viewer").GetProperty("canRsvp").GetBoolean(), "Visitors cannot RSVP");
            Check.Equal("SignIn", Api.Str(visitor.GetProperty("viewer"), "requires"), "requires SignIn");
            Check.True(visitor.GetProperty("myRsvp").ValueKind == JsonValueKind.Null, "No myRsvp");
            ApiResult anonRsvp = await Api.SendAsync(f, HttpMethod.Put, "/api/v1/events/" + publicEvent + "/rsvp", null, new { status = "Going" }, ct).ConfigureAwait(false);
            Check.Equal(HttpStatusCode.Unauthorized, anonRsvp.Status, "Visitors must sign in to RSVP");
            await Api.ExpectErrorAsync(f, HttpMethod.Get, "/api/v1/events/" + signedIn, null, null, HttpStatusCode.NotFound, "NotFound", ct).ConfigureAwait(false);
        }

        private static async Task ModelsAsync(HttpFixture f, CancellationToken ct)
        {
            TestMember admin = await Api.AdminAsync(f, ct).ConfigureAwait(false);
            TestMember free = await Api.RegisterAsync(f, ct).ConfigureAwait(false);
            string id = await EventTestKit.PublishedAsync(f, admin.AccessToken, "Members only", ct, new { audience = "Members" }).ConfigureAwait(false);
            await EventTestKit.SetAccessAsync(f, new { model = "FullyGated" }, ct).ConfigureAwait(false);
            ApiResult gated = await Api.SendAsync(f, HttpMethod.Get, "/api/v1/events", free.AccessToken, null, ct).ConfigureAwait(false);
            Check.Equal((HttpStatusCode)402, gated.Status, "FullyGated: free accounts need a membership");
            ApiResult anon = await Api.SendAsync(f, HttpMethod.Get, "/api/v1/events", null, null, ct).ConfigureAwait(false);
            Check.Equal(HttpStatusCode.Unauthorized, anon.Status, "FullyGated: visitors sign in");
            await EventTestKit.SetAccessAsync(f, new { model = "Open", anonymousBrowsing = false }, ct).ConfigureAwait(false);
            JsonElement open = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/events/" + id, free.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.True(!open.GetProperty("locked").GetBoolean(), "Open: Members events are readable");
            await EventTestKit.RsvpAsync(f, free.AccessToken, id, "Going", ct).ConfigureAwait(false);
        }

        private static async Task PublicAsync(HttpFixture f, CancellationToken ct)
        {
            TestMember admin = await Api.AdminAsync(f, ct).ConfigureAwait(false);
            string pub = await EventTestKit.PublishedAsync(f, admin.AccessToken, "Open day", ct, new { audience = "Public", online = new { joinUrl = "https://meet.example.test/x" }, format = "Hybrid" }).ConfigureAwait(false);
            string priv = await EventTestKit.PublishedAsync(f, admin.AccessToken, "Private day", ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Get, "/api/v1/public/events", null, null, HttpStatusCode.NotFound, "NotAvailable", ct).ConfigureAwait(false);
            f.Settings.Events.PublicListingsEnabled = true;
            ApiResult list = await Api.SendAsync(f, HttpMethod.Post, "/api/v1/public/events/enumerate", null, new { maxResults = 50 }, ct).ConfigureAwait(false);
            Check.Equal(HttpStatusCode.OK, list.Status, "Public list");
            string cache = list.Headers.TryGetValue("Cache-Control", out string? c) ? c : String.Empty;
            Check.True(cache.Contains("public", StringComparison.Ordinal) && cache.Contains("max-age=60", StringComparison.Ordinal), "Cache-Control: " + cache);
            List<string> ids = EventTestKit.Ids(list.Body);
            Check.True(ids.Contains(pub) && !ids.Contains(priv), "Only Public events");
            Check.True(list.Body.GetProperty("maxResults").GetInt32() <= 20, "maxResults at most 20");
            JsonElement item = list.Body.GetProperty("objects")[0];
            Check.True(!item.TryGetProperty("viewer", out JsonElement _) && !item.TryGetProperty("myRsvp", out JsonElement _), "No viewer or RSVP data");
            Check.True(!list.Text.Contains("meet.example.test", StringComparison.Ordinal), "No join URLs");
            JsonElement one = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/public/events/" + pub, null, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal("About Open day", Api.Str(one, "description"), "Description included");
            await Api.ExpectErrorAsync(f, HttpMethod.Get, "/api/v1/public/events/" + priv, null, null, HttpStatusCode.NotFound, "NotFound", ct).ConfigureAwait(false);
        }

        private static async Task FeatureOffAsync(HttpFixture f, CancellationToken ct)
        {
            EventTestKit.Relax(f);
            TestMember member = await Api.MemberAsync(f, ct).ConfigureAwait(false);
            TestMember admin = await Api.AdminAsync(f, ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Get, "/api/v1/events", member.AccessToken, null, HttpStatusCode.NotFound, "NotAvailable", ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Post, "/api/v1/admin/events/enumerate", admin.AccessToken, new { }, HttpStatusCode.NotFound, "NotAvailable", ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Get, "/api/v1/me/calendar-feed", member.AccessToken, null, HttpStatusCode.NotFound, "NotAvailable", ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Post, "/api/v1/me/events/enumerate", member.AccessToken, new { }, HttpStatusCode.NotFound, "NotAvailable", ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Get, "/api/v1/public/events", null, null, HttpStatusCode.NotFound, "NotAvailable", ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Get, "/api/v1/calendar/cfd_x.y.ics", null, null, HttpStatusCode.NotFound, "NotAvailable", ct).ConfigureAwait(false);
        }

        private static async Task KillSwitchAsync(HttpFixture f, CancellationToken ct)
        {
            TestMember manager = await EventTestKit.ManagerAsync(f, ct).ConfigureAwait(false);
            TestMember member = await Api.MemberAsync(f, ct).ConfigureAwait(false);
            string id = await EventTestKit.PublishedAsync(f, manager.AccessToken, "Killed", ct).ConfigureAwait(false);
            await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/admin/app/kill-switches/events", HttpFixture.AdminToken, new { killed = true, message = "Events are paused.", reason = "Incident 7" }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            ApiResult blocked = await Api.SendAsync(f, HttpMethod.Get, "/api/v1/events/" + id, member.AccessToken, null, ct).ConfigureAwait(false);
            Check.Equal((HttpStatusCode)503, blocked.Status, "Member routes blocked");
            Check.Equal("FeatureDisabled", blocked.Error, "FeatureDisabled");
            ApiResult rsvp = await Api.SendAsync(f, HttpMethod.Put, "/api/v1/events/" + id + "/rsvp", member.AccessToken, new { status = "Going" }, ct).ConfigureAwait(false);
            Check.Equal((HttpStatusCode)503, rsvp.Status, "RSVPs blocked");
            await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/admin/events/enumerate", manager.AccessToken, new { }, HttpStatusCode.OK, ct).ConfigureAwait(false);
        }

        private static async Task ConversationAsync(HttpFixture f, CancellationToken ct)
        {
            TestMember admin = await Api.AdminAsync(f, ct).ConfigureAwait(false);
            TestMember host = await SafetyHelpers.MemberAsync(f, ct, "Event Host").ConfigureAwait(false);
            TestMember member = await SafetyHelpers.MemberAsync(f, ct, "Commenter").ConfigureAwait(false);
            string id = await EventTestKit.PublishedAsync(f, admin.AccessToken, "Talk night", ct, new { hostUserIds = new[] { host.Id } }).ConfigureAwait(false);
            JsonElement comment = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/events/" + id + "/comments", member.AccessToken, new { body = "Looking forward to it" }, HttpStatusCode.Created, ct).ConfigureAwait(false);
            List<Notification> notices = await EventTestKit.NotificationsAsync(f, host.Id, NotificationTypeEnum.PostComment, ct).ConfigureAwait(false);
            Check.Equal(1, notices.Count, "Hosts get PostComment");
            Check.True(notices[0].Path.StartsWith("/events/" + id, StringComparison.Ordinal), "Path to the event");
            await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/events/" + id + "/comments", host.AccessToken, new { body = "See you there", parentCommentId = Api.Str(comment, "id") }, HttpStatusCode.Created, ct).ConfigureAwait(false);
            Check.Equal(1, (await EventTestKit.NotificationsAsync(f, member.Id, NotificationTypeEnum.CommentReply, ct).ConfigureAwait(false)).Count, "Replies notify the parent author");
            JsonElement tree = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/events/" + id + "/comments", member.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal(1, tree.GetProperty("objects").GetArrayLength(), "GET comment tree");
            await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/events/" + id + "/comments/enumerate", member.AccessToken, new { }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal(2, (await EventTestKit.RecordAsync(f, id, ct).ConfigureAwait(false)).CommentCount, "commentcount");
            string like = f.Property.Reactions[0].Key;
            await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/events/" + id + "/reactions/" + like, member.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal(1, (await EventTestKit.RecordAsync(f, id, ct).ConfigureAwait(false)).ReactionTotal, "reactiontotal");
            string closed = await EventTestKit.PublishedAsync(f, admin.AccessToken, "No comments", ct, new { commentsEnabled = false }).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Post, "/api/v1/events/" + closed + "/comments", member.AccessToken, new { body = "Hi" }, HttpStatusCode.Forbidden, "CommentsDisabled", ct).ConfigureAwait(false);
            await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/admin/events/" + id + "/cancel", admin.AccessToken, new { reason = "Speaker is ill", notifyAttendees = false }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Post, "/api/v1/events/" + id + "/comments", member.AccessToken, new { body = "Oh no" }, HttpStatusCode.Conflict, "EventCanceled", ct).ConfigureAwait(false);
            JsonElement after = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/events/" + id + "/comments", member.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal(1, after.GetProperty("objects").GetArrayLength(), "Existing comments stay readable");
            await Api.ExpectAsync(f, HttpMethod.Delete, "/api/v1/events/" + id + "/reactions/" + like, member.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
        }

        private static async Task LifecycleAsync(HttpFixture f, CancellationToken ct)
        {
            TestMember admin = await Api.AdminAsync(f, ct).ConfigureAwait(false);
            TestMember leaving = await SafetyHelpers.MemberAsync(f, ct, "Leaving Member").ConfigureAwait(false);
            TestMember waiting = await SafetyHelpers.MemberAsync(f, ct, "Waiting Member").ConfigureAwait(false);
            string id = await EventTestKit.PublishedAsync(f, admin.AccessToken, "Full house", ct, new { capacity = 1, hostUserIds = new[] { leaving.Id } }).ConfigureAwait(false);
            await EventTestKit.RsvpAsync(f, leaving.AccessToken, id, "Going", ct).ConfigureAwait(false);
            await EventTestKit.RsvpAsync(f, waiting.AccessToken, id, "Going", ct).ConfigureAwait(false);
            await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/me/calendar-feed", leaving.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            IAccountExportSection rsvps = f.Services.AccountExports.Sections.First(s => s.Name == "rsvps");
            IAccountExportSection feed = f.Services.AccountExports.Sections.First(s => s.Name == "calendarFeed");
            using (MemoryStream stream = new MemoryStream())
            {
                using (Utf8JsonWriter writer = new Utf8JsonWriter(stream))
                {
                    writer.WriteStartObject();
                    writer.WritePropertyName(rsvps.Name);
                    await rsvps.WriteAsync(writer, leaving.Id, ct).ConfigureAwait(false);
                    writer.WritePropertyName(feed.Name);
                    await feed.WriteAsync(writer, leaving.Id, ct).ConfigureAwait(false);
                    writer.WriteEndObject();
                }
                using (JsonDocument doc = JsonDocument.Parse(Encoding.UTF8.GetString(stream.ToArray())))
                {
                    JsonElement exported = doc.RootElement;
                    Check.Equal(id, Api.Str(exported.GetProperty("rsvps")[0], "eventId"), "rsvps exported");
                    Check.Equal("Full house", Api.Str(exported.GetProperty("rsvps")[0], "eventTitle"), "With the event title");
                    Check.Equal(1, exported.GetProperty("calendarFeed").GetArrayLength(), "calendarFeed exported");
                    Check.True(!exported.GetProperty("calendarFeed")[0].TryGetProperty("secretHash", out JsonElement _), "Never the secret");
                }
            }
            await Api.ExpectAsync(f, HttpMethod.Delete, "/api/v1/me", leaving.AccessToken, new { confirm = "DELETE" }, HttpStatusCode.Accepted, ct).ConfigureAwait(false);
            await f.Services.Events.EraseForAccountAsync(leaving.Id, ct).ConfigureAwait(false);
            Check.True(await f.Database.Events.Rsvps.ReadAsync(id, leaving.Id, ct).ConfigureAwait(false) == null, "RSVP deleted");
            Check.Equal(EventRsvpStatusEnum.Going, (await f.Database.Events.Rsvps.ReadAsync(id, waiting.Id, ct).ConfigureAwait(false))!.Status, "The waitlist was promoted");
            Check.True(await f.Database.Events.Feeds.ReadForUserAsync(leaving.Id, ct).ConfigureAwait(false) == null, "Feed deleted");
            Check.True(!(await EventTestKit.RecordAsync(f, id, ct).ConfigureAwait(false)).HostUserIds.Contains(leaving.Id), "Host row removed; the event stays");
        }

        #endregion
    }
}
