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
    using TrellisKit.Server.Services;
    using TrellisKit.Server.Services.Email;
    using TrellisKit.Server.Services.Email.Templates;
    using TrellisKit.Server.Services.Realtime;
    using TrellisKit.Server.Workers;

    /// <summary>
    /// Phase 5 broadcasts and announcements (docs/PHASE5_CONTRACT.md section 8, resolved decisions 20 and 22, as-built
    /// note D): segment validation and every filter kind, membership states per access model, preview count equals the
    /// sent count, idempotent batches across worker restarts, preferences and suppressions, the Essential override and its
    /// SystemAdmin-only rule, the Communicator role, test sends, the schedule window, the confirmCount tolerance, cancel
    /// mid-send, the daily and recipient limits, the kill switch, announcements for recipients only, email rendering, and
    /// no per-recipient realtime events.
    /// </summary>
    public static class BroadcastSuite
    {
        #region Public-Members

        /// <summary>
        /// Suite identifier.
        /// </summary>
        public const string Id = "Broadcasts";

        #endregion

        #region Private-Members

        private const string _Base = BroadcastTestKit.Base;
        private static readonly DateTime _Bulk = new DateTime(2001, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the suite.
        /// </summary>
        /// <returns>Suite.</returns>
        public static TestSuiteDescriptor Build()
        {
            return new TestSuiteDescriptor(Id, "Broadcasts (" + TestEnvironment.Provider + ")", new[]
            {
                Case("Segments.Validation", "Unknown fields, options, and operators, bad values, too many filters, and the Phase 4 filters with an unknown event or series are 400 SegmentInvalid with fieldErrors; saved segments create, read, list by label, edit, preview by id, and delete", ValidationAsync),
                Case("Segments.EveryFilterKind", "membership, membershipSource, store, profile fields, labels (staff labels too), tags, sign-up and activity windows, push tokens, verified email, and roles each select exactly the right members, combined with AND", FiltersAsync),
                Case("Segments.EventRsvpAndSeriesFollowers", "eventRsvp selects members by their RSVP to an event (going, interested, any) and seriesFollowers the members following a series (opted-out followers excluded), against the Phase 4 tables; both answer SegmentInvalid when their feature is off", Phase4FiltersAsync),
                Case("Access.MembershipStatesPerModel", "FullyGated reaches members and staff only; Open reaches every attested account; banned, deleted, and unattested accounts are never recipients", AccessAsync),
                Case("Send.PreviewEqualsSentAndIdempotent", "The sent count equals the preview count; batches are idempotent across a worker restart that lost its cursor; nothing is published on the realtime bus per recipient", IdempotentAsync),
                Case("Send.CancelMidSend", "Cancel during a send with a small batch size stops the remaining batches; a canceled broadcast cannot be edited or canceled again", CancelAsync),
                Case("Send.ConfirmCountAndSchedule", "confirmCount must be within 5 percent of the preview; schedules are 5 minutes to 90 days ahead; scheduled broadcasts stay editable and send when due on the worker's clock; sent broadcasts are not editable", ScheduleAsync),
                Case("Limits.MaxPerDayAndRecipients", "broadcasts.maxPerDay applies to SystemAdmin per UTC day (409 BroadcastLimitReached), test sends do not count, and broadcasts.maxRecipients bounds the audience", LimitsAsync),
                Case("Delivery.PreferencesSuppressionsEssential", "Optional broadcasts respect the Announcements push and email preferences and suppressions with a one-click unsubscribe; Essential ignores the preferences but never suppressions and is SystemAdmin only", PreferencesAsync),
                Case("Roles.Communicator", "A Communicator may do everything on broadcasts and nothing else in the admin console; members cannot use broadcast routes; every action is audited", RolesAsync),
                Case("Test.SendsToCallerOnly", "A test send reaches the caller only on the chosen channels, marked Test, opens as an announcement, and stays out of lists and limits", TestSendAsync),
                Case("Announcements.RecipientsOnly", "Recipients read and list their announcements (labels and tags included) through an Announcements notification; anyone else gets 404", AnnouncementsAsync),
                Case("Email.Rendering", "The broadcast template renders the Markdown subset to safe HTML (no raw HTML, no unsafe links) with a text part, previews like the other templates, and its defaults use declared tokens", RenderingAsync),
                Case("Worker.KillSwitchPauses", "The broadcasts kill switch pauses the send worker while the admin routes stay open; turning it off resumes the send", KillSwitchAsync),
                Case("Lifecycle.AccountDeletionAndCatalog", "Account deletion deletes the member's deliveries; the Announcements category is in-app always, push and email on and editable", LifecycleAsync)
            });
        }

        #endregion

        #region Private-Methods

        private static TestCaseDescriptor Case(string caseId, string name, Func<HttpFixture, CancellationToken, Task> body)
        {
            return SuiteCases.Http(Id, caseId, name, (f, ct) =>
            {
                BroadcastTestKit.Prepare(f);
                return body(f, ct);
            });
        }

        private static async Task ExpectSegmentInvalidAsync(HttpFixture f, object filter, string what, CancellationToken ct, string? messagePart = null)
        {
            ApiResult r = await Api.SendAsync(f, HttpMethod.Post, _Base + "/segments/preview", HttpFixture.AdminToken, new { segment = BroadcastTestKit.Segment(filter) }, ct).ConfigureAwait(false);
            Check.Equal(HttpStatusCode.BadRequest, r.Status, what + " status (" + r.Text + ")");
            Check.Equal("SegmentInvalid", r.Error, what + " error");
            Check.True(r.Body.GetProperty("fieldErrors").TryGetProperty("segment.filters[0]", out JsonElement message), what + " names the filter (" + r.Text + ")");
            if (messagePart != null) Check.True((message.GetString() ?? String.Empty).Contains(messagePart, StringComparison.OrdinalIgnoreCase), what + " message (" + message.GetString() + ")");
        }

        private static async Task Phase4FiltersAsync(HttpFixture f, CancellationToken ct)
        {
            EventTestKit.Relax(f);
            LibraryTestKit.Relax(f);
            TestMember admin = await Api.AdminAsync(f, ct).ConfigureAwait(false);
            List<TestMember> members = new List<TestMember>();
            for (int i = 0; i < 4; i++) members.Add(await Api.MemberAsync(f, ct).ConfigureAwait(false));
            string eventId = await EventTestKit.PublishedAsync(f, admin.AccessToken, "Segment event", ct).ConfigureAwait(false);
            await EventTestKit.RsvpAsync(f, members[0].AccessToken, eventId, "Going", ct).ConfigureAwait(false);
            await EventTestKit.RsvpAsync(f, members[1].AccessToken, eventId, "Going", ct).ConfigureAwait(false);
            await EventTestKit.RsvpAsync(f, members[2].AccessToken, eventId, "Interested", ct).ConfigureAwait(false);
            Check.Equal(2L, await BroadcastTestKit.CountAsync(f, HttpFixture.AdminToken, BroadcastTestKit.Segment(BroadcastTestKit.Filter("eventRsvp", "going", eventId)), ct).ConfigureAwait(false), "Going RSVPs");
            Check.Equal(1L, await BroadcastTestKit.CountAsync(f, HttpFixture.AdminToken, BroadcastTestKit.Segment(BroadcastTestKit.Filter("eventRsvp", "interested", eventId)), ct).ConfigureAwait(false), "Interested RSVPs");
            Check.Equal(3L, await BroadcastTestKit.CountAsync(f, HttpFixture.AdminToken, BroadcastTestKit.Segment(BroadcastTestKit.Filter("eventRsvp", "any", eventId)), ct).ConfigureAwait(false), "Any RSVP");

            JsonElement series = await LibraryTestKit.SeriesAsync(f, admin.AccessToken, "Segment series", ct).ConfigureAwait(false);
            string seriesId = Api.Str(series, "id");
            foreach (int i in new[] { 1, 3 })
                await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/series/" + seriesId + "/follow", members[i].AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal(2L, await BroadcastTestKit.CountAsync(f, HttpFixture.AdminToken, BroadcastTestKit.Segment(BroadcastTestKit.Filter("seriesFollowers", "eq", seriesId)), ct).ConfigureAwait(false), "Series followers");
            Check.Equal(1L, await BroadcastTestKit.CountAsync(f, HttpFixture.AdminToken, BroadcastTestKit.Segment(BroadcastTestKit.Filter("seriesFollowers", "eq", seriesId), BroadcastTestKit.Filter("eventRsvp", "going", eventId)), ct).ConfigureAwait(false), "Combined with AND");
            await LibraryTestKit.SqlAsync(f, "UPDATE seriesfollows SET isoptedout = TRUE WHERE propertyid = '" + LibraryTestKit.Pid(f) + "' AND seriesid = '" + seriesId + "' AND userid = '" + members[3].Id + "';", ct).ConfigureAwait(false);
            Check.Equal(1L, await BroadcastTestKit.CountAsync(f, HttpFixture.AdminToken, BroadcastTestKit.Segment(BroadcastTestKit.Filter("seriesFollowers", "eq", seriesId)), ct).ConfigureAwait(false), "Opted-out followers are excluded");

            f.Property.Features.Events = false;
            f.Property.Features.Library = false;
            await ExpectSegmentInvalidAsync(f, BroadcastTestKit.Filter("eventRsvp", "going", eventId), "eventRsvp with events off", ct, "features.events").ConfigureAwait(false);
            await ExpectSegmentInvalidAsync(f, BroadcastTestKit.Filter("seriesFollowers", "eq", seriesId), "seriesFollowers with the library off", ct, "features.library").ConfigureAwait(false);
        }

        private static async Task ValidationAsync(HttpFixture f, CancellationToken ct)
        {
            TestMember member = await NotificationTestKit.MemberAsync(f, "Plain Member", ct).ConfigureAwait(false);
            await ExpectSegmentInvalidAsync(f, BroadcastTestKit.Filter("favoriteColor", "eq", "blue"), "Unknown field", ct).ConfigureAwait(false);
            await ExpectSegmentInvalidAsync(f, BroadcastTestKit.Filter("membership", "eq", "Gold"), "Unknown option", ct).ConfigureAwait(false);
            await ExpectSegmentInvalidAsync(f, BroadcastTestKit.Filter("membership", "like", "Active"), "Unknown operator", ct).ConfigureAwait(false);
            await ExpectSegmentInvalidAsync(f, BroadcastTestKit.Filter("store", "in", new[] { "Amazon" }), "Unknown store", ct).ConfigureAwait(false);
            await ExpectSegmentInvalidAsync(f, BroadcastTestKit.Filter("profile.noSuchField", "eq", "x"), "Unknown profile field", ct).ConfigureAwait(false);
            await ExpectSegmentInvalidAsync(f, BroadcastTestKit.Filter("activeWithinDays", "eq", "ten"), "Bad value type", ct).ConfigureAwait(false);
            await ExpectSegmentInvalidAsync(f, BroadcastTestKit.Filter("role", "in", new[] { "User" }), "User is not a staff role", ct).ConfigureAwait(false);
            await ExpectSegmentInvalidAsync(f, BroadcastTestKit.Filter("tags.level", "contains", "x"), "Unknown tag operator", ct).ConfigureAwait(false);
            await ExpectSegmentInvalidAsync(f, BroadcastTestKit.Filter("eventRsvp", "going", "evt_x"), "eventRsvp with an unknown event", ct, "event").ConfigureAwait(false);
            await ExpectSegmentInvalidAsync(f, BroadcastTestKit.Filter("seriesFollowers", "eq", "ser_x"), "seriesFollowers with an unknown series", ct, "series").ConfigureAwait(false);
            object[] many = Enumerable.Range(0, 21).Select(i => BroadcastTestKit.Filter("hasVerifiedEmail", "eq", true)).ToArray();
            ApiResult tooMany = await Api.SendAsync(f, HttpMethod.Post, _Base + "/segments/preview", HttpFixture.AdminToken, new { segment = new { filters = many } }, ct).ConfigureAwait(false);
            Check.Equal("SegmentInvalid", tooMany.Error, "At most 20 filters");
            ApiResult missing = await Api.SendAsync(f, HttpMethod.Post, _Base + "/segments/preview", HttpFixture.AdminToken, new { segment = new { filters = new[] { new { field = "membership", @operator = "eq" } } } }, ct).ConfigureAwait(false);
            Check.Equal("SegmentInvalid", missing.Error, "Missing value");
            Check.True(missing.Body.GetProperty("fieldErrors").TryGetProperty("segment.filters[0].value", out JsonElement _), "Missing value named");
            ApiResult badCreate = await Api.SendAsync(f, HttpMethod.Post, _Base, HttpFixture.AdminToken, BroadcastTestKit.Draft("Bad", BroadcastTestKit.Segment(BroadcastTestKit.Filter("nope", "eq", "x"))), ct).ConfigureAwait(false);
            Check.Equal("SegmentInvalid", badCreate.Error, "Create with an invalid inline segment");

            JsonElement saved = await Api.ExpectAsync(f, HttpMethod.Post, _Base + "/segments", HttpFixture.AdminToken,
                new { name = "VIPs", description = "Our best", filters = new[] { BroadcastTestKit.Filter("labels", "any", new[] { "vip", "gold" }) }, labels = new[] { "ops" } }, HttpStatusCode.Created, ct).ConfigureAwait(false);
            string segmentId = Api.Str(saved, "id");
            Check.True(segmentId.StartsWith("bsg_", StringComparison.Ordinal), "Segment id prefix");
            JsonElement filter = saved.GetProperty("filters")[0];
            Check.Equal("labels", Api.Str(filter, "field"), "Filter field round trips");
            Check.Equal(2, filter.GetProperty("value").GetArrayLength(), "List value round trips as an array");
            ContentTestKit.CheckMetadata(saved, "segment");
            JsonElement read = await Api.ExpectAsync(f, HttpMethod.Get, _Base + "/segments/" + segmentId, HttpFixture.AdminToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal("VIPs", Api.Str(read, "name"), "Read");
            JsonElement listed = await Api.ExpectAsync(f, HttpMethod.Post, _Base + "/segments/enumerate", HttpFixture.AdminToken, new { labels = new[] { "ops" } }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.True(BroadcastTestKit.Ids(listed).Contains(segmentId), "Listed by label");
            JsonElement none = await Api.ExpectAsync(f, HttpMethod.Post, _Base + "/segments/enumerate", HttpFixture.AdminToken, new { labels = new[] { "other" } }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal(0, none.GetProperty("objects").GetArrayLength(), "Label filter excludes");
            JsonElement renamed = await Api.ExpectAsync(f, HttpMethod.Put, _Base + "/segments/" + segmentId, HttpFixture.AdminToken, new { name = "Renamed" }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal("Renamed", Api.Str(renamed, "name"), "Renamed");
            Check.Equal(1, renamed.GetProperty("filters").GetArrayLength(), "Omitted filters stay");
            JsonElement refiltered = await Api.ExpectAsync(f, HttpMethod.Put, _Base + "/segments/" + segmentId, HttpFixture.AdminToken, new { filters = new[] { BroadcastTestKit.Filter("membership", "eq", "Active") } }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal("membership", Api.Str(refiltered.GetProperty("filters")[0], "field"), "Filters replaced");
            JsonElement byId = await Api.ExpectAsync(f, HttpMethod.Post, _Base + "/segments/preview", HttpFixture.AdminToken, new { segmentId }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal(1L, byId.GetProperty("count").GetInt64(), "Preview by segment id (the one member)");
            Check.Equal(member.Id, Api.Str(byId.GetProperty("sample").GetProperty("objects")[0], "id"), "Sample lists the member");
            await Api.ExpectErrorAsync(f, HttpMethod.Post, _Base + "/segments", member.AccessToken, new { name = "Nope" }, HttpStatusCode.Forbidden, "Forbidden", ct).ConfigureAwait(false);
            await Api.ExpectAsync(f, HttpMethod.Delete, _Base + "/segments/" + segmentId, HttpFixture.AdminToken, null, HttpStatusCode.NoContent, ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Get, _Base + "/segments/" + segmentId, HttpFixture.AdminToken, null, HttpStatusCode.NotFound, "NotFound", ct).ConfigureAwait(false);
        }

        private static async Task<long> CountAsync(HttpFixture f, CancellationToken ct, params object[] filters)
        {
            return await BroadcastTestKit.CountAsync(f, HttpFixture.AdminToken, BroadcastTestKit.Segment(filters), ct).ConfigureAwait(false);
        }

        private static async Task FiltersAsync(HttpFixture f, CancellationToken ct)
        {
            await Api.AdminAsync(f, ct).ConfigureAwait(false);
            TestMember m1 = await NotificationTestKit.MemberAsync(f, "Member One", ct).ConfigureAwait(false);
            TestMember m2 = await NotificationTestKit.MemberAsync(f, "Member Two", ct).ConfigureAwait(false);
            TestMember m3 = await NotificationTestKit.MemberAsync(f, "Member Three", ct).ConfigureAwait(false);
            TestMember m4 = await NotificationTestKit.MemberAsync(f, "Member Four", ct).ConfigureAwait(false);
            TestMember free = await Api.RegisterAsync(f, ct, null, "Free Account").ConfigureAwait(false);
            await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/admin/profile-fields", HttpFixture.AdminToken, new { key = "homeCity", label = "Home city", type = "Text", filterable = true }, HttpStatusCode.Created, ct).ConfigureAwait(false);
            await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/me/profile", m1.AccessToken, new { profile = new { homeCity = "Springfield" } }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            await NotificationTestKit.RegisterTokenAsync(f, m1, ct).ConfigureAwait(false);
            await NotificationTestKit.VerifyEmailAsync(f, m2.Id, ct).ConfigureAwait(false);
            await BroadcastTestKit.LabelAsync(f, m1.Id, ct, "vip").ConfigureAwait(false);
            await BroadcastTestKit.LabelAsync(f, m2.Id, ct, "vip", "gold").ConfigureAwait(false);
            await BroadcastTestKit.LabelAsync(f, m3.Id, ct, "staff:watch").ConfigureAwait(false);
            await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/metadata/User/" + m4.Id, HttpFixture.AdminToken, new { tags = new Dictionary<string, string> { { "level", "pro" } } }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            await f.Raw.ExecuteQueryAsync("UPDATE users SET lastseenutc = NULL;", false, ct).ConfigureAwait(false);
            await f.Raw.ExecuteQueryAsync("UPDATE users SET lastseenutc = now() WHERE id = '" + m1.Id + "';", false, ct).ConfigureAwait(false);

            Check.Equal(5L, await CountAsync(f, ct).ConfigureAwait(false), "FullyGated: four members and the SystemAdmin, not the free account");
            Check.Equal(2L, await CountAsync(f, ct, BroadcastTestKit.Filter("labels", "any", new[] { "vip" })).ConfigureAwait(false), "labels any");
            Check.Equal(1L, await CountAsync(f, ct, BroadcastTestKit.Filter("labels", "all", new[] { "vip", "gold" })).ConfigureAwait(false), "labels all");
            Check.Equal(1L, await CountAsync(f, ct, BroadcastTestKit.Filter("labels", "any", "staff:watch")).ConfigureAwait(false), "staff labels");
            Check.Equal(1L, await CountAsync(f, ct, BroadcastTestKit.Filter("tags.level", "eq", "pro")).ConfigureAwait(false), "tag value");
            Check.Equal(1L, await CountAsync(f, ct, BroadcastTestKit.Filter("tags.level", "exists", true)).ConfigureAwait(false), "tag exists");
            Check.Equal(0L, await CountAsync(f, ct, BroadcastTestKit.Filter("tags.level", "eq", "novice")).ConfigureAwait(false), "tag other value");
            Check.Equal(4L, await CountAsync(f, ct, BroadcastTestKit.Filter("membership", "eq", "Active")).ConfigureAwait(false), "membership Active");
            Check.Equal(1L, await CountAsync(f, ct, BroadcastTestKit.Filter("membership", "eq", "NotActive")).ConfigureAwait(false), "membership NotActive (the SystemAdmin)");
            Check.Equal(5L, await CountAsync(f, ct, BroadcastTestKit.Filter("membership", "eq", "Any")).ConfigureAwait(false), "membership Any");
            Check.Equal(4L, await CountAsync(f, ct, BroadcastTestKit.Filter("membershipSource", "in", new[] { "AdminComp" })).ConfigureAwait(false), "membershipSource AdminComp");
            Check.Equal(0L, await CountAsync(f, ct, BroadcastTestKit.Filter("membershipSource", "eq", "SignupCode")).ConfigureAwait(false), "membershipSource SignupCode");
            Check.Equal(0L, await CountAsync(f, ct, BroadcastTestKit.Filter("store", "in", new[] { "Stripe", "Apple" })).ConfigureAwait(false), "store");
            Check.Equal(1L, await CountAsync(f, ct, BroadcastTestKit.Filter("profile.homeCity", "eq", "springfield")).ConfigureAwait(false), "profile eq (case-insensitive)");
            Check.Equal(1L, await CountAsync(f, ct, BroadcastTestKit.Filter("profile.homeCity", "hasValue", true)).ConfigureAwait(false), "profile hasValue");
            Check.Equal(0L, await CountAsync(f, ct, BroadcastTestKit.Filter("signedUpAfterUtc", "eq", DateTime.UtcNow.AddDays(1).ToString("O"))).ConfigureAwait(false), "signedUpAfterUtc");
            Check.Equal(5L, await CountAsync(f, ct, BroadcastTestKit.Filter("signedUpBeforeUtc", "eq", DateTime.UtcNow.AddDays(1).ToString("O"))).ConfigureAwait(false), "signedUpBeforeUtc");
            Check.Equal(1L, await CountAsync(f, ct, BroadcastTestKit.Filter("activeWithinDays", "eq", 7)).ConfigureAwait(false), "activeWithinDays");
            Check.Equal(4L, await CountAsync(f, ct, BroadcastTestKit.Filter("inactiveForDays", "eq", 30)).ConfigureAwait(false), "inactiveForDays (never seen counts)");
            Check.Equal(1L, await CountAsync(f, ct, BroadcastTestKit.Filter("hasPushToken", "eq", true)).ConfigureAwait(false), "hasPushToken true");
            Check.Equal(4L, await CountAsync(f, ct, BroadcastTestKit.Filter("hasPushToken", "eq", false)).ConfigureAwait(false), "hasPushToken false");
            Check.Equal(1L, await CountAsync(f, ct, BroadcastTestKit.Filter("hasVerifiedEmail", "eq", true)).ConfigureAwait(false), "hasVerifiedEmail");
            Check.Equal(1L, await CountAsync(f, ct, BroadcastTestKit.Filter("role", "in", new[] { "SystemAdmin" })).ConfigureAwait(false), "role SystemAdmin");
            Check.Equal(0L, await CountAsync(f, ct, BroadcastTestKit.Filter("role", "eq", "Communicator")).ConfigureAwait(false), "role Communicator");
            Check.Equal(1L, await CountAsync(f, ct, BroadcastTestKit.Filter("labels", "any", "vip"), BroadcastTestKit.Filter("hasPushToken", "eq", true)).ConfigureAwait(false), "Filters combine with AND");
            Check.Equal(0L, await CountAsync(f, ct, BroadcastTestKit.Filter("hasPushToken", "eq", true), BroadcastTestKit.Filter("hasPushToken", "eq", false)).ConfigureAwait(false), "Contradictions match nobody");
            JsonElement preview = await BroadcastTestKit.PreviewAsync(f, HttpFixture.AdminToken, BroadcastTestKit.Segment(), ct).ConfigureAwait(false);
            Check.Equal(5L, preview.GetProperty("byChannel").GetProperty("inApp").GetInt64(), "byChannel.inApp");
            Check.Equal(1L, preview.GetProperty("byChannel").GetProperty("push").GetInt64(), "byChannel.push (token holders)");
            Check.Equal(1L, preview.GetProperty("byChannel").GetProperty("email").GetInt64(), "byChannel.email (verified addresses)");
            Check.True(preview.GetProperty("sample").GetProperty("objects").GetArrayLength() <= 10 && preview.GetProperty("sample").GetProperty("totalRecords").GetInt64() == 5, "Bounded sample with the total");
            Check.True(!preview.ToString().Contains(free.Id, StringComparison.Ordinal), "The free account is not sampled");
        }

        private static async Task AccessAsync(HttpFixture f, CancellationToken ct)
        {
            await Api.AdminAsync(f, ct).ConfigureAwait(false);
            await NotificationTestKit.MemberAsync(f, "Paying Member", ct).ConfigureAwait(false);
            TestMember free = await Api.RegisterAsync(f, ct, null, "Free Account").ConfigureAwait(false);
            TestMember banned = await NotificationTestKit.MemberAsync(f, "Banned Member", ct).ConfigureAwait(false);
            await f.Raw.ExecuteQueryAsync("UPDATE users SET isbanned = TRUE WHERE id = '" + banned.Id + "';", false, ct).ConfigureAwait(false);
            List<string> unattested = await BroadcastTestKit.InsertUsersAsync(f, 1, DateTime.UtcNow.AddDays(-2), ct).ConfigureAwait(false);
            await f.Raw.ExecuteQueryAsync("UPDATE users SET ageattestedutc = NULL WHERE id = '" + unattested[0] + "';", false, ct).ConfigureAwait(false);
            List<string> deleted = await BroadcastTestKit.InsertUsersAsync(f, 1, DateTime.UtcNow.AddDays(-2), ct).ConfigureAwait(false);
            await f.Raw.ExecuteQueryAsync("UPDATE users SET isdeleted = TRUE WHERE id = '" + deleted[0] + "';", false, ct).ConfigureAwait(false);

            Check.Equal(2L, await CountAsync(f, ct).ConfigureAwait(false), "FullyGated: the member and the SystemAdmin");
            Check.Equal(0L, await CountAsync(f, ct, BroadcastTestKit.Filter("membership", "eq", "NotActive"), BroadcastTestKit.Filter("role", "in", new[] { "Moderator" })).ConfigureAwait(false), "No free accounts in FullyGated");
            await BroadcastTestKit.OpenModelAsync(f, ct).ConfigureAwait(false);
            Check.Equal(3L, await CountAsync(f, ct).ConfigureAwait(false), "Open: every attested account (member, SystemAdmin, free), never banned, deleted, or unattested ones");
            Check.Equal(2L, await CountAsync(f, ct, BroadcastTestKit.Filter("membership", "eq", "NotActive")).ConfigureAwait(false), "Open: NotActive is the free account and the SystemAdmin");
            Check.Equal(1L, await CountAsync(f, ct, BroadcastTestKit.Filter("membership", "eq", "Active")).ConfigureAwait(false), "Open: Active is the member");
            JsonElement preview = await BroadcastTestKit.PreviewAsync(f, HttpFixture.AdminToken, BroadcastTestKit.Segment(), ct).ConfigureAwait(false);
            string text = preview.ToString();
            Check.True(text.Contains(free.Id, StringComparison.Ordinal) && !text.Contains(banned.Id, StringComparison.Ordinal) && !text.Contains(unattested[0], StringComparison.Ordinal) && !text.Contains(deleted[0], StringComparison.Ordinal), "Sample follows the base rules");
        }

        private static async Task IdempotentAsync(HttpFixture f, CancellationToken ct)
        {
            await BroadcastTestKit.OpenModelAsync(f, ct).ConfigureAwait(false);
            f.Settings.Broadcasts.BatchSize = 50;
            await BroadcastTestKit.InsertUsersAsync(f, 120, _Bulk, ct).ConfigureAwait(false);
            object segment = BroadcastTestKit.BulkSegment(_Bulk);
            long count = await BroadcastTestKit.CountAsync(f, HttpFixture.AdminToken, segment, ct).ConfigureAwait(false);
            Check.Equal(120L, count, "Preview count");
            string id = await BroadcastTestKit.CreateAsync(f, HttpFixture.AdminToken, BroadcastTestKit.Draft("Bulk", segment, email: true), ct).ConfigureAwait(false);
            int events = 0;
            Func<RealtimeEvent, Task> handler = e =>
            {
                Interlocked.Increment(ref events);
                return Task.CompletedTask;
            };
            f.Services.RealtimeBus.EventReceived += handler;
            try
            {
                JsonElement sending = await BroadcastTestKit.SendAsync(f, HttpFixture.AdminToken, id, count, ct).ConfigureAwait(false);
                Check.Equal("Sending", Api.Str(sending, "status"), "Sending");
                BroadcastSendService first = new BroadcastSendService(f.Services, f.Services.Broadcasts.Segments) { BatchesPerPass = 1 };
                await first.RunPassAsync(ct).ConfigureAwait(false);
                Check.Equal(50L, await BroadcastTestKit.DeliveriesAsync(f, id, ct).ConfigureAwait(false), "One batch of broadcasts.batchSize");
                Check.Equal(50L, await BroadcastTestKit.NotificationsAsync(f, id, ct).ConfigureAwait(false), "One notification per delivered recipient");
                // A worker that crashed before recording its cursor: a new instance repeats the first batch harmlessly.
                await f.Raw.ExecuteQueryAsync("UPDATE broadcasts SET cursoruserid = NULL WHERE id = '" + id + "';", false, ct).ConfigureAwait(false);
                BroadcastSendService restarted = new BroadcastSendService(f.Services, f.Services.Broadcasts.Segments) { BatchesPerPass = 1 };
                await restarted.RunPassAsync(ct).ConfigureAwait(false);
                Check.Equal(50L, await BroadcastTestKit.DeliveriesAsync(f, id, ct).ConfigureAwait(false), "The repeated batch wrote nothing new");
                Check.Equal(50L, await BroadcastTestKit.NotificationsAsync(f, id, ct).ConfigureAwait(false), "No duplicate notifications");
                await BroadcastTestKit.DrainAsync(f, ct, restarted).ConfigureAwait(false);
            }
            finally
            {
                f.Services.RealtimeBus.EventReceived -= handler;
            }
            Check.Equal(count, await BroadcastTestKit.DeliveriesAsync(f, id, ct).ConfigureAwait(false), "Sent count equals the preview count");
            Check.Equal(count, await BroadcastTestKit.ScalarAsync(f, "SELECT COUNT(DISTINCT recipientuserid) AS cnt FROM notifications WHERE type = 'Broadcast' AND targetid = '" + id + "';", ct).ConfigureAwait(false), "One notification per recipient");
            Check.Equal(count, await BroadcastTestKit.NotificationsAsync(f, id, ct).ConfigureAwait(false), "No duplicates overall");
            Check.Equal(count, (long)f.Fakes.Email.Messages.Count(m => m.Category == BroadcastEmail.TemplateKey), "One email per recipient");
            Check.Equal(0, events, "No realtime bus event per recipient");
            JsonElement sent = await Api.ExpectAsync(f, HttpMethod.Get, _Base + "/" + id, HttpFixture.AdminToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal("Sent", Api.Str(sent, "status"), "Sent");
            Check.True(sent.GetProperty("sentUtc").ValueKind == JsonValueKind.String, "sentUtc set");
            Check.Equal(120, sent.GetProperty("stats").GetProperty("targeted").GetInt32(), "stats.targeted");
            Check.Equal(120, sent.GetProperty("stats").GetProperty("inAppCreated").GetInt32(), "stats.inAppCreated");
            Check.Equal(120, sent.GetProperty("stats").GetProperty("emailQueued").GetInt32(), "stats.emailQueued");
            Check.Equal(120, sent.GetProperty("stats").GetProperty("emailSent").GetInt32(), "stats.emailSent");
            JsonElement deliveries = await Api.ExpectAsync(f, HttpMethod.Post, _Base + "/" + id + "/deliveries/enumerate", HttpFixture.AdminToken, new { maxResults = 10, filters = new { channel = "email", status = "Sent" } }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal(10, deliveries.GetProperty("objects").GetArrayLength(), "Deliveries page");
            Check.Equal(120L, deliveries.GetProperty("totalRecords").GetInt64(), "Deliveries filtered by channel and status");
            ContentTestKit.CheckMetadata(deliveries.GetProperty("objects")[0], "delivery");
        }

        private static async Task CancelAsync(HttpFixture f, CancellationToken ct)
        {
            await BroadcastTestKit.OpenModelAsync(f, ct).ConfigureAwait(false);
            f.Settings.Broadcasts.BatchSize = 50;
            await BroadcastTestKit.InsertUsersAsync(f, 120, _Bulk, ct).ConfigureAwait(false);
            object segment = BroadcastTestKit.BulkSegment(_Bulk);
            string id = await BroadcastTestKit.CreateAsync(f, HttpFixture.AdminToken, BroadcastTestKit.Draft("Cancel me", segment), ct).ConfigureAwait(false);
            await BroadcastTestKit.SendAsync(f, HttpFixture.AdminToken, id, 120, ct).ConfigureAwait(false);
            BroadcastSendService sender = new BroadcastSendService(f.Services, f.Services.Broadcasts.Segments) { BatchesPerPass = 1 };
            await sender.RunPassAsync(ct).ConfigureAwait(false);
            Check.Equal(50L, await BroadcastTestKit.DeliveriesAsync(f, id, ct).ConfigureAwait(false), "First batch delivered");
            JsonElement canceled = await Api.ExpectAsync(f, HttpMethod.Post, _Base + "/" + id + "/cancel", HttpFixture.AdminToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal("Canceled", Api.Str(canceled, "status"), "Canceled");
            Check.True(canceled.GetProperty("canceledUtc").ValueKind == JsonValueKind.String, "canceledUtc");
            await sender.RunPassAsync(ct).ConfigureAwait(false);
            await sender.RunPassAsync(ct).ConfigureAwait(false);
            Check.Equal(50L, await BroadcastTestKit.DeliveriesAsync(f, id, ct).ConfigureAwait(false), "Remaining batches stopped");
            Check.Equal(50, (await Api.ExpectAsync(f, HttpMethod.Get, _Base + "/" + id, HttpFixture.AdminToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false)).GetProperty("stats").GetProperty("targeted").GetInt32(), "stats.targeted");
            await Api.ExpectErrorAsync(f, HttpMethod.Post, _Base + "/" + id + "/cancel", HttpFixture.AdminToken, null, HttpStatusCode.Conflict, "BroadcastNotEditable", ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Put, _Base + "/" + id, HttpFixture.AdminToken, new { title = "Too late" }, HttpStatusCode.Conflict, "BroadcastNotEditable", ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Post, _Base + "/" + id + "/send", HttpFixture.AdminToken, new { confirmCount = 120 }, HttpStatusCode.Conflict, "BroadcastNotEditable", ct).ConfigureAwait(false);
            JsonElement list = await Api.ExpectAsync(f, HttpMethod.Post, _Base + "/enumerate", HttpFixture.AdminToken, new { filters = new { status = "Canceled" } }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.True(BroadcastTestKit.Ids(list).Contains(id), "Listed by status");
        }

        private static async Task ScheduleAsync(HttpFixture f, CancellationToken ct)
        {
            await BroadcastTestKit.OpenModelAsync(f, ct).ConfigureAwait(false);
            await BroadcastTestKit.InsertUsersAsync(f, 40, _Bulk, ct).ConfigureAwait(false);
            object segment = BroadcastTestKit.BulkSegment(_Bulk);
            string now = await BroadcastTestKit.CreateAsync(f, HttpFixture.AdminToken, BroadcastTestKit.Draft("Now", segment), ct).ConfigureAwait(false);
            ApiResult off = await Api.SendAsync(f, HttpMethod.Post, _Base + "/" + now + "/send", HttpFixture.AdminToken, new { confirmCount = 30 }, ct).ConfigureAwait(false);
            Check.Equal(HttpStatusCode.BadRequest, off.Status, "confirmCount off by 25 percent");
            Check.Equal("ValidationFailed", off.Error, "ValidationFailed");
            Check.True(off.Body.GetProperty("fieldErrors").TryGetProperty("confirmCount", out JsonElement _), "fieldErrors.confirmCount");
            await Api.ExpectErrorAsync(f, HttpMethod.Post, _Base + "/" + now + "/send", HttpFixture.AdminToken, new { }, HttpStatusCode.BadRequest, "ValidationFailed", ct).ConfigureAwait(false);
            await BroadcastTestKit.SendAsync(f, HttpFixture.AdminToken, now, 39, ct).ConfigureAwait(false);
            await BroadcastTestKit.DrainAsync(f, ct).ConfigureAwait(false);
            Check.Equal("Sent", await BroadcastTestKit.StatusAsync(f, HttpFixture.AdminToken, now, ct).ConfigureAwait(false), "Sent within 5 percent");
            await Api.ExpectErrorAsync(f, HttpMethod.Put, _Base + "/" + now, HttpFixture.AdminToken, new { title = "Edit" }, HttpStatusCode.Conflict, "BroadcastNotEditable", ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Delete, _Base + "/" + now, HttpFixture.AdminToken, null, HttpStatusCode.Conflict, "BroadcastNotEditable", ct).ConfigureAwait(false);

            string later = await BroadcastTestKit.CreateAsync(f, HttpFixture.AdminToken, BroadcastTestKit.Draft("Later", segment), ct).ConfigureAwait(false);
            DateTime start = DateTime.UtcNow;
            foreach (DateTime bad in new[] { start.AddMinutes(1), start.AddDays(91), start.AddMinutes(-5) })
            {
                ApiResult r = await Api.SendAsync(f, HttpMethod.Post, _Base + "/" + later + "/schedule", HttpFixture.AdminToken, new { sendAtUtc = bad }, ct).ConfigureAwait(false);
                Check.Equal(HttpStatusCode.BadRequest, r.Status, "Schedule window (" + bad.ToString("O") + ")");
                Check.True(r.Body.GetProperty("fieldErrors").TryGetProperty("sendAtUtc", out JsonElement _), "fieldErrors.sendAtUtc");
            }
            JsonElement scheduled = await Api.ExpectAsync(f, HttpMethod.Post, _Base + "/" + later + "/schedule", HttpFixture.AdminToken, new { sendAtUtc = start.AddMinutes(10) }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal("Scheduled", Api.Str(scheduled, "status"), "Scheduled");
            JsonElement edited = await Api.ExpectAsync(f, HttpMethod.Put, _Base + "/" + later, HttpFixture.AdminToken, new { title = "Later, edited" }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal("Scheduled", Api.Str(edited, "status"), "Edits keep the schedule");
            Check.Equal("Later, edited", Api.Str(edited, "title"), "Edited while scheduled");
            await Api.ExpectErrorAsync(f, HttpMethod.Delete, _Base + "/" + later, HttpFixture.AdminToken, null, HttpStatusCode.Conflict, "BroadcastNotEditable", ct).ConfigureAwait(false);
            BroadcastSendService sender = new BroadcastSendService(f.Services, f.Services.Broadcasts.Segments);
            await sender.RunPassAsync(ct).ConfigureAwait(false);
            Check.Equal("Scheduled", await BroadcastTestKit.StatusAsync(f, HttpFixture.AdminToken, later, ct).ConfigureAwait(false), "Not due yet");
            sender.Clock = () => DateTime.UtcNow.AddMinutes(11);
            await sender.RunPassAsync(ct).ConfigureAwait(false);
            Check.Equal("Sent", await BroadcastTestKit.StatusAsync(f, HttpFixture.AdminToken, later, ct).ConfigureAwait(false), "Sent when due on the worker clock");
            Check.Equal(40L, await BroadcastTestKit.DeliveriesAsync(f, later, ct).ConfigureAwait(false), "Every recipient");
            JsonElement audit = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/admin/audit?action=broadcast.schedule", HttpFixture.AdminToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.True(audit.GetProperty("totalRecords").GetInt64() >= 1, "Audited broadcast.schedule");
        }

        private static async Task LimitsAsync(HttpFixture f, CancellationToken ct)
        {
            TestMember admin = await Api.AdminAsync(f, ct).ConfigureAwait(false);
            await NotificationTestKit.MemberAsync(f, "Limit One", ct).ConfigureAwait(false);
            await NotificationTestKit.MemberAsync(f, "Limit Two", ct).ConfigureAwait(false);
            f.Settings.Broadcasts.MaxPerDay = 1;
            object everyone = BroadcastTestKit.Segment();
            string first = await BroadcastTestKit.CreateAsync(f, admin.AccessToken, BroadcastTestKit.Draft("First", everyone), ct).ConfigureAwait(false);
            string second = await BroadcastTestKit.CreateAsync(f, admin.AccessToken, BroadcastTestKit.Draft("Second", everyone), ct).ConfigureAwait(false);
            await BroadcastTestKit.SendAsync(f, admin.AccessToken, first, 3, ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Post, _Base + "/" + second + "/send", admin.AccessToken, new { confirmCount = 3 }, HttpStatusCode.Conflict, "BroadcastLimitReached", ct).ConfigureAwait(false);
            DateTime soon = DateTime.UtcNow.AddMinutes(10);
            ApiResult sameDay = await Api.SendAsync(f, HttpMethod.Post, _Base + "/" + second + "/schedule", admin.AccessToken, new { sendAtUtc = soon }, ct).ConfigureAwait(false);
            if (soon.Date == DateTime.UtcNow.Date) Check.Equal("BroadcastLimitReached", sameDay.Error, "Scheduling into a full day");
            DateTime tomorrow = DateTime.UtcNow.Date.AddDays(1).AddHours(12);
            JsonElement scheduled = await Api.ExpectAsync(f, HttpMethod.Post, _Base + "/" + second + "/schedule", admin.AccessToken, new { sendAtUtc = tomorrow }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal("Scheduled", Api.Str(scheduled, "status"), "Another day has room");
            await Api.ExpectAsync(f, HttpMethod.Post, _Base + "/" + second + "/test", admin.AccessToken, new { channels = new[] { "inApp" } }, HttpStatusCode.Accepted, ct).ConfigureAwait(false);
            f.Settings.Broadcasts.MaxPerDay = 5;
            f.Settings.Broadcasts.MaxRecipients = 1;
            string third = await BroadcastTestKit.CreateAsync(f, admin.AccessToken, BroadcastTestKit.Draft("Third", everyone), ct).ConfigureAwait(false);
            ApiResult tooMany = await Api.SendAsync(f, HttpMethod.Post, _Base + "/" + third + "/send", admin.AccessToken, new { confirmCount = 3 }, ct).ConfigureAwait(false);
            Check.Equal("ValidationFailed", tooMany.Error, "maxRecipients");
            Check.True(tooMany.Body.GetProperty("fieldErrors").TryGetProperty("segment", out JsonElement _), "fieldErrors.segment");
            f.Settings.Broadcasts.MaxRecipients = 100000;
            await BroadcastTestKit.SendAsync(f, admin.AccessToken, third, 3, ct).ConfigureAwait(false);
        }

        private static PushDeliveryWorker PushWorker(HttpFixture f)
        {
            // The fixture's own push worker may hold the push_delivery lease; a clock past its lease lets this worker run.
            PushDeliveryWorker worker = NotificationTestKit.PushWorker(f, () => DateTime.UtcNow.AddMinutes(10));
            worker.PreferenceExemptions = f.Services.Broadcasts.PushExemptionsAsync;
            return worker;
        }

        private static async Task PushAllAsync(HttpFixture f, PushDeliveryWorker worker, CancellationToken ct)
        {
            int processed = 0;
            for (int i = 0; i < 200; i++)
            {
                processed += await worker.RunOnceAsync(ct).ConfigureAwait(false);
                if (await BroadcastTestKit.ScalarAsync(f, "SELECT COUNT(*) AS cnt FROM notifications WHERE pushstate = 'Pending';", ct).ConfigureAwait(false) == 0) return;
                await Task.Delay(50, ct).ConfigureAwait(false);
            }
            DataTable leases = await f.Raw.ExecuteQueryAsync("SELECT jobname, holder, leaseuntilutc FROM jobleases;", false, ct).ConfigureAwait(false);
            throw new InvalidOperationException("Pushes stayed pending (processed " + processed + "; leases " + String.Join("; ", leases.Rows.Cast<DataRow>().Select(r => r[0] + "=" + r[1] + "@" + r[2])) + ").");
        }

        private static async Task<string> StatusOfAsync(HttpFixture f, string broadcastId, string userId, string column, CancellationToken ct)
        {
            DataTable row = await f.Raw.ExecuteQueryAsync("SELECT " + column + " FROM broadcastdeliveries WHERE broadcastid = '" + broadcastId + "' AND userid = '" + userId + "';", false, ct).ConfigureAwait(false);
            return row.Rows.Count == 0 ? "none" : RowReader.GetString(row.Rows[0], column);
        }

        private static async Task PreferencesAsync(HttpFixture f, CancellationToken ct)
        {
            TestMember admin = await Api.AdminAsync(f, ct).ConfigureAwait(false);
            TestMember comms = await BroadcastTestKit.CommunicatorAsync(f, ct).ConfigureAwait(false);
            TestMember a = await NotificationTestKit.MemberAsync(f, "Alice Default", ct).ConfigureAwait(false);
            TestMember b = await NotificationTestKit.MemberAsync(f, "Bob OptedOut", ct).ConfigureAwait(false);
            TestMember c = await NotificationTestKit.MemberAsync(f, "Cara Suppressed", ct).ConfigureAwait(false);
            TestMember d = await NotificationTestKit.MemberAsync(f, "Dan Unverified", ct).ConfigureAwait(false);
            PushDeliveryWorker pusher = PushWorker(f);
            Dictionary<string, string> tokens = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (TestMember m in new[] { a, b, c, d })
            {
                tokens[m.Id] = await NotificationTestKit.RegisterTokenAsync(f, m, ct).ConfigureAwait(false);
                await BroadcastTestKit.LabelAsync(f, m.Id, ct, "pref-test").ConfigureAwait(false);
                if (m != d) await NotificationTestKit.VerifyEmailAsync(f, m.Id, ct).ConfigureAwait(false);
            }
            await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/me/notification-preferences", b.AccessToken, new { categories = new[] { new { category = "Announcements", push = false, email = false } } }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            ApiResult inApp = await Api.SendAsync(f, HttpMethod.Put, "/api/v1/me/notification-preferences", b.AccessToken, new { categories = new[] { new { category = "Announcements", inApp = false } } }, ct).ConfigureAwait(false);
            Check.Equal(HttpStatusCode.BadRequest, inApp.Status, "Announcements in-app is not editable");
            await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/admin/email/suppressions", HttpFixture.AdminToken, new { email = c.Email, note = "Bounced" }, HttpStatusCode.Created, ct).ConfigureAwait(false);
            object segment = BroadcastTestKit.Segment(BroadcastTestKit.Filter("labels", "any", "pref-test"));
            JsonElement preview = await BroadcastTestKit.PreviewAsync(f, comms.AccessToken, segment, ct).ConfigureAwait(false);
            Check.Equal(4L, preview.GetProperty("count").GetInt64(), "Four recipients");
            Check.Equal(3L, preview.GetProperty("byChannel").GetProperty("push").GetInt64(), "Push: tokens and the preference");
            Check.Equal(1L, preview.GetProperty("byChannel").GetProperty("email").GetInt64(), "Email: verified, preference on, not suppressed");

            string optional = await BroadcastTestKit.CreateAsync(f, comms.AccessToken, BroadcastTestKit.Draft("Optional", segment, push: true, email: true), ct).ConfigureAwait(false);
            await BroadcastTestKit.SendAsync(f, comms.AccessToken, optional, 4, ct).ConfigureAwait(false);
            await BroadcastTestKit.DrainAsync(f, ct).ConfigureAwait(false);
            Check.Equal("Sent", await StatusOfAsync(f, optional, a.Id, "emailstatus", ct).ConfigureAwait(false), "A email sent");
            Check.Equal("SkippedPreference", await StatusOfAsync(f, optional, b.Id, "emailstatus", ct).ConfigureAwait(false), "B email skipped by preference");
            Check.Equal("SkippedPreference", await StatusOfAsync(f, optional, b.Id, "pushstatus", ct).ConfigureAwait(false), "B push skipped by preference");
            Check.Equal("Suppressed", await StatusOfAsync(f, optional, c.Id, "emailstatus", ct).ConfigureAwait(false), "C suppressed");
            Check.Equal("NoAddress", await StatusOfAsync(f, optional, d.Id, "emailstatus", ct).ConfigureAwait(false), "D unverified");
            Check.Equal("Created", await StatusOfAsync(f, optional, b.Id, "inappstatus", ct).ConfigureAwait(false), "In-app always");
            List<TrellisKit.Core.Integrations.Models.EmailMessage> aEmails = f.Fakes.Email.To(a.Email, BroadcastEmail.TemplateKey);
            Check.Equal(1, aEmails.Count, "A got one email");
            Check.Equal("Optional subject", aEmails[0].Subject, "Subject");
            Check.True(aEmails[0].Headers.TryGetValue("List-Unsubscribe", out string? unsubscribe) && unsubscribe.Contains("/api/v1/email/unsubscribe?token=", StringComparison.Ordinal), "One-click unsubscribe header");
            Check.True(aEmails[0].Headers.ContainsKey("List-Unsubscribe-Post"), "List-Unsubscribe-Post");
            Check.True((aEmails[0].HtmlBody ?? String.Empty).Contains("<strong>body</strong>", StringComparison.Ordinal) && (aEmails[0].HtmlBody ?? String.Empty).Contains("href=\"https://example.test/x\"", StringComparison.Ordinal), "Markdown rendered");
            Check.Equal(0, f.Fakes.Email.To(b.Email, BroadcastEmail.TemplateKey).Count + f.Fakes.Email.To(c.Email, BroadcastEmail.TemplateKey).Count + f.Fakes.Email.To(d.Email, BroadcastEmail.TemplateKey).Count, "Nobody else got email");
            await PushAllAsync(f, pusher, ct).ConfigureAwait(false);
            Check.Equal(1, f.Fakes.Push.To(tokens[a.Id]).Count, "A got the push");
            Check.Equal(0, f.Fakes.Push.To(tokens[b.Id]).Count, "B's push preference is off");
            Check.Equal("Optional push", f.Fakes.Push.To(tokens[a.Id])[0].Title, "Push title");
            await BroadcastTestKit.DrainAsync(f, ct).ConfigureAwait(false);
            JsonElement stats = (await Api.ExpectAsync(f, HttpMethod.Get, _Base + "/" + optional, comms.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false)).GetProperty("stats");
            Check.Equal(4, stats.GetProperty("targeted").GetInt32(), "targeted");
            Check.Equal(4, stats.GetProperty("inAppCreated").GetInt32(), "inAppCreated");
            Check.Equal(3, stats.GetProperty("pushSent").GetInt32(), "pushSent from the push states");
            Check.Equal(1, stats.GetProperty("emailSent").GetInt32(), "emailSent");
            Check.Equal(1, stats.GetProperty("emailSuppressed").GetInt32(), "emailSuppressed");
            Check.Equal(1, stats.GetProperty("skippedPreferences").GetInt32(), "skippedPreferences");

            ApiResult commsEssential = await Api.SendAsync(f, HttpMethod.Post, _Base, comms.AccessToken, BroadcastTestKit.Draft("Policy", segment, push: true, email: true, category: "Essential", reason: "Terms of service change"), ct).ConfigureAwait(false);
            Check.Equal(HttpStatusCode.Forbidden, commsEssential.Status, "A Communicator cannot create an Essential broadcast");
            ApiResult noReason = await Api.SendAsync(f, HttpMethod.Post, _Base, admin.AccessToken, BroadcastTestKit.Draft("Policy", segment, push: true, email: true, category: "Essential"), ct).ConfigureAwait(false);
            Check.True(noReason.Status == HttpStatusCode.BadRequest && noReason.Body.GetProperty("fieldErrors").TryGetProperty("essentialReason", out JsonElement _), "Essential needs a reason");
            string essential = await BroadcastTestKit.CreateAsync(f, admin.AccessToken, BroadcastTestKit.Draft("Policy", segment, push: true, email: true, category: "Essential", reason: "Terms of service change"), ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Post, _Base + "/" + essential + "/send", comms.AccessToken, new { confirmCount = 4 }, HttpStatusCode.Forbidden, "Forbidden", ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Put, _Base + "/" + essential, comms.AccessToken, new { title = "Changed" }, HttpStatusCode.Forbidden, "Forbidden", ct).ConfigureAwait(false);
            JsonElement essentialPreview = await BroadcastTestKit.PreviewAsync(f, admin.AccessToken, segment, ct, "Essential").ConfigureAwait(false);
            Check.Equal(4L, essentialPreview.GetProperty("byChannel").GetProperty("push").GetInt64(), "Essential push ignores the preference");
            Check.Equal(2L, essentialPreview.GetProperty("byChannel").GetProperty("email").GetInt64(), "Essential email ignores the preference, never suppressions");
            await BroadcastTestKit.SendAsync(f, admin.AccessToken, essential, 4, ct).ConfigureAwait(false);
            await BroadcastTestKit.DrainAsync(f, ct).ConfigureAwait(false);
            Check.Equal("Sent", await StatusOfAsync(f, essential, b.Id, "emailstatus", ct).ConfigureAwait(false), "B gets the essential email");
            Check.Equal("Suppressed", await StatusOfAsync(f, essential, c.Id, "emailstatus", ct).ConfigureAwait(false), "Suppressions still apply");
            List<TrellisKit.Core.Integrations.Models.EmailMessage> bEmails = f.Fakes.Email.To(b.Email, BroadcastEmail.TemplateKey);
            Check.Equal(1, bEmails.Count, "B got the essential email");
            Check.True(!bEmails[0].Headers.ContainsKey("List-Unsubscribe"), "No unsubscribe header on an essential notice");
            Check.True(bEmails[0].TextBody.Contains("essential notice", StringComparison.Ordinal), "Essential footer");
            await PushAllAsync(f, pusher, ct).ConfigureAwait(false);
            Check.Equal(1, f.Fakes.Push.To(tokens[b.Id]).Count, "B got the essential push");
            JsonElement audit = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/admin/audit?action=broadcast.essential", HttpFixture.AdminToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.True(audit.GetProperty("totalRecords").GetInt64() >= 2, "Audited broadcast.essential (create and send)");
        }

        private static async Task RolesAsync(HttpFixture f, CancellationToken ct)
        {
            TestMember comms = await BroadcastTestKit.CommunicatorAsync(f, ct).ConfigureAwait(false);
            TestMember member = await NotificationTestKit.MemberAsync(f, "Role Member", ct).ConfigureAwait(false);
            object segment = BroadcastTestKit.Segment(BroadcastTestKit.Filter("membership", "eq", "Active"));
            JsonElement seg = await Api.ExpectAsync(f, HttpMethod.Post, _Base + "/segments", comms.AccessToken, new { name = "Members", filters = new[] { BroadcastTestKit.Filter("membership", "eq", "Active") }, labels = new[] { "staff:internal" } }, HttpStatusCode.Created, ct).ConfigureAwait(false);
            Check.True(seg.GetProperty("labels").EnumerateArray().Any(l => l.GetString() == "staff:internal"), "Communicators are staff for segment metadata");
            string draft = await BroadcastTestKit.CreateAsync(f, comms.AccessToken, new { title = "From comms", inApp = new { body = "Hi" }, segmentId = Api.Str(seg, "id") }, ct).ConfigureAwait(false);
            JsonElement read = await Api.ExpectAsync(f, HttpMethod.Get, _Base + "/" + draft, comms.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal(Api.Str(seg, "id"), Api.Str(read.GetProperty("segment"), "id"), "Copied from the saved segment");
            Check.Equal("Members", Api.Str(read.GetProperty("segment"), "name"), "Segment name");
            Check.True(read.GetProperty("channels").GetProperty("inApp").GetBoolean() && !read.GetProperty("channels").GetProperty("push").GetBoolean(), "Default channels: in-app only");
            await Api.ExpectAsync(f, HttpMethod.Put, _Base + "/" + draft, comms.AccessToken, new { title = "From comms, edited" }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            long count = await BroadcastTestKit.CountAsync(f, comms.AccessToken, segment, ct).ConfigureAwait(false);
            Check.Equal(1L, count, "The member");
            await BroadcastTestKit.SendAsync(f, comms.AccessToken, draft, count, ct).ConfigureAwait(false);
            await BroadcastTestKit.DrainAsync(f, ct).ConfigureAwait(false);
            await Api.ExpectAsync(f, HttpMethod.Post, _Base + "/" + draft + "/deliveries/enumerate", comms.AccessToken, new { }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            string other = await BroadcastTestKit.CreateAsync(f, comms.AccessToken, BroadcastTestKit.Draft("Other", segment), ct).ConfigureAwait(false);
            await Api.ExpectAsync(f, HttpMethod.Post, _Base + "/" + other + "/schedule", comms.AccessToken, new { sendAtUtc = DateTime.UtcNow.AddDays(2) }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            await Api.ExpectAsync(f, HttpMethod.Post, _Base + "/" + other + "/cancel", comms.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            string doomed = await BroadcastTestKit.CreateAsync(f, comms.AccessToken, BroadcastTestKit.Draft("Doomed", segment), ct).ConfigureAwait(false);
            await Api.ExpectAsync(f, HttpMethod.Delete, _Base + "/" + doomed, comms.AccessToken, null, HttpStatusCode.NoContent, ct).ConfigureAwait(false);
            await Api.ExpectAsync(f, HttpMethod.Post, _Base + "/enumerate", comms.AccessToken, new { }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            foreach (string path in new[] { "/api/v1/admin/members", "/api/v1/admin/audit", "/api/v1/admin/forums", "/api/v1/admin/email/templates" })
                await Api.ExpectErrorAsync(f, HttpMethod.Get, path, comms.AccessToken, null, HttpStatusCode.Forbidden, "Forbidden", ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Post, _Base, member.AccessToken, BroadcastTestKit.Draft("Member", segment), HttpStatusCode.Forbidden, "Forbidden", ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Post, _Base + "/segments/preview", member.AccessToken, new { }, HttpStatusCode.Forbidden, "Forbidden", ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Get, _Base + "/" + draft, member.AccessToken, null, HttpStatusCode.Forbidden, "Forbidden", ct).ConfigureAwait(false);
            foreach (string action in new[] { "broadcast.create", "broadcast.send", "broadcast.schedule", "broadcast.cancel", "broadcast.delete" })
            {
                JsonElement audit = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/admin/audit?action=" + action, HttpFixture.AdminToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                Check.True(audit.GetProperty("totalRecords").GetInt64() >= 1, "Audited " + action);
            }
        }

        private static async Task TestSendAsync(HttpFixture f, CancellationToken ct)
        {
            TestMember admin = await Api.AdminAsync(f, ct).ConfigureAwait(false);
            TestMember member = await NotificationTestKit.MemberAsync(f, "Bystander", ct).ConfigureAwait(false);
            await NotificationTestKit.VerifyEmailAsync(f, admin.Id, ct).ConfigureAwait(false);
            await NotificationTestKit.VerifyEmailAsync(f, member.Id, ct).ConfigureAwait(false);
            string id = await BroadcastTestKit.CreateAsync(f, admin.AccessToken, BroadcastTestKit.Draft("Preview me", BroadcastTestKit.Segment(), email: true), ct).ConfigureAwait(false);
            JsonElement test = await Api.ExpectAsync(f, HttpMethod.Post, _Base + "/" + id + "/test", admin.AccessToken, new { channels = new[] { "inApp", "email" } }, HttpStatusCode.Accepted, ct).ConfigureAwait(false);
            string copy = Api.Str(test, "announcementId");
            Check.True(copy.Length > 0 && copy != id, "A separate test copy");
            Check.Equal(2, test.GetProperty("channels").GetArrayLength(), "Chosen channels");
            Check.Equal(1L, await BroadcastTestKit.DeliveriesAsync(f, copy, ct).ConfigureAwait(false), "One delivery: the caller");
            Check.Equal(1L, await BroadcastTestKit.DeliveriesAsync(f, copy, ct, "userid = '" + admin.Id + "'").ConfigureAwait(false), "To the caller");
            Check.Equal(0L, await BroadcastTestKit.DeliveriesAsync(f, id, ct).ConfigureAwait(false), "The broadcast itself is untouched");
            List<TrellisKit.Core.Integrations.Models.EmailMessage> emails = f.Fakes.Email.To(admin.Email, BroadcastEmail.TemplateKey);
            Check.Equal(1, emails.Count, "Test email to the caller");
            Check.True(emails[0].Subject.StartsWith("[Test]", StringComparison.Ordinal), "Marked Test (" + emails[0].Subject + ")");
            Check.Equal(0, f.Fakes.Email.To(member.Email, BroadcastEmail.TemplateKey).Count, "Nobody else");
            JsonElement announcement = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/announcements/" + copy, admin.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.True(Api.Str(announcement, "title").StartsWith("[Test]", StringComparison.Ordinal), "Announcement marked Test");
            await Api.ExpectErrorAsync(f, HttpMethod.Get, "/api/v1/announcements/" + copy, member.AccessToken, null, HttpStatusCode.NotFound, "NotFound", ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Get, _Base + "/" + copy, admin.AccessToken, null, HttpStatusCode.NotFound, "NotFound", ct).ConfigureAwait(false);
            JsonElement list = await Api.ExpectAsync(f, HttpMethod.Post, _Base + "/enumerate", admin.AccessToken, new { }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.True(!BroadcastTestKit.Ids(list).Contains(copy) && BroadcastTestKit.Ids(list).Contains(id), "Test copies are never listed");
            Check.Equal("Draft", await BroadcastTestKit.StatusAsync(f, admin.AccessToken, id, ct).ConfigureAwait(false), "Still a draft");
            await Api.ExpectErrorAsync(f, HttpMethod.Post, _Base + "/" + id + "/test", admin.AccessToken, new { channels = new[] { "push" } }, HttpStatusCode.BadRequest, "ValidationFailed", ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Post, _Base + "/" + id + "/test", admin.AccessToken, new { channels = new[] { "fax" } }, HttpStatusCode.BadRequest, "ValidationFailed", ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Post, _Base + "/" + id + "/test", HttpFixture.AdminToken, new { }, HttpStatusCode.Forbidden, "Forbidden", ct).ConfigureAwait(false);
            JsonElement audit = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/admin/audit?action=broadcast.test", HttpFixture.AdminToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.True(audit.GetProperty("totalRecords").GetInt64() >= 1, "Audited broadcast.test");
        }

        private static async Task AnnouncementsAsync(HttpFixture f, CancellationToken ct)
        {
            TestMember admin = await Api.AdminAsync(f, ct).ConfigureAwait(false);
            TestMember recipient = await NotificationTestKit.MemberAsync(f, "Recipient", ct).ConfigureAwait(false);
            TestMember outsider = await NotificationTestKit.MemberAsync(f, "Outsider", ct).ConfigureAwait(false);
            await BroadcastTestKit.LabelAsync(f, recipient.Id, ct, "ann").ConfigureAwait(false);
            object segment = BroadcastTestKit.Segment(BroadcastTestKit.Filter("labels", "any", "ann"));
            JsonElement created = await Api.ExpectAsync(f, HttpMethod.Post, _Base, admin.AccessToken,
                new { title = "Internal title", inApp = new { title = "Holiday schedule", body = "Opening hours **change** next week." }, segment, labels = new[] { "news" }, tags = new Dictionary<string, string> { { "season", "winter" } } }, HttpStatusCode.Created, ct).ConfigureAwait(false);
            string id = Api.Str(created, "id");
            Check.Equal("/announcements/" + id, Api.Str(created.GetProperty("inApp"), "path"), "inApp.path");
            await Api.ExpectErrorAsync(f, HttpMethod.Get, "/api/v1/announcements/" + id, recipient.AccessToken, null, HttpStatusCode.NotFound, "NotFound", ct).ConfigureAwait(false);
            await BroadcastTestKit.SendAsync(f, admin.AccessToken, id, 1, ct).ConfigureAwait(false);
            await BroadcastTestKit.DrainAsync(f, ct).ConfigureAwait(false);
            JsonElement announcement = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/announcements/" + id, recipient.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal("Holiday schedule", Api.Str(announcement, "title"), "Announcement title");
            Check.True(Api.Str(announcement, "body").Contains("**change**", StringComparison.Ordinal), "Markdown body as stored");
            Check.True(announcement.GetProperty("sentUtc").ValueKind == JsonValueKind.String, "sentUtc");
            Check.True(announcement.GetProperty("labels").EnumerateArray().Any(l => l.GetString() == "news") && Api.Str(announcement.GetProperty("tags"), "season") == "winter", "Labels and tags");
            JsonElement mine = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/announcements/enumerate", recipient.AccessToken, new { }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.True(BroadcastTestKit.Ids(mine).SequenceEqual(new[] { id }), "Recipient lists it");
            JsonElement byLabel = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/announcements/enumerate", recipient.AccessToken, new { labels = new[] { "other" } }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal(0, byLabel.GetProperty("objects").GetArrayLength(), "Label filter");
            JsonElement get = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/announcements", recipient.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal(1, get.GetProperty("objects").GetArrayLength(), "GET form");
            await Api.ExpectErrorAsync(f, HttpMethod.Get, "/api/v1/announcements/" + id, outsider.AccessToken, null, HttpStatusCode.NotFound, "NotFound", ct).ConfigureAwait(false);
            JsonElement theirs = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/announcements/enumerate", outsider.AccessToken, new { }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal(0, theirs.GetProperty("objects").GetArrayLength(), "Outsider lists nothing");
            await Api.ExpectErrorAsync(f, HttpMethod.Get, "/api/v1/announcements/bct_nope", recipient.AccessToken, null, HttpStatusCode.NotFound, "NotFound", ct).ConfigureAwait(false);
            await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/announcements/" + id, null, null, HttpStatusCode.Unauthorized, ct).ConfigureAwait(false);
            JsonElement notifications = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/notifications/enumerate", recipient.AccessToken, new { }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            JsonElement notice = notifications.GetProperty("objects").EnumerateArray().First(n => Api.Str(n, "type") == "Broadcast");
            Check.Equal("Announcements", Api.Str(notice, "category"), "Announcements category");
            Check.Equal("/announcements/" + id, Api.Str(notice, "path"), "Path");
            Check.Equal("Holiday schedule", Api.Str(notice, "title"), "Notification title");
        }

        private static async Task RenderingAsync(HttpFixture f, CancellationToken ct)
        {
            EmailTemplate template = EmailTemplateCatalog.Get(BroadcastEmail.TemplateKey);
            Check.Equal(0, EmailRenderer.Validate(template, new EmailTemplateOverride { Subject = template.DefaultSubject, Preheader = template.DefaultPreheader, Heading = template.DefaultHeading, Body = template.DefaultBody }).Count, "Defaults use declared tokens");
            Dictionary<string, string> tokens = new Dictionary<string, string>(template.Samples, StringComparer.Ordinal)
            {
                ["body"] = "Hello **bold** and *soft* and ~~gone~~.\n\n- one\n- two\n\n1. first\n2. second\n\n> quoted\n\n## Heading\n\n<script>alert(1)</script> [bad](javascript:alert(1)) [good](https://example.test/ok) https://example.test/bare and \\*literal\\*"
            };
            EmailRendered rendered = EmailRenderer.Render(template, null, tokens, new EmailBrand { Name = "Example Community", PrimaryColor = "#2E7D32" });
            string html = rendered.Html;
            Check.True(html.Contains("<strong>bold</strong>", StringComparison.Ordinal) && html.Contains("<em>soft</em>", StringComparison.Ordinal) && html.Contains("<s>gone</s>", StringComparison.Ordinal), "Emphasis");
            Check.True(html.Contains("<ul", StringComparison.Ordinal) && html.Contains("<li>one</li>", StringComparison.Ordinal) && html.Contains("<ol", StringComparison.Ordinal), "Lists");
            Check.True(html.Contains("<blockquote", StringComparison.Ordinal) && html.Contains("<h2", StringComparison.Ordinal), "Quote and heading");
            Check.True(html.Contains("href=\"https://example.test/ok\"", StringComparison.Ordinal) && html.Contains("href=\"https://example.test/bare\"", StringComparison.Ordinal), "http links");
            Check.True(!html.Contains("<script>", StringComparison.Ordinal) && html.Contains("&lt;script&gt;", StringComparison.Ordinal), "Raw HTML is text");
            Check.True(!html.Contains("href=\"javascript", StringComparison.OrdinalIgnoreCase), "No unsafe link schemes");
            Check.True(html.Contains("*literal*", StringComparison.Ordinal), "Escapes render literally");
            Check.True(rendered.Text.Contains("**bold**", StringComparison.Ordinal) && !rendered.Text.Contains("<strong>", StringComparison.Ordinal), "Text part keeps the source");
            Check.True(html.Contains("Read more", StringComparison.Ordinal) && html.Contains("https://example.test/app/announcements/bct_sample", StringComparison.Ordinal), "Button");
            Check.Equal("Holiday schedule", rendered.Subject, "Subject token");
            JsonElement preview = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/admin/email/templates/broadcast/preview", HttpFixture.AdminToken, new { }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.True(Api.Str(preview, "html").Contains("<strong>next week</strong>", StringComparison.Ordinal), "Admin preview renders the sample Markdown");
            JsonElement listed = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/admin/email/templates/broadcast", HttpFixture.AdminToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal("Optional", Api.Str(listed, "category"), "Optional template");
            TestMember admin = await Api.AdminAsync(f, ct).ConfigureAwait(false);
            await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/admin/email/test", HttpFixture.AdminToken, new { template = "broadcast", to = admin.Email }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal(1, f.Fakes.Email.To(admin.Email, BroadcastEmail.TemplateKey).Count, "Test send like the other templates");
        }

        private static async Task KillSwitchAsync(HttpFixture f, CancellationToken ct)
        {
            TestMember admin = await Api.AdminAsync(f, ct).ConfigureAwait(false);
            await NotificationTestKit.MemberAsync(f, "Waiting Member", ct).ConfigureAwait(false);
            await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/admin/app/kill-switches/broadcasts", HttpFixture.AdminToken, new { killed = true, reason = "Incident 7" }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            string id = await BroadcastTestKit.CreateAsync(f, admin.AccessToken, BroadcastTestKit.Draft("Paused", BroadcastTestKit.Segment()), ct).ConfigureAwait(false);
            await BroadcastTestKit.SendAsync(f, admin.AccessToken, id, 2, ct).ConfigureAwait(false);
            Check.Equal(0, await f.Services.Broadcasts.Sender.RunPassAsync(ct).ConfigureAwait(false), "The worker pauses");
            Check.Equal("Sending", await BroadcastTestKit.StatusAsync(f, admin.AccessToken, id, ct).ConfigureAwait(false), "Still sending");
            Check.Equal(0L, await BroadcastTestKit.DeliveriesAsync(f, id, ct).ConfigureAwait(false), "Nothing delivered");
            await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/admin/app/kill-switches/broadcasts", HttpFixture.AdminToken, new { killed = false, reason = "Resolved" }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            await BroadcastTestKit.DrainAsync(f, ct).ConfigureAwait(false);
            Check.Equal("Sent", await BroadcastTestKit.StatusAsync(f, admin.AccessToken, id, ct).ConfigureAwait(false), "Resumed");
            Check.Equal(2L, await BroadcastTestKit.DeliveriesAsync(f, id, ct).ConfigureAwait(false), "Delivered");
        }

        private static async Task LifecycleAsync(HttpFixture f, CancellationToken ct)
        {
            TestMember admin = await Api.AdminAsync(f, ct).ConfigureAwait(false);
            TestMember leaving = await NotificationTestKit.MemberAsync(f, "Leaving Member", ct).ConfigureAwait(false);
            JsonElement doc = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/me/notification-preferences", leaving.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            JsonElement category = doc.GetProperty("objects").EnumerateArray().First(c => Api.Str(c, "category") == "Announcements");
            Check.True(category.GetProperty("inApp").GetBoolean() && category.GetProperty("push").GetBoolean() && category.GetProperty("email").GetBoolean(), "Announcements defaults: in-app, push, and email on");
            Check.True(!category.GetProperty("editable").GetProperty("inApp").GetBoolean() && category.GetProperty("editable").GetProperty("push").GetBoolean() && category.GetProperty("editable").GetProperty("email").GetBoolean(), "Push and email editable, in-app not");
            Check.Equal(NotificationCategoryEnum.Announcements, NotificationCatalog.CategoryOf(NotificationTypeEnum.Broadcast), "Broadcast notifications are Announcements");
            string id = await BroadcastTestKit.CreateAsync(f, admin.AccessToken, BroadcastTestKit.Draft("Goodbye", BroadcastTestKit.Segment()), ct).ConfigureAwait(false);
            await BroadcastTestKit.SendAsync(f, admin.AccessToken, id, 2, ct).ConfigureAwait(false);
            await BroadcastTestKit.DrainAsync(f, ct).ConfigureAwait(false);
            Check.Equal(1L, await BroadcastTestKit.DeliveriesAsync(f, id, ct, "userid = '" + leaving.Id + "'").ConfigureAwait(false), "Delivered before deletion");
            await Api.ExpectAsync(f, HttpMethod.Delete, "/api/v1/me", leaving.AccessToken, new { confirm = "DELETE" }, HttpStatusCode.Accepted, ct).ConfigureAwait(false);
            Check.Equal(0L, await BroadcastTestKit.DeliveriesAsync(f, id, ct, "userid = '" + leaving.Id + "'").ConfigureAwait(false), "Deliveries deleted with the account");
            Check.Equal(1L, await BroadcastTestKit.DeliveriesAsync(f, id, ct).ConfigureAwait(false), "Other deliveries stay");
        }

        #endregion
    }
}
