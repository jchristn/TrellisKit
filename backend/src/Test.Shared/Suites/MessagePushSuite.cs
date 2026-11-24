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
    using TrellisKit.Core.Integrations.Models;
    using TrellisKit.Server.Services.Messaging;

    /// <summary>
    /// Phase 5 direct message pushes (docs/PHASE5_CONTRACT.md section 4.9 and resolved decision 12): no push while the
    /// recipient has a foreground connection (fake presence probe), one push per conversation per coalescing window while
    /// unread, the three preview modes, the Messages push preference, notification hours and the member's exemption,
    /// muted conversations, pending requests, and no in-app DirectMessage rows.
    /// </summary>
    public static class MessagePushSuite
    {
        #region Public-Members

        /// <summary>
        /// Suite identifier.
        /// </summary>
        public const string Id = "MessagePush";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the suite.
        /// </summary>
        /// <returns>Suite.</returns>
        public static TestSuiteDescriptor Build()
        {
            return new TestSuiteDescriptor(Id, "Direct message pushes (" + TestEnvironment.Provider + ")", new[]
            {
                Case("Push.ForegroundCoalescingAndPayload", "A foreground connection suppresses pushes; otherwise one push per conversation per window while unread, with the DirectMessage data and the SenderOnly preview", async (f, ct) =>
                {
                    Noon(f);
                    TestMember a = await MessagingTestKit.MemberAsync(f, ct, "Alex Sender").ConfigureAwait(false);
                    TestMember b = await MessagingTestKit.MemberAsync(f, ct).ConfigureAwait(false);
                    string id = await MessagingTestKit.PairAsync(f, a, b, ct).ConfigureAwait(false);
                    string device = await MessagingTestKit.PushTokenAsync(f, b, ct).ConfigureAwait(false);
                    f.Fakes.Push.Clear();
                    bool foreground = true;
                    f.Services.Messaging.PresenceProbe = (userId, conversationId, token) => Task.FromResult(new RealtimePresence { HasForeground = foreground && userId == b.Id });
                    await MessagingTestKit.SendOkAsync(f, a, id, "while connected", ct).ConfigureAwait(false);
                    Check.Equal(0, f.Fakes.Push.To(device).Count, "Foreground: socket only");
                    foreground = false;
                    JsonElement sent = await MessagingTestKit.SendOkAsync(f, a, id, "secret text", ct).ConfigureAwait(false);
                    List<PushMessage> pushes = f.Fakes.Push.To(device);
                    Check.Equal(1, pushes.Count, "One push");
                    PushMessage push = pushes[0];
                    Check.Equal("DirectMessage", push.Data["type"], "type");
                    Check.Equal(id, push.Data["conversationId"], "conversationId");
                    Check.Equal(sent.GetProperty("seq").GetInt64().ToString(System.Globalization.CultureInfo.InvariantCulture), push.Data["seq"], "seq");
                    Check.Equal("/messages/" + id, push.Data["path"], "path");
                    Check.Equal(id, push.Data["collapseId"], "collapse key per conversation");
                    Check.Equal(id, push.Data["threadId"], "thread per conversation");
                    Check.Equal("Alex Sender sent you a message", push.Body, "SenderOnly preview");
                    Check.True(!(push.Title + push.Body).Contains("secret", StringComparison.Ordinal), "No text by default");
                    Check.True(push.Badge >= 1, "Badge counts unread conversations");
                    await MessagingTestKit.SendOkAsync(f, a, id, "again", ct).ConfigureAwait(false);
                    Check.Equal(1, f.Fakes.Push.To(device).Count, "Coalesced while unread");
                    await Api.ExpectAsync(f, HttpMethod.Post, MessagingTestKit.Conversations + "/" + id + "/read", b.AccessToken, new { seq = 1000 }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    await MessagingTestKit.SendOkAsync(f, a, id, "after reading", ct).ConfigureAwait(false);
                    Check.Equal(2, f.Fakes.Push.To(device).Count, "Reading ends the coalescing window");
                    Check.Equal(0L, await MessagingTestKit.CountAsync(f, "notifications", "type = 'DirectMessage'", ct).ConfigureAwait(false), "No in-app DirectMessage rows");
                }),
                Case("Push.PreviewModes", "Full shows a 100 character excerpt; Hidden shows only New message", async (f, ct) =>
                {
                    Noon(f);
                    f.Settings.Messaging.PushCoalesceSeconds = 0;
                    TestMember a = await MessagingTestKit.MemberAsync(f, ct, "Pat Preview").ConfigureAwait(false);
                    TestMember b = await MessagingTestKit.MemberAsync(f, ct).ConfigureAwait(false);
                    string id = await MessagingTestKit.PairAsync(f, a, b, ct).ConfigureAwait(false);
                    string device = await MessagingTestKit.PushTokenAsync(f, b, ct).ConfigureAwait(false);
                    await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/me/messaging/settings", b.AccessToken, new { pushPreview = "Full" }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    f.Fakes.Push.Clear();
                    await MessagingTestKit.SendOkAsync(f, a, id, "Full preview " + new string('x', 200), ct).ConfigureAwait(false);
                    PushMessage full = f.Fakes.Push.To(device).Last();
                    Check.Equal("Pat Preview", full.Title, "Full title");
                    Check.True(full.Body != null && full.Body.StartsWith("Full preview", StringComparison.Ordinal) && full.Body.Length <= 104, "Full excerpt (" + full.Body + ")");
                    await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/me/messaging/settings", b.AccessToken, new { pushPreview = "Hidden" }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    await MessagingTestKit.SendOkAsync(f, a, id, "hidden text", ct).ConfigureAwait(false);
                    PushMessage hidden = f.Fakes.Push.To(device).Last();
                    Check.Equal("New message", hidden.Title, "Hidden title");
                    Check.True(hidden.Body == null, "No body");
                    Check.Equal("New message", MessagePushService.Render(TrellisKit.Core.Enums.PushPreviewEnum.Hidden, "X", new TrellisKit.Core.Models.Message()).Title, "Renderer");
                }),
                Case("Push.PreferencesHoursMuteRequests", "No push when the Messages push preference is off, outside notification hours (unless exempt), in a muted conversation, or for a pending request", async (f, ct) =>
                {
                    Noon(f);
                    f.Settings.Messaging.PushCoalesceSeconds = 0;
                    TestMember a = await MessagingTestKit.MemberAsync(f, ct).ConfigureAwait(false);
                    TestMember b = await MessagingTestKit.MemberAsync(f, ct).ConfigureAwait(false);
                    string id = await MessagingTestKit.PairAsync(f, a, b, ct).ConfigureAwait(false);
                    string device = await MessagingTestKit.PushTokenAsync(f, b, ct).ConfigureAwait(false);
                    f.Fakes.Push.Clear();
                    await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/me/notification-preferences", b.AccessToken, new { categories = new object[] { new { category = "Messages", push = false } } }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    await MessagingTestKit.SendOkAsync(f, a, id, "pref off", ct).ConfigureAwait(false);
                    Check.Equal(0, f.Fakes.Push.To(device).Count, "Preference off");
                    await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/me/notification-preferences", b.AccessToken, new { categories = new object[] { new { category = "Messages", push = true } }, notificationHours = new { enabled = true, start = "01:00", end = "02:00", timeZone = "UTC" } }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    await MessagingTestKit.SendOkAsync(f, a, id, "outside hours", ct).ConfigureAwait(false);
                    Check.Equal(0, f.Fakes.Push.To(device).Count, "Outside notification hours");
                    JsonElement exempt = await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/me/messaging/settings", b.AccessToken, new { exemptFromNotificationHours = true }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.True(exempt.GetProperty("exemptFromNotificationHours").GetBoolean(), "Exemption saved");
                    await MessagingTestKit.SendOkAsync(f, a, id, "exempt", ct).ConfigureAwait(false);
                    Check.Equal(1, f.Fakes.Push.To(device).Count, "Exempt from notification hours");
                    await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/me/notification-preferences", b.AccessToken, new { pushPaused = true }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    await MessagingTestKit.SendOkAsync(f, a, id, "paused", ct).ConfigureAwait(false);
                    Check.Equal(1, f.Fakes.Push.To(device).Count, "Push paused");
                    await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/me/notification-preferences", b.AccessToken, new { pushPaused = false }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    await Api.ExpectAsync(f, HttpMethod.Put, MessagingTestKit.Conversations + "/" + id + "/mute", b.AccessToken, new { }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    await MessagingTestKit.SendOkAsync(f, a, id, "muted", ct).ConfigureAwait(false);
                    Check.Equal(1, f.Fakes.Push.To(device).Count, "Muted conversation");
                    TestMember stranger = await MessagingTestKit.MemberAsync(f, ct).ConfigureAwait(false);
                    await MessagingTestKit.StartIdAsync(f, stranger, b.Id, "a request", ct).ConfigureAwait(false);
                    Check.Equal(1, f.Fakes.Push.To(device).Count, "Requests never push");
                })
            });
        }

        #endregion

        #region Private-Methods

        private static void Noon(HttpFixture f)
        {
            DateTime noon = DateTime.UtcNow.Date.AddHours(12);
            f.Services.Messaging.Clock = () => noon.AddTicks(DateTime.UtcNow.Ticks % TimeSpan.TicksPerHour);
        }

        private static TestCaseDescriptor Case(string caseId, string name, Func<HttpFixture, CancellationToken, Task> body)
        {
            return SuiteCases.Http(Id, caseId, name, body);
        }

        #endregion
    }
}
