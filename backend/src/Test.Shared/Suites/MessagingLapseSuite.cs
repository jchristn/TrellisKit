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
    using TrellisKit.Core.PropertyConfig;

    /// <summary>
    /// Phase 5 messaging access and lapses (docs/PHASE5_CONTRACT.md section 0.3, resolved decision 26, as-built note B):
    /// in FullyGated and in PartiallyGated with messaging gated a lapsed member gets 402 MembershipRequired on every
    /// messaging route and is unavailable to others; MessagingLapseWorker removes their conversations for them after
    /// messaging.lapsedRetentionDays (fixed clock) while the other member's history stays; rejoining before that
    /// restores everything; without gating, signed-in accounts message each other; the feature switch hides messaging.
    /// </summary>
    public static class MessagingLapseSuite
    {
        #region Public-Members

        /// <summary>
        /// Suite identifier.
        /// </summary>
        public const string Id = "MessagingLapse";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the suite.
        /// </summary>
        /// <returns>Suite.</returns>
        public static TestSuiteDescriptor Build()
        {
            return new TestSuiteDescriptor(Id, "Messaging access and lapses (" + TestEnvironment.Provider + ")", new[]
            {
                SuiteCases.Http(Id, "Lapse.FullyGatedPurge", "FullyGated: a lapsed member gets 402 on messaging routes and is unavailable; after the retention days the worker removes the conversations for them only", (f, ct) => LapseAsync(f, ct)),
                SuiteCases.Http(Id, "Lapse.PartiallyGatedPurge", "PartiallyGated with messaging gated behaves the same", (f, ct) => LapseAsync(f, ct), p => p.Access = new PropertyAccess { Model = AccessModelEnum.PartiallyGated, GatedFeatures = new List<string> { "messaging" } }),
                SuiteCases.Http(Id, "Lapse.RejoinBeforePurge", "Rejoining before messaging.lapsedRetentionDays clears the lapse and restores full access", async (f, ct) =>
                {
                    TestMember a = await MessagingTestKit.MemberAsync(f, ct).ConfigureAwait(false);
                    TestMember b = await MessagingTestKit.MemberAsync(f, ct).ConfigureAwait(false);
                    string id = await MessagingTestKit.PairAsync(f, a, b, ct).ConfigureAwait(false);
                    DateTime start = DateTime.UtcNow;
                    f.Services.Messaging.Clock = () => start;
                    await MessagingTestKit.LapseAsync(f, b.Id, ct).ConfigureAwait(false);
                    await f.Services.Messaging.Maintenance.LapsePassAsync(ct).ConfigureAwait(false);
                    Check.True(await f.Database.Conversations.ReadLapseAsync(b.Id, ct).ConfigureAwait(false) != null, "Lapse recorded");
                    await Api.GrantCompAsync(f, b.Id, ct).ConfigureAwait(false);
                    f.Services.Tokens.InvalidateUser(b.Id);
                    f.Services.Messaging.Clock = () => start.AddDays(30);
                    await f.Services.Messaging.Maintenance.LapsePassAsync(ct).ConfigureAwait(false);
                    Check.True(await f.Database.Conversations.ReadLapseAsync(b.Id, ct).ConfigureAwait(false) == null, "Lapse cleared");
                    f.Services.Messaging.Clock = () => start.AddDays(120);
                    await f.Services.Messaging.Maintenance.LapsePassAsync(ct).ConfigureAwait(false);
                    Check.True((await MessagingTestKit.ListIdsAsync(f, b, null, ct).ConfigureAwait(false)).Contains(id), "Everything kept");
                    Check.Equal(2, MessagingTestKit.Seqs(await MessagingTestKit.HistoryAsync(f, b, id, null, ct).ConfigureAwait(false)).Count, "History kept");
                }),
                SuiteCases.Http(Id, "Access.PartiallyGatedNotGated", "PartiallyGated without messaging gated: signed-in accounts without a membership message each other", (f, ct) => OpenAccessAsync(f, ct),
                    p => p.Access = new PropertyAccess { Model = AccessModelEnum.PartiallyGated, GatedFeatures = new List<string> { "forums" } }),
                SuiteCases.Http(Id, "Access.Open", "Open: signed-in accounts message each other; lapse records are never written", async (f, ct) =>
                {
                    await OpenAccessAsync(f, ct).ConfigureAwait(false);
                    Check.Equal(0, await f.Services.Messaging.Maintenance.LapsePassAsync(ct).ConfigureAwait(false), "No lapses in Open");
                }, p => p.Access = new PropertyAccess { Model = AccessModelEnum.Open }),
                SuiteCases.Http(Id, "Access.FeatureOff", "features.messaging off: every messaging route is 404 NotAvailable", async (f, ct) =>
                {
                    TestMember a = await MessagingTestKit.MemberAsync(f, ct).ConfigureAwait(false);
                    TestMember b = await MessagingTestKit.MemberAsync(f, ct).ConfigureAwait(false);
                    f.Property.Features.Messaging = false;
                    await Api.ExpectErrorAsync(f, HttpMethod.Post, MessagingTestKit.Conversations, a.AccessToken, new { recipientUserId = b.Id }, HttpStatusCode.NotFound, "NotAvailable", ct).ConfigureAwait(false);
                    await Api.ExpectErrorAsync(f, HttpMethod.Get, MessagingTestKit.Conversations, a.AccessToken, null, HttpStatusCode.NotFound, "NotAvailable", ct).ConfigureAwait(false);
                    await Api.ExpectErrorAsync(f, HttpMethod.Get, "/api/v1/me/messaging/settings", a.AccessToken, null, HttpStatusCode.NotFound, "NotAvailable", ct).ConfigureAwait(false);
                    await Api.ExpectErrorAsync(f, HttpMethod.Post, "/api/v1/messages/search/enumerate", a.AccessToken, new { search = "hello" }, HttpStatusCode.NotFound, "NotAvailable", ct).ConfigureAwait(false);
                    f.Property.Features.Messaging = true;
                })
            });
        }

        #endregion

        #region Private-Methods

        private static async Task LapseAsync(HttpFixture f, CancellationToken ct)
        {
            TestMember a = await MessagingTestKit.MemberAsync(f, ct, "Ann Stays").ConfigureAwait(false);
            TestMember b = await MessagingTestKit.MemberAsync(f, ct, "Bob Lapses").ConfigureAwait(false);
            string id = await MessagingTestKit.PairAsync(f, a, b, ct).ConfigureAwait(false);
            DateTime start = DateTime.UtcNow;
            f.Services.Messaging.Clock = () => start;
            await MessagingTestKit.LapseAsync(f, b.Id, ct).ConfigureAwait(false);
            string path = MessagingTestKit.Conversations + "/" + id;
            foreach (KeyValuePair<HttpMethod, string> route in new[]
            {
                new KeyValuePair<HttpMethod, string>(HttpMethod.Get, MessagingTestKit.Conversations),
                new KeyValuePair<HttpMethod, string>(HttpMethod.Get, path),
                new KeyValuePair<HttpMethod, string>(HttpMethod.Post, path + "/messages/enumerate"),
                new KeyValuePair<HttpMethod, string>(HttpMethod.Post, path + "/read"),
                new KeyValuePair<HttpMethod, string>(HttpMethod.Post, MessagingTestKit.Conversations + "/changes/enumerate"),
                new KeyValuePair<HttpMethod, string>(HttpMethod.Post, "/api/v1/messages/search/enumerate"),
                new KeyValuePair<HttpMethod, string>(HttpMethod.Get, "/api/v1/me/messaging/unread-count"),
                new KeyValuePair<HttpMethod, string>(HttpMethod.Post, "/api/v1/me/messaging/declined/enumerate")
            })
            {
                ApiResult result = await Api.SendAsync(f, route.Key, route.Value, b.AccessToken, route.Key == HttpMethod.Get ? null : new { seq = 1, search = "hello" }, ct).ConfigureAwait(false);
                Check.Equal(HttpStatusCode.PaymentRequired, result.Status, route.Key + " " + route.Value + " (" + result.Text + ")");
                Check.Equal("MembershipRequired", result.Error, "MembershipRequired");
            }
            await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/me/messaging/settings", b.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal("RecipientUnavailable", (await MessagingTestKit.SendAsync(f, a, id, "still there?", ct).ConfigureAwait(false)).Error, "Unavailable to the other member");
            Check.Equal(1, await f.Services.Messaging.Maintenance.LapsePassAsync(ct).ConfigureAwait(false), "Lapse recorded");
            f.Services.Messaging.Clock = () => start.AddDays(f.Settings.Messaging.LapsedRetentionDays - 1);
            await f.Services.Messaging.Maintenance.LapsePassAsync(ct).ConfigureAwait(false);
            Check.Equal(0L, await MessagingTestKit.CountAsync(f, "conversationmembers", "userid = '" + b.Id + "' AND deletedutc IS NOT NULL", ct).ConfigureAwait(false), "Kept inside the retention window");
            f.Services.Messaging.Clock = () => start.AddDays(f.Settings.Messaging.LapsedRetentionDays + 1);
            await f.Services.Messaging.Maintenance.LapsePassAsync(ct).ConfigureAwait(false);
            Check.Equal(1L, await MessagingTestKit.CountAsync(f, "conversationmembers", "userid = '" + b.Id + "' AND deletedutc IS NOT NULL", ct).ConfigureAwait(false), "Removed for the lapsed member");
            Check.Equal(0L, await MessagingTestKit.CountAsync(f, "messagingcontacts", "userid = '" + b.Id + "'", ct).ConfigureAwait(false), "Contacts cleared");
            Check.Equal(2, MessagingTestKit.Seqs(await MessagingTestKit.HistoryAsync(f, a, id, null, ct).ConfigureAwait(false)).Count, "The other member's history is untouched");
            await Api.GrantCompAsync(f, b.Id, ct).ConfigureAwait(false);
            f.Services.Tokens.InvalidateUser(b.Id);
            Check.True(!(await MessagingTestKit.ListIdsAsync(f, b, null, ct).ConfigureAwait(false)).Contains(id), "Gone for the member after rejoining late");
        }

        private static async Task OpenAccessAsync(HttpFixture f, CancellationToken ct)
        {
            TestMember a = await Api.RegisterAsync(f, ct).ConfigureAwait(false);
            TestMember b = await Api.RegisterAsync(f, ct).ConfigureAwait(false);
            string id = await MessagingTestKit.StartIdAsync(f, a, b.Id, "no membership needed", ct).ConfigureAwait(false);
            await MessagingTestKit.SendOkAsync(f, b, id, "reply", ct).ConfigureAwait(false);
            await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/me/messaging/unread-count", a.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal(2, MessagingTestKit.Seqs(await MessagingTestKit.HistoryAsync(f, a, id, null, ct).ConfigureAwait(false)).Count, "History");
            await Api.ExpectErrorAsync(f, HttpMethod.Get, MessagingTestKit.Conversations, null, null, HttpStatusCode.Unauthorized, "Unauthorized", ct).ConfigureAwait(false);
        }

        #endregion
    }
}
