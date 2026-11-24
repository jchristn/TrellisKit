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
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Helpers;
    using TrellisKit.Core.Models;
    using TrellisKit.Core.PropertyConfig;
    using TrellisKit.Core.Settings;
    using TrellisKit.Server.Services;
    using TrellisKit.Server.Services.Realtime;

    /// <summary>
    /// The realtime WebSocket protocol (docs/PHASE5_CONTRACT.md section 4 and as-built note A) over real sockets against
    /// the in-test server: pre-upgrade checks, hello and welcome, close codes, frame limits, heartbeats, token expiry
    /// and refresh, revocation, kill switches, connection caps, rate limits, backpressure, live notifications,
    /// capability gating, the inbound handler seam, and draining.
    /// </summary>
    public static class RealtimeProtocolSuite
    {
        #region Public-Members

        /// <summary>
        /// Suite identifier.
        /// </summary>
        public const string Id = "RealtimeProtocol";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the suite.
        /// </summary>
        /// <returns>Suite.</returns>
        public static TestSuiteDescriptor Build()
        {
            return new TestSuiteDescriptor(Id, "Realtime protocol (" + TestEnvironment.Provider + ")", new[]
            {
                SuiteCases.Http(Id, "Hello.Welcome", "hello returns welcome with connection id, capabilities, heartbeat, and resume; ping gets pong; the registry row lives while connected", async (f, ct) =>
                {
                    TestMember member = await Api.MemberAsync(f, ct).ConfigureAwait(false);
                    RealtimeTestClient client = await RealtimeTestKit.OpenAsync(f, member.AccessToken, ct, new Dictionary<string, object?> { ["propertyId"] = f.Services.PropertyId, ["resume"] = new { syncToken = "opaque" } }).ConfigureAwait(false);
                    await using (client.ConfigureAwait(false))
                    {
                        JsonElement welcome = client.Welcome;
                        Check.True(client.ConnectionId.StartsWith("rtc_", StringComparison.Ordinal), "Connection id prefix");
                        Check.Equal(member.Id, welcome.GetProperty("userId").GetString(), "User id");
                        Check.Equal(25, welcome.GetProperty("heartbeatSeconds").GetInt32(), "Heartbeat");
                        Check.Equal(16384, welcome.GetProperty("maxInboundFrameBytes").GetInt32(), "Frame cap");
                        List<string> capabilities = RealtimeTestKit.Capabilities(welcome);
                        Check.True(capabilities.Contains("messaging") && capabilities.Contains("notifications") && capabilities.Contains("tickets"), "Member capabilities: " + String.Join(",", capabilities));
                        Check.True(welcome.GetProperty("resume").GetProperty("syncRequired").GetBoolean(), "Sync always required");
                        Check.True(welcome.TryGetProperty("serverTime", out JsonElement _), "Server time");
                        await RealtimeTestKit.PingAsync(client, ct).ConfigureAwait(false);
                        List<RealtimeConnectionRecord> rows = await f.Services.Realtime.Registry.RecentAsync(member.Id, ct).ConfigureAwait(false);
                        Check.Equal(1, rows.Count, "Registry row");
                        Check.Equal(client.ConnectionId, rows[0].Id, "Row id");
                        Check.Equal(f.Server.Realtime.TaskId, rows[0].TaskId, "Task id");
                        Check.Equal("Foreground", rows[0].AppState, "App state");
                        Check.Equal("Mobile", rows[0].Client, "Client");
                    }
                    Check.True(await RealtimeTestKit.EventuallyAsync(async () => (await f.Services.Realtime.Registry.RecentAsync(member.Id, ct).ConfigureAwait(false)).Count == 0, RealtimeTestKit.Wait, ct).ConfigureAwait(false), "Row deleted on disconnect");
                }),
                SuiteCases.Http(Id, "Hello.Timeout", "A socket without hello closes 4408 after realtime.authTimeoutSeconds", s => s.Realtime.AuthTimeoutSeconds = 3, async (f, ct) =>
                {
                    RealtimeTestClient client = await RealtimeTestKit.ConnectAsync(f, ct).ConfigureAwait(false);
                    await using (client.ConfigureAwait(false))
                    {
                        Check.Equal<int?>(4408, await client.WaitCloseAsync(TimeSpan.FromSeconds(8), ct).ConfigureAwait(false), "Hello timeout");
                    }
                }),
                SuiteCases.Http(Id, "Hello.BadToken", "A bad access token gets error Unauthorized and close 4401", async (f, ct) =>
                {
                    RealtimeTestClient client = await RealtimeTestKit.ConnectAsync(f, ct).ConfigureAwait(false);
                    await using (client.ConfigureAwait(false))
                    {
                        await client.SendAsync(new { type = "hello", id = "c1", data = new { accessToken = "not-a-token" } }, ct).ConfigureAwait(false);
                        JsonElement error = await client.ExpectDataAsync("error", RealtimeTestKit.Wait, ct).ConfigureAwait(false);
                        Check.Equal("Unauthorized", error.GetProperty("code").GetString(), "Error code");
                        Check.Equal("c1", error.GetProperty("frameId").GetString(), "Frame id echoed");
                        Check.Equal<int?>(4401, await client.WaitCloseAsync(RealtimeTestKit.Wait, ct).ConfigureAwait(false), "Close code");
                    }
                }),
                SuiteCases.Http(Id, "Hello.OtherPropertyToken", "An access token bound to another property closes 4401", async (f, ct) =>
                {
                    TestMember member = await Api.MemberAsync(f, ct).ConfigureAwait(false);
                    SessionPrincipal principal = (await f.Services.Tokens.ValidateAccessTokenAsync(member.AccessToken, ct).ConfigureAwait(false))!;
                    AuthSession session = (await f.Database.Sessions.ReadAsync(principal.SessionId, ct).ConfigureAwait(false))!;
                    TokenService other = new TokenService(f.Database, f.Settings.Auth, f.Fakes.Set.TokenProtector, f.Services.Logger) { PropertyId = PropertyIds.Generate() };
                    string foreign = other.IssueAccessToken(session, DateTime.UtcNow).Token;
                    RealtimeTestClient client = await RealtimeTestKit.ConnectAsync(f, ct).ConfigureAwait(false);
                    await using (client.ConfigureAwait(false))
                    {
                        Check.Equal<int?>(4401, await RealtimeTestKit.HelloRefusedAsync(client, foreign, ct).ConfigureAwait(false), "Foreign token");
                    }
                }),
                SuiteCases.Http(Id, "Hello.PropertyMismatch", "hello.propertyId naming another property closes 4404", async (f, ct) =>
                {
                    TestMember member = await Api.MemberAsync(f, ct).ConfigureAwait(false);
                    RealtimeTestClient client = await RealtimeTestKit.ConnectAsync(f, ct).ConfigureAwait(false);
                    await using (client.ConfigureAwait(false))
                    {
                        Check.Equal<int?>(4404, await RealtimeTestKit.HelloRefusedAsync(client, member.AccessToken, ct, new Dictionary<string, object?> { ["propertyId"] = PropertyIds.Generate() }).ConfigureAwait(false), "Mismatch");
                    }
                }),
                SuiteCases.Http(Id, "Hello.Protocol", "hello.protocol other than tk.v1 closes 4400", async (f, ct) =>
                {
                    TestMember member = await Api.MemberAsync(f, ct).ConfigureAwait(false);
                    RealtimeTestClient client = await RealtimeTestKit.ConnectAsync(f, ct).ConfigureAwait(false);
                    await using (client.ConfigureAwait(false))
                    {
                        Check.Equal<int?>(4400, await RealtimeTestKit.HelloRefusedAsync(client, member.AccessToken, ct, new Dictionary<string, object?> { ["protocol"] = "tk.v2" }).ConfigureAwait(false), "Unknown protocol");
                    }
                }),
                SuiteCases.Http(Id, "Frames.BeforeHello", "A frame before hello closes 4400", async (f, ct) =>
                {
                    RealtimeTestClient client = await RealtimeTestKit.ConnectAsync(f, ct).ConfigureAwait(false);
                    await using (client.ConfigureAwait(false))
                    {
                        await client.SendAsync(new { type = "ping", data = new { } }, ct).ConfigureAwait(false);
                        Check.Equal<int?>(4400, await client.WaitCloseAsync(RealtimeTestKit.Wait, ct).ConfigureAwait(false), "Frame before hello");
                    }
                }),
                SuiteCases.Http(Id, "Frames.Malformed", "Malformed JSON after hello closes 4400", async (f, ct) =>
                {
                    TestMember member = await Api.MemberAsync(f, ct).ConfigureAwait(false);
                    RealtimeTestClient client = await RealtimeTestKit.OpenAsync(f, member.AccessToken, ct).ConfigureAwait(false);
                    await using (client.ConfigureAwait(false))
                    {
                        await client.SendTextAsync("{not json", ct).ConfigureAwait(false);
                        Check.Equal<int?>(4400, await client.WaitCloseAsync(RealtimeTestKit.Wait, ct).ConfigureAwait(false), "Malformed JSON");
                    }
                }),
                SuiteCases.Http(Id, "Frames.Binary", "A binary frame closes 1003", async (f, ct) =>
                {
                    TestMember member = await Api.MemberAsync(f, ct).ConfigureAwait(false);
                    RealtimeTestClient client = await RealtimeTestKit.OpenAsync(f, member.AccessToken, ct).ConfigureAwait(false);
                    await using (client.ConfigureAwait(false))
                    {
                        await client.SendBinaryAsync(new byte[] { 1, 2, 3 }, ct).ConfigureAwait(false);
                        Check.Equal<int?>(1003, await client.WaitCloseAsync(RealtimeTestKit.Wait, ct).ConfigureAwait(false), "Binary");
                    }
                }),
                SuiteCases.Http(Id, "Frames.Oversized", "A frame over realtime.maxInboundFrameBytes closes 1009", s => s.Realtime.MaxInboundFrameBytes = 1024, async (f, ct) =>
                {
                    TestMember member = await Api.MemberAsync(f, ct).ConfigureAwait(false);
                    RealtimeTestClient client = await RealtimeTestKit.OpenAsync(f, member.AccessToken, ct).ConfigureAwait(false);
                    await using (client.ConfigureAwait(false))
                    {
                        Check.Equal(1024, client.Welcome.GetProperty("maxInboundFrameBytes").GetInt32(), "Welcome cap");
                        await client.SendAsync(new { type = "ping", data = new { pad = new string('x', 4000) } }, ct).ConfigureAwait(false);
                        Check.Equal<int?>(1009, await client.WaitCloseAsync(RealtimeTestKit.Wait, ct).ConfigureAwait(false), "Too large");
                    }
                }),
                SuiteCases.Http(Id, "Frames.Unknown", "An unknown type gets error UnknownFrame and the socket stays open; reserved types without a handler are ignored", async (f, ct) =>
                {
                    TestMember member = await Api.MemberAsync(f, ct).ConfigureAwait(false);
                    RealtimeTestClient client = await RealtimeTestKit.OpenAsync(f, member.AccessToken, ct).ConfigureAwait(false);
                    await using (client.ConfigureAwait(false))
                    {
                        await client.SendAsync(new { type = "bogus", id = "b1", data = new { } }, ct).ConfigureAwait(false);
                        JsonElement error = await client.ExpectDataAsync("error", RealtimeTestKit.Wait, ct).ConfigureAwait(false);
                        Check.Equal("UnknownFrame", error.GetProperty("code").GetString(), "Unknown frame");
                        Check.Equal("b1", error.GetProperty("frameId").GetString(), "Frame id");
                        await client.SendAsync(new { type = "ack", data = new { conversationId = "cnv_x", seq = 1 } }, ct).ConfigureAwait(false);
                        List<string> skipped = new List<string>();
                        await client.SendAsync(new { type = "ping", data = new { } }, ct).ConfigureAwait(false);
                        await client.ExpectAsync("pong", RealtimeTestKit.Wait, ct, skipped).ConfigureAwait(false);
                        // The messaging module handles ack; without the messaging capability (a never-paid member in the FullyGated
                        // fixture) the gateway answers one CapabilityRequired error, and an unknown conversation is otherwise ignored.
                        Check.True(skipped.Count == 0 || (skipped.Count == 1 && skipped[0].Contains("error", StringComparison.Ordinal)), "Reserved type ignored or refused by capability (" + String.Join(", ", skipped) + ")");
                        await client.SendAsync(new { type = "state", id = "s1", data = new { appState = "Sideways" } }, ct).ConfigureAwait(false);
                        Check.Equal("ValidationFailed", (await client.ExpectDataAsync("error", RealtimeTestKit.Wait, ct).ConfigureAwait(false)).GetProperty("code").GetString(), "State validation");
                    }
                }),
                SuiteCases.Http(Id, "Upgrade.Checks", "Before the upgrade: plain GET 426, foreign Origin 403, allowed and absent Origin accepted, subprotocol 400", async (f, ct) =>
                {
                    ApiResult plain = await Api.SendAsync(f, HttpMethod.Get, RealtimeGateway.Path, null, null, ct, "web").ConfigureAwait(false);
                    Check.Equal(HttpStatusCode.UpgradeRequired, plain.Status, "Plain GET");
                    Check.Equal(403, await RealtimeTestKit.UpgradeStatusAsync(f, ct, "https://attacker.invalid").ConfigureAwait(false), "Foreign origin");
                    ApiResult origin = await RealtimeTestKit.RawUpgradeAsync(f, ct, new Dictionary<string, string> { ["Origin"] = "https://attacker.invalid" }).ConfigureAwait(false);
                    Check.Equal("OriginNotAllowed", origin.Error, "Origin error code");
                    Check.Equal(0, await RealtimeTestKit.UpgradeStatusAsync(f, ct, "http://localhost:5173").ConfigureAwait(false), "cors.allowedOrigins origin");
                    Check.Equal(0, await RealtimeTestKit.UpgradeStatusAsync(f, ct, "https://" + f.Property.Domains.Web).ConfigureAwait(false), "Property web origin");
                    Check.Equal(0, await RealtimeTestKit.UpgradeStatusAsync(f, ct).ConfigureAwait(false), "No origin (native apps)");
                    Check.Equal(400, await RealtimeTestKit.UpgradeStatusAsync(f, ct, null, "tk.v1").ConfigureAwait(false), "Subprotocol refused");
                    ApiResult sub = await RealtimeTestKit.RawUpgradeAsync(f, ct, new Dictionary<string, string> { ["Sec-WebSocket-Protocol"] = "tk.v1" }).ConfigureAwait(false);
                    Check.Equal("SubprotocolNotSupported", sub.Error, "Subprotocol error code");
                }),
                SuiteCases.Http(Id, "Upgrade.Limits", "Anonymous upgrades per address per minute and pending hellos per address return 429 with Retry-After", s => { s.Realtime.AnonymousUpgradesPerMinute = 3; s.Realtime.MaxPendingHellos = 2; }, async (f, ct) =>
                {
                    RealtimeTestClient one = await RealtimeTestKit.ConnectAsync(f, ct).ConfigureAwait(false);
                    RealtimeTestClient two = await RealtimeTestKit.ConnectAsync(f, ct).ConfigureAwait(false);
                    await using (one.ConfigureAwait(false))
                    await using (two.ConfigureAwait(false))
                    {
                        ApiResult pending = await RealtimeTestKit.RawUpgradeAsync(f, ct).ConfigureAwait(false);
                        Check.Equal(HttpStatusCode.TooManyRequests, pending.Status, "Pending hellos");
                        Check.True(pending.RetryAfter != null, "Retry-After on pending");
                        ApiResult rate = await RealtimeTestKit.RawUpgradeAsync(f, ct).ConfigureAwait(false);
                        Check.Equal(HttpStatusCode.TooManyRequests, rate.Status, "Upgrades per minute");
                        Check.Equal("RateLimited", rate.Error, "Error code");
                        Check.True(rate.RetryAfter != null, "Retry-After on rate");
                    }
                }),
                SuiteCases.Http(Id, "KillSwitch.Realtime", "The realtime kill switch refuses upgrades with 503 FeatureDisabled and closes open sockets 4503; the messaging switch sends feature.disabled", async (f, ct) =>
                {
                    TestMember member = await Api.MemberAsync(f, ct).ConfigureAwait(false);
                    RealtimeTestClient open = await RealtimeTestKit.OpenAsync(f, member.AccessToken, ct).ConfigureAwait(false);
                    await using (open.ConfigureAwait(false))
                    {
                        await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/admin/app/kill-switches/messaging", HttpFixture.AdminToken, new { killed = true, message = "Messages are paused.", reason = "Test" }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                        JsonElement disabled = await open.ExpectDataAsync("feature.disabled", RealtimeTestKit.Wait, ct).ConfigureAwait(false);
                        Check.Equal("messaging", disabled.GetProperty("feature").GetString(), "Messaging disabled");
                        Check.Equal("Messages are paused.", disabled.GetProperty("message").GetString(), "Switch message");
                        await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/admin/app/kill-switches/realtime", HttpFixture.AdminToken, new { killed = true, reason = "Test" }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                        Check.Equal<int?>(4503, await open.WaitCloseAsync(RealtimeTestKit.Wait, ct).ConfigureAwait(false), "Open socket closed");
                    }
                    ApiResult refused = await RealtimeTestKit.RawUpgradeAsync(f, ct).ConfigureAwait(false);
                    Check.Equal(HttpStatusCode.ServiceUnavailable, refused.Status, "Upgrade refused");
                    Check.Equal("FeatureDisabled", refused.Error, "Error code");
                }),
                SuiteCases.Http(Id, "Disabled.Setting", "realtime.enabled false refuses upgrades with 503 FeatureDisabled", s => s.Realtime.Enabled = false, async (f, ct) =>
                {
                    ApiResult refused = await RealtimeTestKit.RawUpgradeAsync(f, ct).ConfigureAwait(false);
                    Check.Equal(HttpStatusCode.ServiceUnavailable, refused.Status, "Disabled");
                    Check.Equal("FeatureDisabled", refused.Error, "Error code");
                    Check.Equal("InProcess", f.Server.RealtimeBus.Mode, "No PostgreSQL listener when realtime is off");
                }),
                SuiteCases.Http(Id, "Auth.ExpiringAndRefresh", "auth.expiring arrives 5 minutes before expiry; auth.refresh with the same session extends it, another session closes 4401", async (f, ct) =>
                {
                    TestMember member = await Api.MemberAsync(f, ct).ConfigureAwait(false);
                    string soon = await RealtimeTestKit.TokenExpiringInAsync(f, member.AccessToken, TimeSpan.FromMinutes(4), ct).ConfigureAwait(false);
                    RealtimeTestClient client = await RealtimeTestKit.OpenAsync(f, soon, ct).ConfigureAwait(false);
                    await using (client.ConfigureAwait(false))
                    {
                        JsonElement expiring = await client.ExpectDataAsync("auth.expiring", RealtimeTestKit.Wait, ct).ConfigureAwait(false);
                        DateTime expires = expiring.GetProperty("expiresUtc").GetDateTime().ToUniversalTime();
                        Check.True(expires > DateTime.UtcNow.AddMinutes(3) && expires < DateTime.UtcNow.AddMinutes(5), "Expiry " + expires.ToString("O"));
                        string fresh = await RealtimeTestKit.TokenIssuedAtAsync(f, member.AccessToken, DateTime.UtcNow, ct).ConfigureAwait(false);
                        await client.SendAsync(new { type = "auth.refresh", id = "r1", data = new { accessToken = fresh } }, ct).ConfigureAwait(false);
                        await RealtimeTestKit.PingAsync(client, ct).ConfigureAwait(false);
                        DateTime? tracked = f.Server.Realtime.TokenExpiryOf(client.ConnectionId);
                        Check.True(tracked != null && tracked.Value > DateTime.UtcNow.AddMinutes(50), "Refreshed expiry");
                        TestMember again = await Api.LoginAsync(f, new TestMember { Email = member.Email, Password = member.Password }, ct).ConfigureAwait(false);
                        await client.SendAsync(new { type = "auth.refresh", id = "r2", data = new { accessToken = again.AccessToken } }, ct).ConfigureAwait(false);
                        Check.Equal("Unauthorized", (await client.ExpectDataAsync("error", RealtimeTestKit.Wait, ct).ConfigureAwait(false)).GetProperty("code").GetString(), "Other session refused");
                        Check.Equal<int?>(4401, await client.WaitCloseAsync(RealtimeTestKit.Wait, ct).ConfigureAwait(false), "Closed");
                    }
                }),
                SuiteCases.Http(Id, "Auth.Expiry", "A token that expires without auth.refresh closes 4401", async (f, ct) =>
                {
                    TestMember member = await Api.MemberAsync(f, ct).ConfigureAwait(false);
                    string brief = await RealtimeTestKit.TokenExpiringInAsync(f, member.AccessToken, TimeSpan.FromSeconds(3), ct).ConfigureAwait(false);
                    RealtimeTestClient client = await RealtimeTestKit.OpenAsync(f, brief, ct).ConfigureAwait(false);
                    await using (client.ConfigureAwait(false))
                    {
                        Check.Equal<int?>(4401, await client.WaitCloseAsync(RealtimeTestKit.Wait, ct).ConfigureAwait(false), "Expired");
                    }
                }),
                SuiteCases.Http(Id, "Revocation", "Sign-out and revoking every session close the session's sockets 4401 within realtime.revocationSeconds; other sessions stay", async (f, ct) =>
                {
                    TestMember member = await Api.MemberAsync(f, ct).ConfigureAwait(false);
                    TestMember second = await Api.LoginAsync(f, new TestMember { Email = member.Email, Password = member.Password }, ct).ConfigureAwait(false);
                    RealtimeTestClient first = await RealtimeTestKit.OpenAsync(f, member.AccessToken, ct).ConfigureAwait(false);
                    RealtimeTestClient other = await RealtimeTestKit.OpenAsync(f, second.AccessToken, ct).ConfigureAwait(false);
                    await using (first.ConfigureAwait(false))
                    await using (other.ConfigureAwait(false))
                    {
                        await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/auth/logout", null, new { refreshToken = member.RefreshToken }, HttpStatusCode.NoContent, ct).ConfigureAwait(false);
                        Check.Equal<int?>(4401, await first.WaitCloseAsync(TimeSpan.FromSeconds(f.Settings.Realtime.RevocationSeconds), ct).ConfigureAwait(false), "Signed-out session closed in time");
                        await RealtimeTestKit.PingAsync(other, ct).ConfigureAwait(false);
                        await f.Services.Sessions.RevokeAllAsync(member.Id, null, "test", ct).ConfigureAwait(false);
                        Check.Equal<int?>(4401, await other.WaitCloseAsync(TimeSpan.FromSeconds(f.Settings.Realtime.RevocationSeconds), ct).ConfigureAwait(false), "Revoke all closed");
                    }
                }),
                SuiteCases.Http(Id, "Heartbeat.SilenceDrop", "A connection silent for two heartbeat intervals plus 5 seconds is dropped; pings keep it open", async (f, ct) =>
                {
                    f.Server.Realtime.HeartbeatSecondsOverride = 1;
                    TestMember member = await Api.MemberAsync(f, ct).ConfigureAwait(false);
                    RealtimeTestClient silent = await RealtimeTestKit.OpenAsync(f, member.AccessToken, ct).ConfigureAwait(false);
                    RealtimeTestClient talking = await RealtimeTestKit.OpenAsync(f, member.AccessToken, ct).ConfigureAwait(false);
                    await using (silent.ConfigureAwait(false))
                    await using (talking.ConfigureAwait(false))
                    {
                        Check.Equal(1, silent.Welcome.GetProperty("heartbeatSeconds").GetInt32(), "Override in welcome");
                        Task<int?> dropped = silent.WaitCloseAsync(TimeSpan.FromSeconds(12), ct);
                        for (int i = 0; i < 10 && !dropped.IsCompleted; i++)
                        {
                            await RealtimeTestKit.PingAsync(talking, ct).ConfigureAwait(false);
                            await Task.Delay(1000, ct).ConfigureAwait(false);
                        }
                        Check.Equal<int?>(1000, await dropped.ConfigureAwait(false), "Silent connection dropped");
                        await RealtimeTestKit.PingAsync(talking, ct).ConfigureAwait(false);
                    }
                }),
                SuiteCases.Http(Id, "Limits.ConnectionCap", "Beyond realtime.maxConnectionsPerUser the oldest connection closes 4409", s => s.Realtime.MaxConnectionsPerUser = 2, async (f, ct) =>
                {
                    TestMember member = await Api.MemberAsync(f, ct).ConfigureAwait(false);
                    RealtimeTestClient a = await RealtimeTestKit.OpenAsync(f, member.AccessToken, ct).ConfigureAwait(false);
                    RealtimeTestClient b = await RealtimeTestKit.OpenAsync(f, member.AccessToken, ct).ConfigureAwait(false);
                    RealtimeTestClient c = await RealtimeTestKit.OpenAsync(f, member.AccessToken, ct).ConfigureAwait(false);
                    await using (a.ConfigureAwait(false))
                    await using (b.ConfigureAwait(false))
                    await using (c.ConfigureAwait(false))
                    {
                        Check.Equal<int?>(4409, await a.WaitCloseAsync(RealtimeTestKit.Wait, ct).ConfigureAwait(false), "Oldest replaced");
                        await RealtimeTestKit.PingAsync(b, ct).ConfigureAwait(false);
                        await RealtimeTestKit.PingAsync(c, ct).ConfigureAwait(false);
                    }
                }),
                SuiteCases.Http(Id, "Limits.Connects", "New connections per member per minute: the next hello gets error RateLimited with retryAfterMs and closes 4429", s => s.Realtime.ConnectsPerMinute = 1, async (f, ct) =>
                {
                    TestMember member = await Api.MemberAsync(f, ct).ConfigureAwait(false);
                    RealtimeTestClient a = await RealtimeTestKit.OpenAsync(f, member.AccessToken, ct).ConfigureAwait(false);
                    RealtimeTestClient b = await RealtimeTestKit.ConnectAsync(f, ct).ConfigureAwait(false);
                    await using (a.ConfigureAwait(false))
                    await using (b.ConfigureAwait(false))
                    {
                        await b.SendAsync(new { type = "hello", id = "c1", data = new { accessToken = member.AccessToken } }, ct).ConfigureAwait(false);
                        JsonElement error = await b.ExpectDataAsync("error", RealtimeTestKit.Wait, ct).ConfigureAwait(false);
                        Check.Equal("RateLimited", error.GetProperty("code").GetString(), "Code");
                        Check.True(error.GetProperty("retryAfterMs").GetInt32() > 0, "retryAfterMs");
                        Check.Equal<int?>(4429, await b.WaitCloseAsync(RealtimeTestKit.Wait, ct).ConfigureAwait(false), "Closed");
                    }
                }),
                SuiteCases.Http(Id, "Limits.InboundRate", "Inbound frames over the rate get error RateLimited with retryAfterMs; realtime.maxViolations within a minute close 4429", s => { s.Realtime.MaxInboundFramesPerSecond = 1; s.Realtime.MaxViolations = 2; }, async (f, ct) =>
                {
                    TestMember member = await Api.MemberAsync(f, ct).ConfigureAwait(false);
                    RealtimeTestClient client = await RealtimeTestKit.OpenAsync(f, member.AccessToken, ct).ConfigureAwait(false);
                    await using (client.ConfigureAwait(false))
                    {
                        for (int i = 0; i < 6; i++) await client.SendAsync(new { type = "ping", id = "p" + i, data = new { } }, ct).ConfigureAwait(false);
                        JsonElement error = await client.ExpectDataAsync("error", RealtimeTestKit.Wait, ct).ConfigureAwait(false);
                        Check.Equal("RateLimited", error.GetProperty("code").GetString(), "Code");
                        Check.True(error.GetProperty("retryAfterMs").GetInt32() > 0, "retryAfterMs");
                        Check.Equal<int?>(4429, await client.WaitCloseAsync(RealtimeTestKit.Wait, ct).ConfigureAwait(false), "Closed");
                    }
                }),
                SuiteCases.Http(Id, "Backpressure.Overflow", "A reader that stops reading overflows the outbound queue: the queue is cleared and one resync.required Overflow is sent", s => { s.Realtime.MaxQueuedFrames = 16; s.Realtime.MaxQueuedBytes = 65536; s.Realtime.SlowConsumerSeconds = 300; }, async (f, ct) =>
                {
                    RealtimeTestKit.RegisterBlob(f.Services, 30000);
                    TestMember member = await Api.MemberAsync(f, ct).ConfigureAwait(false);
                    RealtimeTestClient client = await RealtimeTestKit.OpenAsync(f, member.AccessToken, ct).ConfigureAwait(false);
                    await using (client.ConfigureAwait(false))
                    {
                        client.Pause();
                        for (int i = 0; i < 1500; i++) RealtimeTestKit.Local(f, RealtimeTestKit.BlobKind, member.Id, i);
                        await Task.Delay(1500, ct).ConfigureAwait(false);
                        client.Resume();
                        JsonElement resync = await client.ExpectDataAsync("resync.required", TimeSpan.FromSeconds(30), ct).ConfigureAwait(false);
                        Check.Equal("Overflow", resync.GetProperty("reason").GetString(), "Reason");
                        await RealtimeTestKit.PingAsync(client, ct).ConfigureAwait(false);
                    }
                }),
                SuiteCases.Http(Id, "Backpressure.SlowConsumer", "A connection whose writes stay blocked longer than the slow consumer bound is closed 1013", s => { s.Realtime.MaxQueuedFrames = 10000; s.Realtime.MaxQueuedBytes = 67108864; }, async (f, ct) =>
                {
                    f.Server.Realtime.SlowConsumerSecondsOverride = 2;
                    RealtimeTestKit.RegisterBlob(f.Services, 60000);
                    TestMember member = await Api.MemberAsync(f, ct).ConfigureAwait(false);
                    RealtimeTestClient client = await RealtimeTestKit.OpenAsync(f, member.AccessToken, ct).ConfigureAwait(false);
                    await using (client.ConfigureAwait(false))
                    {
                        string connectionId = client.ConnectionId;
                        client.Pause();
                        for (int i = 0; i < 1000; i++) RealtimeTestKit.Local(f, RealtimeTestKit.BlobKind, member.Id, i);
                        Check.True(await RealtimeTestKit.EventuallyAsync(() => Task.FromResult(f.Server.Realtime.CloseCodeOf(connectionId) == 1013), TimeSpan.FromSeconds(20), ct).ConfigureAwait(false), "Closed 1013 (got " + f.Server.Realtime.CloseCodeOf(connectionId) + ")");
                    }
                }),
                SuiteCases.Http(Id, "Ordering.PerConversation", "Queued frames of one conversation are written in seq order; droppable frames go first on overflow", async (f, ct) =>
                {
                    PropertyRuntime runtime = f.Server.Directory.Default!;
                    RealtimeConnection connection = new RealtimeConnection("rtc_unit", runtime, null!, new System.Collections.Specialized.NameValueCollection(), "127.0.0.1", 40);
                    Func<RealtimeOutboundFrame> resync = () => new RealtimeOutboundFrame("resync.required", "{}", null, null, false);
                    connection.Enqueue(new RealtimeOutboundFrame("message.created", "3", "cnv_a", 3, false), 100, 1000000, resync);
                    connection.Enqueue(new RealtimeOutboundFrame("notification.created", "n", null, null, false), 100, 1000000, resync);
                    connection.Enqueue(new RealtimeOutboundFrame("message.created", "b1", "cnv_b", 1, false), 100, 1000000, resync);
                    connection.Enqueue(new RealtimeOutboundFrame("message.created", "1", "cnv_a", 1, false), 100, 1000000, resync);
                    connection.Enqueue(new RealtimeOutboundFrame("message.created", "2", "cnv_a", 2, false), 100, 1000000, resync);
                    List<string> order = connection.SnapshotQueue().Select(q => q.Json).ToList();
                    List<string> conversationA = order.Where(j => j == "1" || j == "2" || j == "3").ToList();
                    Check.Equal("1,2,3", String.Join(",", conversationA), "Seq order within the conversation (queue " + String.Join(",", order) + ")");
                    RealtimeConnection small = new RealtimeConnection("rtc_unit2", runtime, null!, new System.Collections.Specialized.NameValueCollection(), "127.0.0.1", 40);
                    for (int i = 0; i < 3; i++) small.Enqueue(new RealtimeOutboundFrame("typing", "t" + i, null, null, true), 4, 1000000, resync);
                    small.Enqueue(new RealtimeOutboundFrame("message.created", "m", null, null, false), 4, 1000000, resync);
                    int dropped = small.Enqueue(new RealtimeOutboundFrame("message.created", "m2", null, null, false), 4, 1000000, resync);
                    Check.Equal(3, dropped, "Typing frames dropped first");
                    Check.Equal("m,m2", String.Join(",", small.SnapshotQueue().Select(q => q.Json)), "Messages kept");
                    for (int i = 0; i < 4; i++) small.Enqueue(new RealtimeOutboundFrame("message.created", "x" + i, null, null, false), 4, 1000000, resync);
                    Check.Equal("resync.required,message.created", String.Join(",", small.SnapshotQueue().Select(q => q.Type)), "Cleared to one resync, then new frames queue behind it");
                    await Task.CompletedTask.ConfigureAwait(false);
                }),
                SuiteCases.Http(Id, "Live.NotificationCreated", "A stored notification reaches the recipient's socket as notification.created with the unread count; others get nothing", async (f, ct) =>
                {
                    TestMember owner = await NotificationTestKit.MemberAsync(f, "Example Owner", ct).ConfigureAwait(false);
                    TestMember alex = await NotificationTestKit.MemberAsync(f, "Alex Chen", ct).ConfigureAwait(false);
                    RealtimeTestClient mine = await RealtimeTestKit.OpenAsync(f, owner.AccessToken, ct).ConfigureAwait(false);
                    RealtimeTestClient theirs = await RealtimeTestKit.OpenAsync(f, alex.AccessToken, ct).ConfigureAwait(false);
                    await using (mine.ConfigureAwait(false))
                    await using (theirs.ConfigureAwait(false))
                    {
                        Notification created = (await f.Services.Notifications.CreateAsync(NotificationTestKit.Draft(NotificationTypeEnum.PostComment, owner.Id, alex.Id), ct).ConfigureAwait(false))!;
                        JsonElement data = await mine.ExpectDataAsync("notification.created", RealtimeTestKit.Wait, ct).ConfigureAwait(false);
                        Check.Equal(created.Id, data.GetProperty("notification").GetProperty("id").GetString(), "Notification id");
                        Check.Equal("Alex Chen commented on your post", data.GetProperty("notification").GetProperty("title").GetString(), "Title");
                        Check.True(data.GetProperty("notification").TryGetProperty("labels", out JsonElement _), "Labels");
                        Check.Equal(1L, data.GetProperty("unreadCount").GetInt64(), "Unread count");
                        Check.True(await theirs.NoneAsync("notification.created", TimeSpan.FromSeconds(1.5), ct).ConfigureAwait(false), "Not delivered to the actor");
                        await f.Services.Realtime.DeliverToUsersAsync(RealtimeEventKinds.TicketUpdated, new[] { owner.Id }, e => { e.TargetId = "tkt_example"; e.State = "Answered"; }, ct).ConfigureAwait(false);
                        JsonElement ticket = await mine.ExpectDataAsync("ticket.updated", RealtimeTestKit.Wait, ct).ConfigureAwait(false);
                        Check.Equal("tkt_example", ticket.GetProperty("ticketId").GetString(), "Ticket id");
                        Check.Equal("Answered", ticket.GetProperty("status").GetString(), "Ticket status");
                        await f.Services.Realtime.DeliverToUsersAsync(RealtimeEventKinds.UnreadCounts, new[] { owner.Id }, null, ct).ConfigureAwait(false);
                        Check.Equal(1L, (await mine.ExpectDataAsync("unread.counts", RealtimeTestKit.Wait, ct).ConfigureAwait(false)).GetProperty("notifications").GetInt64(), "Unread counts");
                    }
                }),
                SuiteCases.Http(Id, "Capabilities.FullyGated", "FullyGated: a non-member gets notifications and tickets only; asking only for messaging closes 4402; a granted membership sends access.changed with messaging", async (f, ct) =>
                {
                    TestMember member = await Api.RegisterAsync(f, ct).ConfigureAwait(false);
                    RealtimeTestClient client = await RealtimeTestKit.OpenAsync(f, member.AccessToken, ct).ConfigureAwait(false);
                    RealtimeTestClient only = await RealtimeTestKit.ConnectAsync(f, ct).ConfigureAwait(false);
                    await using (client.ConfigureAwait(false))
                    await using (only.ConfigureAwait(false))
                    {
                        List<string> capabilities = RealtimeTestKit.Capabilities(client.Welcome);
                        Check.Equal("notifications,tickets", String.Join(",", capabilities.OrderBy(c => c, StringComparer.Ordinal)), "Non-member capabilities");
                        Check.Equal("SignedIn", client.Welcome.GetProperty("accessTier").GetString(), "Tier");
                        Check.Equal<int?>(4402, await RealtimeTestKit.HelloRefusedAsync(only, member.AccessToken, ct, new Dictionary<string, object?> { ["capabilities"] = new[] { "messaging" } }).ConfigureAwait(false), "Messaging only");
                        await Api.GrantCompAsync(f, member.Id, ct).ConfigureAwait(false);
                        JsonElement changed = await client.ExpectDataAsync("access.changed", RealtimeTestKit.Wait, ct).ConfigureAwait(false);
                        Check.True(RealtimeTestKit.Capabilities(changed).Contains("messaging"), "Messaging after membership");
                        Check.Equal("Member", changed.GetProperty("accessTier").GetString(), "Member tier");
                    }
                }),
                SuiteCases.Http(Id, "Capabilities.Open", "Open: a signed-in non-member gets messaging; the messaging feature off removes it", p => { p.Access = new PropertyAccess { Model = AccessModelEnum.Open }; }, async (f, ct) =>
                {
                    TestMember member = await Api.RegisterAsync(f, ct).ConfigureAwait(false);
                    RealtimeTestClient client = await RealtimeTestKit.OpenAsync(f, member.AccessToken, ct).ConfigureAwait(false);
                    await using (client.ConfigureAwait(false))
                    {
                        Check.True(RealtimeTestKit.Capabilities(client.Welcome).Contains("messaging"), "Open model messaging");
                    }
                    f.Property.Features.Messaging = false;
                    RealtimeTestClient off = await RealtimeTestKit.OpenAsync(f, member.AccessToken, ct).ConfigureAwait(false);
                    await using (off.ConfigureAwait(false))
                    {
                        Check.True(!RealtimeTestKit.Capabilities(off.Welcome).Contains("messaging"), "Feature off");
                    }
                }),
                SuiteCases.Http(Id, "Seams.Inbound", "Registered inbound handlers receive frames after the gateway's checks; typing Start is throttled per conversation; required capabilities are enforced", async (f, ct) =>
                {
                    int typing = 0;
                    f.Services.Realtime.Inbound.Register("typing", RealtimeCapabilities.Messaging, (context, token) =>
                    {
                        Interlocked.Increment(ref typing);
                        return context.SendAsync("test.typed", new { conversationId = context.DataString("conversationId"), state = context.DataString("state") }, token);
                    });
                    f.Services.Realtime.Inbound.Register("test.echo", null, (context, token) => context.SendAsync("test.echo", new { userId = context.UserId, value = context.DataLong("value") }, token));
                    f.Services.Realtime.Inbound.Register("test.fail", null, (context, token) => throw new InvalidOperationException("boom"));
                    Check.Throws<ArgumentException>(() => f.Services.Realtime.Inbound.Register("ping", null, (context, token) => Task.CompletedTask), "Gateway types are reserved");
                    TestMember member = await Api.MemberAsync(f, ct).ConfigureAwait(false);
                    RealtimeTestClient client = await RealtimeTestKit.OpenAsync(f, member.AccessToken, ct).ConfigureAwait(false);
                    await using (client.ConfigureAwait(false))
                    {
                        await client.SendAsync(new { type = "test.echo", data = new { value = 7 } }, ct).ConfigureAwait(false);
                        JsonElement echo = await client.ExpectDataAsync("test.echo", RealtimeTestKit.Wait, ct).ConfigureAwait(false);
                        Check.Equal(7L, echo.GetProperty("value").GetInt64(), "Echo value");
                        Check.Equal(member.Id, echo.GetProperty("userId").GetString(), "Echo user");
                        await client.SendAsync(new { type = "typing", data = new { conversationId = "cnv_t", state = "Start" } }, ct).ConfigureAwait(false);
                        await client.SendAsync(new { type = "typing", data = new { conversationId = "cnv_t", state = "Start" } }, ct).ConfigureAwait(false);
                        await client.SendAsync(new { type = "typing", data = new { conversationId = "cnv_t", state = "Stop" } }, ct).ConfigureAwait(false);
                        await client.ExpectAsync("test.typed", RealtimeTestKit.Wait, ct).ConfigureAwait(false);
                        await client.ExpectAsync("test.typed", RealtimeTestKit.Wait, ct).ConfigureAwait(false);
                        await RealtimeTestKit.PingAsync(client, ct).ConfigureAwait(false);
                        Check.Equal(2, Volatile.Read(ref typing), "One Start plus one Stop reached the handler");
                        await client.SendAsync(new { type = "test.fail", id = "x1", data = new { } }, ct).ConfigureAwait(false);
                        Check.Equal("InternalError", (await client.ExpectDataAsync("error", RealtimeTestKit.Wait, ct).ConfigureAwait(false)).GetProperty("code").GetString(), "Handler failure");
                        await client.SendAsync(new { type = "state", data = new { appState = "Background", activeConversationId = "cnv_t" } }, ct).ConfigureAwait(false);
                        await RealtimeTestKit.PingAsync(client, ct).ConfigureAwait(false);
                        RealtimeForeground foreground = await f.Services.Realtime.Registry.ForegroundAsync(member.Id, "cnv_t", ct).ConfigureAwait(false);
                        Check.True(!foreground.HasForeground && foreground.RecentConnections == 1, "Background state recorded");
                    }
                    TestMember plain = await Api.RegisterAsync(f, ct).ConfigureAwait(false);
                    RealtimeTestClient limited = await RealtimeTestKit.OpenAsync(f, plain.AccessToken, ct).ConfigureAwait(false);
                    await using (limited.ConfigureAwait(false))
                    {
                        await limited.SendAsync(new { type = "typing", id = "t1", data = new { conversationId = "cnv_t", state = "Start" } }, ct).ConfigureAwait(false);
                        Check.Equal("CapabilityRequired", (await limited.ExpectDataAsync("error", RealtimeTestKit.Wait, ct).ConfigureAwait(false)).GetProperty("code").GetString(), "Capability enforced");
                    }
                }),
                SuiteCases.Http(Id, "Draining", "Draining sends shutdown with reconnectAfterMs from 0 to 5000, then closes 1012; health reports Draining and new upgrades get 503", s => s.Realtime.DrainSeconds = 1, async (f, ct) =>
                {
                    TestMember member = await Api.MemberAsync(f, ct).ConfigureAwait(false);
                    RealtimeTestClient client = await RealtimeTestKit.OpenAsync(f, member.AccessToken, ct).ConfigureAwait(false);
                    await using (client.ConfigureAwait(false))
                    {
                        Task drain = f.Server.Realtime.DrainAsync(ct);
                        JsonElement shutdown = await client.ExpectDataAsync("shutdown", RealtimeTestKit.Wait, ct).ConfigureAwait(false);
                        int after = shutdown.GetProperty("reconnectAfterMs").GetInt32();
                        Check.True(after >= 0 && after <= 5000, "Jitter " + after);
                        Check.Equal<int?>(1012, await client.WaitCloseAsync(RealtimeTestKit.Wait, ct).ConfigureAwait(false), "Service restart");
                        await drain.ConfigureAwait(false);
                    }
                    JsonElement health = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/health", null, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.Equal("Draining", health.GetProperty("status").GetString(), "Health");
                    ApiResult ready = await Api.SendAsync(f, HttpMethod.Get, "/api/v1/health/ready", null, null, ct).ConfigureAwait(false);
                    Check.Equal(HttpStatusCode.ServiceUnavailable, ready.Status, "Not ready");
                    ApiResult refused = await RealtimeTestKit.RawUpgradeAsync(f, ct).ConfigureAwait(false);
                    Check.Equal(HttpStatusCode.ServiceUnavailable, refused.Status, "Upgrade refused while draining");
                    Check.True(refused.RetryAfter != null, "Retry-After");
                }),
                SuiteCases.Unit(Id, "Bus.Payload", "Bus payloads use single-letter keys, omit nulls, and use a schema-suffixed channel", async ct =>
                {
                    Check.Equal("tk_realtime", PostgresRealtimeBus.ChannelFor(null), "Default channel");
                    Check.Equal("tk_realtime_tk_test_ab12", PostgresRealtimeBus.ChannelFor("TK_Test-AB12"), "Schema channel");
                    Check.True(PostgresRealtimeBus.ChannelFor(new string('a', 100)).Length == 63, "Channel length cap");
                    RealtimeEvent sample = new RealtimeEvent { PropertyId = "app_x", Kind = RealtimeEventKinds.TicketUpdated, TargetId = "tkt_1", State = "it's", UserIds = new List<string> { "usr_a" } };
                    string json = PostgresRealtimeBus.Serialize(sample);
                    Check.True(json.Contains("\"p\":\"app_x\"", StringComparison.Ordinal) && json.Contains("\"k\":\"ticket.updated\"", StringComparison.Ordinal), "Short keys: " + json);
                    Check.True(!json.Contains("\"c\":", StringComparison.Ordinal), "Nulls omitted");
                    await Task.CompletedTask.ConfigureAwait(false);
                })
            });
        }

        #endregion
    }
}
