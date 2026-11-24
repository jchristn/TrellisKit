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
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Helpers;
    using TrellisKit.Core.Models;
    using TrellisKit.Core.Settings;
    using TrellisKit.Server.Services.Realtime;

    /// <summary>
    /// Realtime across server tasks (docs/PHASE5_CONTRACT.md section 4.11): two server instances in one test against
    /// the same PostgreSQL schema, each with its own PostgresRealtimeBus listener. Cross-task delivery and revocation,
    /// property isolation of sockets and bus payloads, listener reconnect resync, registry-based foreground detection,
    /// and draining one instance.
    /// </summary>
    public static class RealtimeScaleSuite
    {
        #region Public-Members

        /// <summary>
        /// Suite identifier.
        /// </summary>
        public const string Id = "RealtimeScale";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the suite.
        /// </summary>
        /// <returns>Suite.</returns>
        public static TestSuiteDescriptor Build()
        {
            return new TestSuiteDescriptor(Id, "Realtime scale (" + TestEnvironment.Provider + ")", new[]
            {
                Case("CrossTask.Delivery", "An event published on instance B reaches a socket on instance A; a notification stored on B arrives live on A; signing out on B closes A's socket 4401", null, async (a, b, ct) =>
                {
                    Check.Equal("Postgres", a.Server.RealtimeBus.Mode, "PostgreSQL bus on A");
                    Check.Equal("Postgres", b.Server.RealtimeBus.Mode, "PostgreSQL bus on B");
                    Check.True(a.Server.Realtime.TaskId != b.Server.Realtime.TaskId, "Distinct task ids");
                    TestMember owner = await NotificationTestKit.MemberAsync(a, "Example Owner", ct).ConfigureAwait(false);
                    TestMember alex = await NotificationTestKit.MemberAsync(a, "Alex Chen", ct).ConfigureAwait(false);
                    RealtimeTestClient client = await RealtimeTestKit.OpenAsync(a, owner.AccessToken, ct).ConfigureAwait(false);
                    await using (client.ConfigureAwait(false))
                    {
                        await b.Services.Realtime.DeliverToUsersAsync(RealtimeEventKinds.TicketUpdated, new[] { owner.Id }, e => { e.TargetId = "tkt_cross"; e.State = "Answered"; }, ct).ConfigureAwait(false);
                        JsonElement ticket = await client.ExpectDataAsync("ticket.updated", RealtimeTestKit.Wait, ct).ConfigureAwait(false);
                        Check.Equal("tkt_cross", ticket.GetProperty("ticketId").GetString(), "Cross-task event");
                        Notification created = (await b.Services.Notifications.CreateAsync(NotificationTestKit.Draft(NotificationTypeEnum.PostComment, owner.Id, alex.Id), ct).ConfigureAwait(false))!;
                        JsonElement live = await client.ExpectDataAsync("notification.created", RealtimeTestKit.Wait, ct).ConfigureAwait(false);
                        Check.Equal(created.Id, live.GetProperty("notification").GetProperty("id").GetString(), "Notification from B");
                        await Api.ExpectAsync(b, HttpMethod.Post, "/api/v1/auth/logout", null, new { refreshToken = owner.RefreshToken }, HttpStatusCode.NoContent, ct).ConfigureAwait(false);
                        Check.Equal<int?>(4401, await client.WaitCloseAsync(TimeSpan.FromSeconds(a.Settings.Realtime.RevocationSeconds), ct).ConfigureAwait(false), "Revoked on B, closed on A");
                    }
                }),
                Case("Isolation.Properties", "A socket of property A never receives property B's events; a bus payload naming another property is dropped by A's dispatcher; disabling B closes B's sockets 4401", MultiPropertyKit.Multi, async (a, b, ct) =>
                {
                    string platformToken = await MultiPropertyKit.PlatformLoginAsync(a, ct).ConfigureAwait(false);
                    string second = await MultiPropertyKit.CreateSecondAsync(a, platformToken, ct).ConfigureAwait(false);
                    await b.Server.Directory.ReloadAsync(ct).ConfigureAwait(false);
                    a.PropertyId = TestProperty.SampleId;
                    b.PropertyId = TestProperty.SampleId;
                    TestMember memberA = await Api.RegisterAsync(a, ct).ConfigureAwait(false);
                    RealtimeTestClient socketA = await RealtimeTestKit.OpenAsync(a, memberA.AccessToken, ct).ConfigureAwait(false);
                    a.PropertyId = second;
                    TestMember memberB = await Api.RegisterAsync(a, ct).ConfigureAwait(false);
                    RealtimeTestClient socketB = await RealtimeTestKit.OpenAsync(a, memberB.AccessToken, ct).ConfigureAwait(false);
                    await using (socketA.ConfigureAwait(false))
                    await using (socketB.ConfigureAwait(false))
                    {
                        TrellisKit.Server.Services.ApplicationServices servicesB = b.Server.Directory.Find(second)!.Services;
                        await servicesB.Realtime.DeliverToUsersAsync(RealtimeEventKinds.TicketUpdated, new[] { memberB.Id, memberA.Id }, e => { e.TargetId = "tkt_b"; e.State = "Open"; }, ct).ConfigureAwait(false);
                        Check.Equal("tkt_b", (await socketB.ExpectDataAsync("ticket.updated", RealtimeTestKit.Wait, ct).ConfigureAwait(false)).GetProperty("ticketId").GetString(), "B's member gets B's event");
                        RealtimeEvent forged = new RealtimeEvent { PropertyId = PropertyIds.Generate(), Kind = RealtimeEventKinds.TicketUpdated, TargetId = "tkt_forged", UserIds = new List<string> { memberA.Id } };
                        await b.Server.RealtimeBus.PublishAsync(forged, ct).ConfigureAwait(false);
                        Check.True(await socketA.NoneAsync("ticket.updated", TimeSpan.FromSeconds(2), ct).ConfigureAwait(false), "A's socket received neither B's event nor the foreign payload");
                        await RealtimeTestKit.PingAsync(socketA, ct).ConfigureAwait(false);
                        await MultiPropertyKit.PlatformExpectAsync(a, HttpMethod.Post, "/api/v1/platform/properties/" + second + "/disable", platformToken, new { reason = "test" }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                        Check.Equal<int?>(4401, await socketB.WaitCloseAsync(TimeSpan.FromSeconds(a.Settings.Realtime.RevocationSeconds), ct).ConfigureAwait(false), "Disabled property's socket closed");
                        await RealtimeTestKit.PingAsync(socketA, ct).ConfigureAwait(false);
                    }
                }),
                Case("Bus.Reconnect", "Killing A's listener connection makes A send resync.required BusReconnected; delivery from B resumes after the reconnect", null, async (a, b, ct) =>
                {
                    TestMember member = await Api.MemberAsync(a, ct).ConfigureAwait(false);
                    RealtimeTestClient client = await RealtimeTestKit.OpenAsync(a, member.AccessToken, ct).ConfigureAwait(false);
                    await using (client.ConfigureAwait(false))
                    {
                        PostgresRealtimeBus bus = (PostgresRealtimeBus)a.Server.RealtimeBus;
                        Check.True(await bus.WaitUntilListeningAsync(RealtimeTestKit.Wait, ct).ConfigureAwait(false), "Listening");
                        int pid = bus.ListenerProcessId!.Value;
                        await a.Raw.ExecuteQueryAsync("SELECT pg_terminate_backend(" + pid + ");", false, ct).ConfigureAwait(false);
                        JsonElement resync = await client.ExpectDataAsync("resync.required", TimeSpan.FromSeconds(15), ct).ConfigureAwait(false);
                        Check.Equal("BusReconnected", resync.GetProperty("reason").GetString(), "Reason");
                        Check.True(bus.ListenerProcessId != pid, "New listener connection");
                        await b.Services.Realtime.DeliverToUsersAsync(RealtimeEventKinds.TicketUpdated, new[] { member.Id }, e => { e.TargetId = "tkt_after"; e.State = "Open"; }, ct).ConfigureAwait(false);
                        Check.Equal("tkt_after", (await client.ExpectDataAsync("ticket.updated", RealtimeTestKit.Wait, ct).ConfigureAwait(false)).GetProperty("ticketId").GetString(), "Delivery resumed");
                    }
                }),
                Case("Registry.Foreground", "A member connected to A counts as foreground for a push decision made on B; state Background and disconnect clear it; activeConversationId is reported", null, async (a, b, ct) =>
                {
                    TestMember member = await Api.MemberAsync(a, ct).ConfigureAwait(false);
                    Check.True(!(await b.Services.Realtime.Registry.ForegroundAsync(member.Id, null, ct).ConfigureAwait(false)).HasForeground, "Offline before connecting");
                    RealtimeTestClient client = await RealtimeTestKit.OpenAsync(a, member.AccessToken, ct).ConfigureAwait(false);
                    await using (client.ConfigureAwait(false))
                    {
                        RealtimeForeground seen = await b.Services.Realtime.Registry.ForegroundAsync(member.Id, "cnv_open", ct).ConfigureAwait(false);
                        Check.True(seen.HasForeground && !seen.ConversationActive, "Foreground on A seen from B");
                        await client.SendAsync(new { type = "state", data = new { appState = "Foreground", activeConversationId = "cnv_open" } }, ct).ConfigureAwait(false);
                        Check.True(await RealtimeTestKit.EventuallyAsync(async () => (await b.Services.Realtime.Registry.ForegroundAsync(member.Id, "cnv_open", ct).ConfigureAwait(false)).ConversationActive, RealtimeTestKit.Wait, ct).ConfigureAwait(false), "Active conversation seen from B");
                        await client.SendAsync(new { type = "state", data = new { appState = "Background", activeConversationId = (string?)null } }, ct).ConfigureAwait(false);
                        Check.True(await RealtimeTestKit.EventuallyAsync(async () => !(await b.Services.Realtime.Registry.ForegroundAsync(member.Id, null, ct).ConfigureAwait(false)).HasForeground, RealtimeTestKit.Wait, ct).ConfigureAwait(false), "Background clears foreground");
                    }
                    Check.True(await RealtimeTestKit.EventuallyAsync(async () => (await b.Services.Realtime.Registry.ForegroundAsync(member.Id, null, ct).ConfigureAwait(false)).RecentConnections == 0, RealtimeTestKit.Wait, ct).ConfigureAwait(false), "Row deleted on disconnect");
                }),
                Case("Registry.Prune", "RealtimeRegistryPruneWorker deletes rows not seen for 5 minutes on every property and keeps fresh rows", null, async (a, b, ct) =>
                {
                    TestMember member = await Api.MemberAsync(a, ct).ConfigureAwait(false);
                    DateTime stale = DateTime.UtcNow.AddMinutes(-10);
                    await a.Database.RealtimeConnections.UpsertAsync(new RealtimeConnectionRecord { Id = "rtc_stale", UserId = member.Id, SessionId = "ses_x", TaskId = "tsk_dead", ConnectedUtc = stale, LastSeenUtc = stale }, ct).ConfigureAwait(false);
                    await a.Database.RealtimeConnections.UpsertAsync(new RealtimeConnectionRecord { Id = "rtc_fresh", UserId = member.Id, SessionId = "ses_x", TaskId = "tsk_live" }, ct).ConfigureAwait(false);
                    TrellisKit.Server.Workers.RealtimeRegistryPruneWorker worker = new TrellisKit.Server.Workers.RealtimeRegistryPruneWorker(a.Raw, a.Services.Logger);
                    await using (worker.ConfigureAwait(false))
                    {
                        Check.Equal(1, await worker.RunOnceAsync(ct).ConfigureAwait(false), "One stale row pruned");
                    }
                    DataTable left = await a.Raw.ExecuteQueryAsync("SELECT id FROM realtimeconnections WHERE userid = '" + member.Id + "';", false, ct).ConfigureAwait(false);
                    Check.Equal("rtc_fresh", left.Rows.Count == 1 ? left.Rows[0]["id"].ToString() : "none", "Fresh row kept");
                    await Check.ThrowsAsync<TrellisKit.Core.Exceptions.PropertyScopeException>(() => a.Database.ExecuteQueryAsync("DELETE FROM realtimeconnections WHERE taskid = 'x';", false, ct), "Bound driver refuses unscoped registry SQL").ConfigureAwait(false);
                }),
                Case("Draining.OneInstance", "Draining A closes A's sockets 1012 after shutdown and deletes A's registry rows; B's sockets keep working", s => s.Realtime.DrainSeconds = 1, async (a, b, ct) =>
                {
                    TestMember member = await Api.MemberAsync(a, ct).ConfigureAwait(false);
                    RealtimeTestClient onA = await RealtimeTestKit.OpenAsync(a, member.AccessToken, ct).ConfigureAwait(false);
                    RealtimeTestClient onB = await RealtimeTestKit.OpenAsync(b, member.AccessToken, ct).ConfigureAwait(false);
                    await using (onA.ConfigureAwait(false))
                    await using (onB.ConfigureAwait(false))
                    {
                        Check.Equal(2, (await a.Services.Realtime.Registry.RecentAsync(member.Id, ct).ConfigureAwait(false)).Count, "Two rows before draining");
                        await a.Server.Realtime.DrainAsync(ct).ConfigureAwait(false);
                        await onA.ExpectAsync("shutdown", RealtimeTestKit.Wait, ct).ConfigureAwait(false);
                        Check.Equal<int?>(1012, await onA.WaitCloseAsync(RealtimeTestKit.Wait, ct).ConfigureAwait(false), "A closed 1012");
                        List<RealtimeConnectionRecord> rows = await b.Services.Realtime.Registry.RecentAsync(member.Id, ct).ConfigureAwait(false);
                        Check.True(rows.Count == 1 && rows[0].TaskId == b.Server.Realtime.TaskId, "Only B's row is left");
                        await RealtimeTestKit.PingAsync(onB, ct).ConfigureAwait(false);
                        await a.Services.Realtime.DeliverToUsersAsync(RealtimeEventKinds.TicketUpdated, new[] { member.Id }, e => { e.TargetId = "tkt_drain"; }, ct).ConfigureAwait(false);
                        Check.Equal("tkt_drain", (await onB.ExpectDataAsync("ticket.updated", RealtimeTestKit.Wait, ct).ConfigureAwait(false)).GetProperty("ticketId").GetString(), "B still receives");
                    }
                })
            });
        }

        #endregion

        #region Private-Methods

        private static TestCaseDescriptor Case(string caseId, string name, Action<AppSettings>? settings, Func<HttpFixture, HttpFixture, CancellationToken, Task> body)
        {
            return new TestCaseDescriptor(Id, caseId, name, async ct =>
            {
                HttpFixture primary = await HttpFixture.StartAsync(ct, null, settings).ConfigureAwait(false);
                await using (primary.ConfigureAwait(false))
                {
                    HttpFixture peer = await HttpFixture.StartPeerAsync(primary, ct).ConfigureAwait(false);
                    await using (peer.ConfigureAwait(false))
                    {
                        Check.True(await ((PostgresRealtimeBus)primary.Server.RealtimeBus).WaitUntilListeningAsync(RealtimeTestKit.Wait, ct).ConfigureAwait(false), "A listening");
                        Check.True(await ((PostgresRealtimeBus)peer.Server.RealtimeBus).WaitUntilListeningAsync(RealtimeTestKit.Wait, ct).ConfigureAwait(false), "B listening");
                        await body(primary, peer, ct).ConfigureAwait(false);
                    }
                }
            }, new[] { "http", "integration", TestEnvironment.Provider.ToString().ToLowerInvariant() });
        }

        #endregion
    }
}
