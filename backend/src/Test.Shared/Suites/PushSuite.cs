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
    using TrellisKit.Core.Integrations.Models;
    using TrellisKit.Core.Models;
    using TrellisKit.Server.Services;
    using TrellisKit.Server.Workers;

    /// <summary>
    /// Phase 2 push: token registration rules, session revocation deleting tokens, and the delivery worker (every
    /// suppression outcome, message shape and badge, receipts, pruning, disabling, and the Reaction hourly rule) with
    /// the in-memory sender and a fixed clock.
    /// </summary>
    public static class PushSuite
    {
        #region Public-Members

        /// <summary>
        /// Suite identifier.
        /// </summary>
        public const string Id = "Push";

        #endregion

        #region Private-Members

        private const string _Tokens = "/api/v1/me/push-tokens";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the suite.
        /// </summary>
        /// <returns>Suite.</returns>
        public static TestSuiteDescriptor Build()
        {
            return new TestSuiteDescriptor(Id, "Push (" + TestEnvironment.Provider + ")", new[]
            {
                Case("Tokens.Registration", "Registration validates, returns 201 then 200, moves tokens between accounts, keeps 10, and never returns values", async (f, ct) =>
                {
                    TestMember a = await Api.RegisterAsync(f, ct).ConfigureAwait(false);
                    TestMember b = await Api.RegisterAsync(f, ct).ConfigureAwait(false);
                    ApiResult bad = await Api.SendAsync(f, HttpMethod.Post, _Tokens, a.AccessToken, new { token = "not-a-token", platform = "Ios" }, ct).ConfigureAwait(false);
                    Check.Equal(HttpStatusCode.BadRequest, bad.Status, "Bad token");
                    Check.True(bad.Body.GetProperty("fieldErrors").TryGetProperty("token", out JsonElement _), "fieldErrors.token");
                    ApiResult badPlatform = await Api.SendAsync(f, HttpMethod.Post, _Tokens, a.AccessToken, new { token = NotificationTestKit.NewToken(), platform = "Windows" }, ct).ConfigureAwait(false);
                    Check.True(badPlatform.Body.GetProperty("fieldErrors").TryGetProperty("platform", out JsonElement _), "fieldErrors.platform");
                    string value = "ExpoPushToken[abc_DEF-123]";
                    JsonElement created = await Api.ExpectAsync(f, HttpMethod.Post, _Tokens, a.AccessToken, new { token = value, platform = "Android", deviceName = "Pixel", appVersion = "2.0" }, HttpStatusCode.Created, ct).ConfigureAwait(false);
                    Check.Equal("Android", Api.Str(created, "platform"), "Platform");
                    Check.Equal("Pixel", Api.Str(created, "deviceName"), "Device name");
                    Check.True(!created.TryGetProperty("token", out JsonElement _), "No token value");
                    Check.True(created.GetProperty("labels").ValueKind == JsonValueKind.Array && created.GetProperty("tags").ValueKind == JsonValueKind.Object, "Labels and tags");
                    await Task.Delay(5, ct).ConfigureAwait(false);
                    JsonElement again = await Api.ExpectAsync(f, HttpMethod.Post, _Tokens, a.AccessToken, new { token = value, platform = "Android" }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.Equal(Api.Str(created, "id"), Api.Str(again, "id"), "Same row");
                    Check.True(again.GetProperty("lastSeenUtc").GetDateTime() > created.GetProperty("lastSeenUtc").GetDateTime(), "lastSeenUtc moved");
                    await Api.ExpectAsync(f, HttpMethod.Post, _Tokens, b.AccessToken, new { token = value, platform = "Android" }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    JsonElement listA = await Api.ExpectAsync(f, HttpMethod.Get, _Tokens, a.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.Equal(0, listA.GetProperty("objects").GetArrayLength(), "Moved away from A");
                    ApiResult listB = await Api.SendAsync(f, HttpMethod.Get, _Tokens, b.AccessToken, null, ct).ConfigureAwait(false);
                    Check.Equal(1, listB.Body.GetProperty("objects").GetArrayLength(), "Moved to B");
                    Check.True(!listB.Text.Contains("PushToken[", StringComparison.Ordinal), "GET never returns values");
                    await Api.SendAsync(f, HttpMethod.Post, "/api/v1/auth/logout", null, new { refreshToken = b.RefreshToken }, ct).ConfigureAwait(false);
                    Check.Equal(0L, await NotificationTestKit.CountAsync(f, "SELECT COUNT(*) AS cnt FROM pushtokens WHERE token = '" + value + "';", ct).ConfigureAwait(false), "Moved to B's session: B's sign-out deletes it");

                    List<string> values = new List<string>();
                    for (int i = 0; i < 11; i++)
                    {
                        values.Add(await NotificationTestKit.RegisterTokenAsync(f, a, ct).ConfigureAwait(false));
                        await Task.Delay(3, ct).ConfigureAwait(false);
                    }
                    JsonElement ten = await Api.ExpectAsync(f, HttpMethod.Post, _Tokens + "/enumerate", a.AccessToken, new { maxResults = 50 }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.Equal(10, ten.GetProperty("objects").GetArrayLength(), "Ten kept");
                    Check.Equal(0L, await NotificationTestKit.CountAsync(f, "SELECT COUNT(*) AS cnt FROM pushtokens WHERE token = '" + values[0] + "';", ct).ConfigureAwait(false), "Least recently seen deleted");
                    await Api.ExpectAsync(f, HttpMethod.Delete, _Tokens, a.AccessToken, new { token = "ExponentPushToken[unknown]" }, HttpStatusCode.NoContent, ct).ConfigureAwait(false);
                    await Api.ExpectAsync(f, HttpMethod.Delete, _Tokens, a.AccessToken, new { token = values[5] }, HttpStatusCode.NoContent, ct).ConfigureAwait(false);
                    JsonElement nine = await Api.ExpectAsync(f, HttpMethod.Get, _Tokens, a.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.Equal(9, nine.GetProperty("objects").GetArrayLength(), "Deleted one");
                    f.Settings.Push.RegistrationsPerHour = 1;
                    TestMember c = await Api.RegisterAsync(f, ct).ConfigureAwait(false);
                    await NotificationTestKit.RegisterTokenAsync(f, c, ct).ConfigureAwait(false);
                    await Api.ExpectErrorAsync(f, HttpMethod.Post, _Tokens, c.AccessToken, new { token = NotificationTestKit.NewToken(), platform = "Ios" }, (HttpStatusCode)429, "RateLimited", ct).ConfigureAwait(false);
                }),
                Case("Regression.Push.ReRegisterKeepsDevice", "Re-registering a token without deviceName and appVersion keeps the stored values; sending them updates them", async (f, ct) =>
                {
                    TestMember a = await Api.RegisterAsync(f, ct).ConfigureAwait(false);
                    string value = NotificationTestKit.NewToken();
                    await Api.ExpectAsync(f, HttpMethod.Post, _Tokens, a.AccessToken, new { token = value, platform = "Ios", deviceName = "Phone One", appVersion = "1.2.3" }, HttpStatusCode.Created, ct).ConfigureAwait(false);
                    JsonElement again = await Api.ExpectAsync(f, HttpMethod.Post, _Tokens, a.AccessToken, new { token = value, platform = "Ios" }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.Equal("Phone One", Api.Str(again, "deviceName"), "Device name kept");
                    Check.Equal("1.2.3", Api.Str(again, "appVersion"), "App version kept");
                    JsonElement updated = await Api.ExpectAsync(f, HttpMethod.Post, _Tokens, a.AccessToken, new { token = value, platform = "Ios", appVersion = "1.3.0" }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.Equal("1.3.0", Api.Str(updated, "appVersion"), "App version updated");
                    Check.Equal("Phone One", Api.Str(updated, "deviceName"), "Device name still kept");
                }),
                Case("Tokens.SessionRevoke", "Sign-out, password change, and admin session revoke delete the tokens of the revoked sessions", async (f, ct) =>
                {
                    TestMember first = await Api.RegisterAsync(f, ct).ConfigureAwait(false);
                    TestMember second = await Api.LoginAsync(f, new TestMember { Id = first.Id, Email = first.Email, Password = first.Password }, ct).ConfigureAwait(false);
                    string t1 = await NotificationTestKit.RegisterTokenAsync(f, first, ct).ConfigureAwait(false);
                    string t2 = await NotificationTestKit.RegisterTokenAsync(f, second, ct).ConfigureAwait(false);
                    await Api.SendAsync(f, HttpMethod.Post, "/api/v1/auth/logout", null, new { refreshToken = second.RefreshToken }, ct).ConfigureAwait(false);
                    Check.Equal(0L, await Count(f, t2, ct).ConfigureAwait(false), "Sign-out deletes its token");
                    Check.Equal(1L, await Count(f, t1, ct).ConfigureAwait(false), "Other session keeps its token");

                    TestMember third = await Api.LoginAsync(f, new TestMember { Id = first.Id, Email = first.Email, Password = first.Password }, ct).ConfigureAwait(false);
                    string t3 = await NotificationTestKit.RegisterTokenAsync(f, third, ct).ConfigureAwait(false);
                    await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/me/password", first.AccessToken, new { currentPassword = first.Password, newPassword = "another long passphrase" }, HttpStatusCode.NoContent, ct).ConfigureAwait(false);
                    Check.Equal(0L, await Count(f, t3, ct).ConfigureAwait(false), "Password change deletes other sessions' tokens");
                    Check.Equal(1L, await Count(f, t1, ct).ConfigureAwait(false), "Current session keeps its token");

                    await Api.ExpectAsync(f, HttpMethod.Delete, "/api/v1/admin/members/" + first.Id + "/sessions", HttpFixture.AdminToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.Equal(0L, await Count(f, t1, ct).ConfigureAwait(false), "Admin revoke deletes every token");
                }),
                Case("Worker.Suppression", "The worker records each suppression outcome and sends nothing for it", async (f, ct) =>
                {
                    NotificationTestKit.AllDay(f);
                    DateTime now = DateTime.UtcNow;
                    PushDeliveryWorker worker = NotificationTestKit.PushWorker(f, () => now);
                    TestMember actor = await NotificationTestKit.MemberAsync(f, "Actor", ct).ConfigureAwait(false);
                    async Task<Notification> Make(TestMember recipient, NotificationTypeEnum type = NotificationTypeEnum.CommentReply)
                    {
                        return (await f.Services.Notifications.CreateAsync(NotificationTestKit.Draft(type, recipient.Id, actor.Id), ct).ConfigureAwait(false))!;
                    }
                    async Task<PushStateEnum> State(Notification n)
                    {
                        return (await f.Database.Notifications.ReadAsync(n.Id, ct).ConfigureAwait(false))!.PushState;
                    }

                    TestMember disabled = await NotificationTestKit.MemberAsync(f, "Disabled", ct).ConfigureAwait(false);
                    string disabledToken = await NotificationTestKit.RegisterTokenAsync(f, disabled, ct).ConfigureAwait(false);
                    Notification n1 = await Make(disabled).ConfigureAwait(false);
                    f.Settings.Push.Enabled = false;
                    await worker.RunOnceAsync(ct).ConfigureAwait(false);
                    f.Settings.Push.Enabled = true;
                    Check.Equal(PushStateEnum.Suppressed, await State(n1).ConfigureAwait(false), "push.enabled false");

                    TestMember pref = await NotificationTestKit.MemberAsync(f, "Pref", ct).ConfigureAwait(false);
                    string prefToken = await NotificationTestKit.RegisterTokenAsync(f, pref, ct).ConfigureAwait(false);
                    await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/me/notification-preferences", pref.AccessToken, new { categories = new object[] { new { category = "Replies", push = false } } }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Notification n2 = await Make(pref).ConfigureAwait(false);

                    TestMember paused = await NotificationTestKit.MemberAsync(f, "Paused", ct).ConfigureAwait(false);
                    string pausedToken = await NotificationTestKit.RegisterTokenAsync(f, paused, ct).ConfigureAwait(false);
                    await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/me/notification-preferences", paused.AccessToken, new { pushPaused = true }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Notification n3 = await Make(paused).ConfigureAwait(false);

                    TestMember night = await NotificationTestKit.MemberAsync(f, "Night", ct).ConfigureAwait(false);
                    string nightToken = await NotificationTestKit.RegisterTokenAsync(f, night, ct).ConfigureAwait(false);
                    string start = now.AddHours(2).ToString("HH:00");
                    string end = now.AddHours(4).ToString("HH:00");
                    await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/me/notification-preferences", night.AccessToken, new { notificationHours = new { enabled = true, start, end, timeZone = "UTC" } }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Notification n4 = await Make(night).ConfigureAwait(false);

                    TestMember noToken = await NotificationTestKit.MemberAsync(f, "No token", ct).ConfigureAwait(false);
                    Notification n5 = await Make(noToken).ConfigureAwait(false);

                    TestMember expired = await NotificationTestKit.MemberAsync(f, "Expired session", ct).ConfigureAwait(false);
                    string expiredToken = await NotificationTestKit.RegisterTokenAsync(f, expired, ct).ConfigureAwait(false);
                    await f.Raw.ExecuteQueryAsync("UPDATE authsessions SET expiresutc = " + NotificationTestKit.Ts(now.AddDays(-1)) + " WHERE userid = '" + expired.Id + "';", false, ct).ConfigureAwait(false);
                    Notification n6 = await Make(expired).ConfigureAwait(false);

                    f.Settings.Push.MaxPerUserPerHour = 2;
                    TestMember busy = await NotificationTestKit.MemberAsync(f, "Busy", ct).ConfigureAwait(false);
                    string busyToken = await NotificationTestKit.RegisterTokenAsync(f, busy, ct).ConfigureAwait(false);
                    List<Notification> busyOnes = new List<Notification>();
                    for (int i = 0; i < 3; i++) busyOnes.Add(await Make(busy).ConfigureAwait(false));

                    await worker.RunOnceAsync(ct).ConfigureAwait(false);
                    Check.Equal(PushStateEnum.Suppressed, await State(n2).ConfigureAwait(false), "Category push off");
                    Check.Equal(PushStateEnum.Suppressed, await State(n3).ConfigureAwait(false), "Paused");
                    Check.Equal(PushStateEnum.Suppressed, await State(n4).ConfigureAwait(false), "Outside hours");
                    Check.Equal(PushStateEnum.Suppressed, await State(n5).ConfigureAwait(false), "No token");
                    Check.Equal(PushStateEnum.Suppressed, await State(n6).ConfigureAwait(false), "Expired session token does not count");
                    Check.Equal(PushStateEnum.Sent, await State(busyOnes[0]).ConfigureAwait(false), "First within the rate");
                    Check.Equal(PushStateEnum.Sent, await State(busyOnes[1]).ConfigureAwait(false), "Second within the rate");
                    Check.Equal(PushStateEnum.Suppressed, await State(busyOnes[2]).ConfigureAwait(false), "Third over push.maxPerUserPerHour");
                    foreach (string t in new[] { disabledToken, prefToken, pausedToken, nightToken, expiredToken }) Check.Equal(0, f.Fakes.Push.To(t).Count, "Nothing sent to " + t);
                    Check.Equal(2, f.Fakes.Push.To(busyToken).Count, "Two sent");
                    Check.Equal(PushStateEnum.Suppressed, await State(n1).ConfigureAwait(false), "Suppressed stays suppressed");
                    await worker.DisposeAsync().ConfigureAwait(false);
                }),
                Case("Worker.MessageShape", "A push carries the title, body, data, sound, badge (unread count), channel, and priority", async (f, ct) =>
                {
                    NotificationTestKit.AllDay(f);
                    DateTime now = DateTime.UtcNow;
                    PushDeliveryWorker worker = NotificationTestKit.PushWorker(f, () => now);
                    TestMember actor = await NotificationTestKit.MemberAsync(f, "Alex Chen", ct).ConfigureAwait(false);
                    TestMember member = await NotificationTestKit.MemberAsync(f, "Member", ct).ConfigureAwait(false);
                    string t1 = await NotificationTestKit.RegisterTokenAsync(f, member, ct).ConfigureAwait(false);
                    string t2 = await NotificationTestKit.RegisterTokenAsync(f, member, ct, "Android").ConfigureAwait(false);
                    NotificationDraft older = NotificationTestKit.Draft(NotificationTypeEnum.Mention, member.Id, actor.Id);
                    older.QueuePush = false;
                    await f.Services.Notifications.CreateAsync(older, ct).ConfigureAwait(false);
                    NotificationDraft draft = NotificationTestKit.Draft(NotificationTypeEnum.CommentReply, member.Id, actor.Id);
                    draft.CommentId = "cmt_x";
                    draft.Body = "Does this work for beginners too?";
                    Notification n = (await f.Services.Notifications.CreateAsync(draft, ct).ConfigureAwait(false))!;
                    await worker.RunOnceAsync(ct).ConfigureAwait(false);
                    PushMessage m = f.Fakes.Push.To(t1).Single();
                    Check.Equal("Alex Chen replied to your comment", m.Title, "Title");
                    Check.Equal("Does this work for beginners too?", m.Body, "Body");
                    Check.Equal(n.Id, m.Data["notificationId"], "Data id");
                    Check.Equal("CommentReply", m.Data["type"], "Data type");
                    Check.Equal("/feed/fpo_parent?comment=cmt_x", m.Data["path"], "Data path");
                    Check.Equal("default", m.Sound, "Sound");
                    Check.Equal(2, m.Badge ?? -1, "Badge is the unread count");
                    Check.Equal("default", m.ChannelId, "Channel");
                    Check.Equal("high", m.Priority, "Priority");
                    Check.Equal(1, f.Fakes.Push.To(t2).Count, "Every active token");
                    Check.Equal(2, (await f.Database.PushTokens.EnumerateDeliveriesAsync(n.Id, ct).ConfigureAwait(false)).Count(d => d.Status == PushDeliveryStatusEnum.Sent && d.TicketId != null), "Tickets stored");
                    await worker.RunOnceAsync(ct).ConfigureAwait(false);
                    Check.Equal(1, f.Fakes.Push.To(t1).Count, "Sent once");
                    await worker.DisposeAsync().ConfigureAwait(false);
                }),
                Case("Worker.Receipts", "Receipts after the delay: ok, DeviceNotRegistered prunes, other errors disable after 5, ticket errors count", async (f, ct) =>
                {
                    NotificationTestKit.AllDay(f);
                    DateTime now = DateTime.UtcNow;
                    PushDeliveryWorker worker = NotificationTestKit.PushWorker(f, () => now);
                    TestMember actor = await NotificationTestKit.MemberAsync(f, "Actor", ct).ConfigureAwait(false);
                    TestMember member = await NotificationTestKit.MemberAsync(f, "Member", ct).ConfigureAwait(false);
                    string good = await NotificationTestKit.RegisterTokenAsync(f, member, ct).ConfigureAwait(false);
                    string gone = await NotificationTestKit.RegisterTokenAsync(f, member, ct).ConfigureAwait(false);
                    string flaky = await NotificationTestKit.RegisterTokenAsync(f, member, ct).ConfigureAwait(false);
                    string dead = await NotificationTestKit.RegisterTokenAsync(f, member, ct).ConfigureAwait(false);
                    f.Fakes.Push.ScriptReceiptError(gone, "DeviceNotRegistered");
                    f.Fakes.Push.ScriptReceiptError(flaky, "MessageRateExceeded");
                    f.Fakes.Push.ScriptTicketError(dead, "DeviceNotRegistered");
                    f.Settings.Push.MaxPerUserPerHour = 100;
                    Notification n = (await f.Services.Notifications.CreateAsync(NotificationTestKit.Draft(NotificationTypeEnum.CommentReply, member.Id, actor.Id), ct).ConfigureAwait(false))!;
                    await worker.RunOnceAsync(ct).ConfigureAwait(false);
                    Check.Equal(0L, await Count(f, dead, ct).ConfigureAwait(false), "Ticket DeviceNotRegistered prunes at once");
                    Check.Equal(0, f.Fakes.Push.ReceiptRequests, "Receipts wait for push.receiptDelayMinutes");
                    now = now.AddMinutes(f.Settings.Push.ReceiptDelayMinutes + 1);
                    await worker.RunOnceAsync(ct).ConfigureAwait(false);
                    List<PushDelivery> deliveries = await f.Database.PushTokens.EnumerateDeliveriesAsync(n.Id, ct).ConfigureAwait(false);
                    Check.True(deliveries.Any(d => d.Status == PushDeliveryStatusEnum.Ok && d.ReceiptCheckedUtc != null), "Ok receipt recorded");
                    Check.Equal(0L, await Count(f, gone, ct).ConfigureAwait(false), "Receipt DeviceNotRegistered prunes");
                    Check.Equal(1L, await Count(f, good, ct).ConfigureAwait(false), "Good token kept");
                    PushToken flakyRow = (await f.Database.PushTokens.EnumerateByUserAsync(member.Id, ct).ConfigureAwait(false)).Single(t => t.Token == flaky);
                    Check.Equal(1, flakyRow.FailureCount, "Failure counted");
                    for (int i = 0; i < 4; i++)
                    {
                        await f.Services.Notifications.CreateAsync(NotificationTestKit.Draft(NotificationTypeEnum.CommentReply, member.Id, actor.Id), ct).ConfigureAwait(false);
                        await worker.RunOnceAsync(ct).ConfigureAwait(false);
                        now = now.AddMinutes(f.Settings.Push.ReceiptDelayMinutes + 1);
                        await worker.RunOnceAsync(ct).ConfigureAwait(false);
                    }
                    flakyRow = (await f.Database.PushTokens.EnumerateByUserAsync(member.Id, ct).ConfigureAwait(false)).Single(t => t.Token == flaky);
                    Check.Equal(5, flakyRow.FailureCount, "Five consecutive failures");
                    Check.True(flakyRow.DisabledUtc != null, "Disabled after 5");
                    int before = f.Fakes.Push.To(flaky).Count;
                    await f.Services.Notifications.CreateAsync(NotificationTestKit.Draft(NotificationTypeEnum.CommentReply, member.Id, actor.Id), ct).ConfigureAwait(false);
                    await worker.RunOnceAsync(ct).ConfigureAwait(false);
                    Check.Equal(before, f.Fakes.Push.To(flaky).Count, "Disabled token receives nothing");
                    Check.Equal(0, (await f.Database.PushTokens.EnumerateByUserAsync(member.Id, ct).ConfigureAwait(false)).Single(t => t.Token == good).FailureCount, "Ok resets the count");
                    f.Fakes.Push.FailSends = true;
                    Notification failed = (await f.Services.Notifications.CreateAsync(NotificationTestKit.Draft(NotificationTypeEnum.CommentReply, member.Id, actor.Id), ct).ConfigureAwait(false))!;
                    await worker.RunOnceAsync(ct).ConfigureAwait(false);
                    Check.Equal(PushStateEnum.Failed, (await f.Database.Notifications.ReadAsync(failed.Id, ct).ConfigureAwait(false))!.PushState, "Send failure marks Failed");
                    await worker.DisposeAsync().ConfigureAwait(false);
                }),
                Case("Worker.ReactionHourly", "Reaction pushes at most once per target per hour; aggregated updates do not push again within the hour", async (f, ct) =>
                {
                    NotificationTestKit.AllDay(f);
                    DateTime now = DateTime.UtcNow;
                    PushDeliveryWorker worker = NotificationTestKit.PushWorker(f, () => now);
                    TestMember owner = await NotificationTestKit.MemberAsync(f, "Owner", ct).ConfigureAwait(false);
                    string token = await NotificationTestKit.RegisterTokenAsync(f, owner, ct).ConfigureAwait(false);
                    List<TestMember> reactors = new List<TestMember>();
                    for (int i = 0; i < 4; i++) reactors.Add(await NotificationTestKit.MemberAsync(f, "Reactor " + i, ct).ConfigureAwait(false));
                    string target = "cmt_hourly";
                    Notification first = (await f.Services.Notifications.CreateAsync(NotificationTestKit.Draft(NotificationTypeEnum.Reaction, owner.Id, reactors[0].Id, TargetTypeEnum.Comment, target), ct).ConfigureAwait(false))!;
                    await worker.RunOnceAsync(ct).ConfigureAwait(false);
                    Check.Equal(1, f.Fakes.Push.To(token).Count, "First reaction pushes");
                    await f.Services.Notifications.CreateAsync(NotificationTestKit.Draft(NotificationTypeEnum.Reaction, owner.Id, reactors[1].Id, TargetTypeEnum.Comment, target), ct).ConfigureAwait(false);
                    await worker.RunOnceAsync(ct).ConfigureAwait(false);
                    Check.Equal(1, f.Fakes.Push.To(token).Count, "Aggregated update does not push again");
                    await f.Services.Notifications.MarkReadAsync(owner.Id, new List<string> { first.Id }, ct).ConfigureAwait(false);
                    Notification second = (await f.Services.Notifications.CreateAsync(NotificationTestKit.Draft(NotificationTypeEnum.Reaction, owner.Id, reactors[2].Id, TargetTypeEnum.Comment, target), ct).ConfigureAwait(false))!;
                    Check.True(second.Id != first.Id, "New notification after read");
                    now = now.AddMinutes(10);
                    await worker.RunOnceAsync(ct).ConfigureAwait(false);
                    Check.Equal(1, f.Fakes.Push.To(token).Count, "Same target within the hour is suppressed");
                    Check.Equal(PushStateEnum.Suppressed, (await f.Database.Notifications.ReadAsync(second.Id, ct).ConfigureAwait(false))!.PushState, "suppressed_rate");
                    await f.Services.Notifications.CreateAsync(NotificationTestKit.Draft(NotificationTypeEnum.Reaction, owner.Id, reactors[3].Id, TargetTypeEnum.Comment, "cmt_other"), ct).ConfigureAwait(false);
                    await worker.RunOnceAsync(ct).ConfigureAwait(false);
                    Check.Equal(2, f.Fakes.Push.To(token).Count, "Another target pushes");
                    await worker.DisposeAsync().ConfigureAwait(false);
                })
            });
        }

        #endregion

        #region Private-Methods

        private static Task<long> Count(HttpFixture f, string token, CancellationToken ct)
        {
            return NotificationTestKit.CountAsync(f, "SELECT COUNT(*) AS cnt FROM pushtokens WHERE token = '" + token + "';", ct);
        }

        private static TestCaseDescriptor Case(string caseId, string name, Func<HttpFixture, CancellationToken, Task> body)
        {
            return SuiteCases.Http(Id, caseId, name, body);
        }

        #endregion
    }
}
