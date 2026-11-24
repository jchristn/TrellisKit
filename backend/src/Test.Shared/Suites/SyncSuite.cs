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

    /// <summary>
    /// Phase 5 conversation sync (docs/PHASE5_CONTRACT.md section 5.2): first sync, separate sync watermarks and page cursors, changes
    /// after a token (new messages, receipts, folders, removals), blocks surfacing as Removed for the blocker only, token
    /// expiry after messaging.syncTokenDays, and malformed tokens.
    /// </summary>
    public static class SyncSuite
    {
        #region Public-Members

        /// <summary>
        /// Suite identifier.
        /// </summary>
        public const string Id = "MessageSync";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the suite.
        /// </summary>
        /// <returns>Suite.</returns>
        public static TestSuiteDescriptor Build()
        {
            return new TestSuiteDescriptor(Id, "Conversation sync (" + TestEnvironment.Provider + ")", new[]
            {
                Case("Sync.ChangesTokensRemovals", "A null token returns every conversation; the final token returns only later changes; deletes and blocks are Removed for the caller only", async (f, ct) =>
                {
                    TestMember a = await MessagingTestKit.MemberAsync(f, ct).ConfigureAwait(false);
                    TestMember b = await MessagingTestKit.MemberAsync(f, ct).ConfigureAwait(false);
                    TestMember c = await MessagingTestKit.MemberAsync(f, ct).ConfigureAwait(false);
                    string ab = await MessagingTestKit.PairAsync(f, a, b, ct).ConfigureAwait(false);
                    string ac = await MessagingTestKit.PairAsync(f, a, c, ct).ConfigureAwait(false);
                    List<JsonElement> first = new List<JsonElement>();
                    string token = await DrainAsync(f, a, null, first, 1, ct).ConfigureAwait(false);
                    Check.True(first.Select(i => Api.Str(i.GetProperty("conversation"), "id")).Distinct().OrderBy(i => i).SequenceEqual(new[] { ab, ac }.OrderBy(i => i)), "First sync returns the conversations");
                    Check.True(first.All(i => Api.Str(i, "change") == "Updated"), "Updated");
                    List<JsonElement> none = new List<JsonElement>();
                    string same = await DrainAsync(f, a, token, none, 100, ct).ConfigureAwait(false);
                    Check.Equal(0, none.Count, "No changes");
                    await Task.Delay(1100, ct).ConfigureAwait(false);
                    f.Services.Messaging.Clock = () => DateTime.UtcNow.AddMinutes(2);
                    await MessagingTestKit.SendOkAsync(f, b, ab, "new", ct).ConfigureAwait(false);
                    List<JsonElement> changes = new List<JsonElement>();
                    string next = await DrainAsync(f, a, same, changes, 100, ct).ConfigureAwait(false);
                    Check.Equal(ab, String.Join(",", changes.Select(i => Api.Str(i.GetProperty("conversation"), "id"))), "Only the changed conversation");
                    Check.Equal(3L, changes[0].GetProperty("conversation").GetProperty("lastSeq").GetInt64(), "Current state");

                    f.Services.Messaging.Clock = () => DateTime.UtcNow.AddMinutes(4);
                    Check.Equal(HttpStatusCode.NoContent, (await Api.SendAsync(f, HttpMethod.Delete, MessagingTestKit.Conversations + "/" + ac, a.AccessToken, null, ct).ConfigureAwait(false)).Status, "Delete for me");
                    await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/users/" + b.Id + "/block", a.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    List<JsonElement> removed = new List<JsonElement>();
                    await DrainAsync(f, a, next, removed, 100, ct).ConfigureAwait(false);
                    Check.True(removed.Count == 2 && removed.All(i => Api.Str(i, "change") == "Removed"), "Deleted and blocked conversations are Removed: " + String.Join(",", removed.Select(i => Api.Str(i, "change"))));
                    List<JsonElement> theirs = new List<JsonElement>();
                    await DrainAsync(f, b, null, theirs, 100, ct).ConfigureAwait(false);
                    Check.True(theirs.All(i => Api.Str(i, "change") == "Updated"), "The blocked member never sees a removal");
                }),
                Case("Sync.TokenExpiryAndValidation", "Tokens older than messaging.syncTokenDays and malformed tokens return 400 InvalidContinuationToken; tokens are bound to the caller", async (f, ct) =>
                {
                    TestMember a = await MessagingTestKit.MemberAsync(f, ct).ConfigureAwait(false);
                    TestMember b = await MessagingTestKit.MemberAsync(f, ct).ConfigureAwait(false);
                    await MessagingTestKit.PairAsync(f, a, b, ct).ConfigureAwait(false);
                    string token = await DrainAsync(f, a, null, new List<JsonElement>(), 100, ct).ConfigureAwait(false);
                    await Api.ExpectErrorAsync(f, HttpMethod.Post, MessagingTestKit.Conversations + "/changes/enumerate", a.AccessToken, new { continuationToken = "garbage" }, HttpStatusCode.BadRequest, "InvalidContinuationToken", ct).ConfigureAwait(false);
                    await Api.ExpectErrorAsync(f, HttpMethod.Post, MessagingTestKit.Conversations + "/changes/enumerate", b.AccessToken, new { syncToken = token }, HttpStatusCode.BadRequest, "InvalidContinuationToken", ct).ConfigureAwait(false);
                    f.Services.Messaging.Clock = () => DateTime.UtcNow.AddDays(f.Settings.Messaging.SyncTokenDays + 1);
                    await Api.ExpectErrorAsync(f, HttpMethod.Post, MessagingTestKit.Conversations + "/changes/enumerate", a.AccessToken, new { syncToken = token }, HttpStatusCode.BadRequest, "InvalidContinuationToken", ct).ConfigureAwait(false);
                    f.Services.Messaging.Clock = () => DateTime.UtcNow;
                    JsonElement fresh = await Api.ExpectAsync(f, HttpMethod.Post, MessagingTestKit.Conversations + "/changes/enumerate", a.AccessToken, new { syncToken = token }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.Equal(JsonValueKind.Null, fresh.GetProperty("continuationToken").ValueKind, "Terminal page has no continuation");
                    Check.Equal(JsonValueKind.String, fresh.GetProperty("syncToken").ValueKind, "Terminal page has next sync watermark");
                })
            });
        }

        #endregion

        #region Private-Methods

        private static async Task<string> DrainAsync(HttpFixture f, TestMember member, string? token, List<JsonElement> items, int pageSize, CancellationToken ct)
        {
            string? current = null;
            for (int i = 0; i < 20; i++)
            {
                JsonElement page = await Api.ExpectAsync(f, HttpMethod.Post, MessagingTestKit.Conversations + "/changes/enumerate", member.AccessToken,
                    new { maxResults = pageSize, syncToken = token, continuationToken = current }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                items.AddRange(page.GetProperty("objects").EnumerateArray());
                current = page.GetProperty("continuationToken").GetString();
                if (page.GetProperty("endOfResults").GetBoolean())
                {
                    Check.True(current == null, "Terminal enumeration cursor is null");
                    return Api.Str(page, "syncToken");
                }
                Check.True(current != null, "Intermediate page has continuation");
                Check.Equal(JsonValueKind.Null, page.GetProperty("syncToken").ValueKind, "Intermediate page has no final watermark");
            }
            return current ?? String.Empty;
        }

        private static TestCaseDescriptor Case(string caseId, string name, Func<HttpFixture, CancellationToken, Task> body)
        {
            return SuiteCases.Http(Id, caseId, name, body);
        }

        #endregion
    }
}
