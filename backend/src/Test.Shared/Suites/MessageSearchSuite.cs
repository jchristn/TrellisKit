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
    /// Phase 5 member search over their own direct messages (as-built note B): only the caller's conversations, never
    /// hidden ranges, tombstones, removed messages, conversations with blocked members, or other members' conversations;
    /// bounded pages, relevance and created orderings, conversation and member filters, headlines, and validation.
    /// </summary>
    public static class MessageSearchSuite
    {
        #region Public-Members

        /// <summary>
        /// Suite identifier.
        /// </summary>
        public const string Id = "MessageSearch";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the suite.
        /// </summary>
        /// <returns>Suite.</returns>
        public static TestSuiteDescriptor Build()
        {
            return new TestSuiteDescriptor(Id, "Direct message search (" + TestEnvironment.Provider + ")", new[]
            {
                Case("Search.OwnConversationsOnly", "Search covers the caller's conversations and nothing else; hidden, deleted, removed, and blocked content never matches", async (f, ct) =>
                {
                    TestMember a = await MessagingTestKit.MemberAsync(f, ct, "Ann Seeker").ConfigureAwait(false);
                    TestMember b = await MessagingTestKit.MemberAsync(f, ct, "Bob Partner").ConfigureAwait(false);
                    TestMember c = await MessagingTestKit.MemberAsync(f, ct, "Cat Third").ConfigureAwait(false);
                    TestMember d = await MessagingTestKit.MemberAsync(f, ct, "Dan Fourth").ConfigureAwait(false);
                    string ab = await MessagingTestKit.PairAsync(f, a, b, ct).ConfigureAwait(false);
                    string ac = await MessagingTestKit.PairAsync(f, a, c, ct).ConfigureAwait(false);
                    string cd = await MessagingTestKit.PairAsync(f, c, d, ct).ConfigureAwait(false);
                    await MessagingTestKit.SendOkAsync(f, b, ab, "pineapple pizza tonight", ct).ConfigureAwait(false);
                    await MessagingTestKit.SendOkAsync(f, c, ac, "the pineapple is ripe", ct).ConfigureAwait(false);
                    await MessagingTestKit.SendOkAsync(f, d, cd, "pineapple secret between others", ct).ConfigureAwait(false);
                    JsonElement removable = await MessagingTestKit.SendOkAsync(f, b, ab, "pineapple to be deleted", ct).ConfigureAwait(false);
                    await Api.ExpectAsync(f, HttpMethod.Delete, "/api/v1/messages/" + Api.Str(removable, "id"), b.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    JsonElement page = await Search(f, a, new { search = "pineapple" }, ct).ConfigureAwait(false);
                    List<string> conversations = page.GetProperty("objects").EnumerateArray().Select(o => Api.Str(o, "conversationId")).ToList();
                    Check.Equal(2, conversations.Count, "Two live matches in the caller's conversations");
                    Check.True(!conversations.Contains(cd), "Never other members' conversations");
                    Check.True(!page.ToString().Contains("between others", StringComparison.Ordinal), "No foreign text");
                    JsonElement hit = page.GetProperty("objects")[0];
                    Check.True(Api.Str(hit, "headline").Contains("**pineapple**", StringComparison.OrdinalIgnoreCase), "Headline marks the match: " + Api.Str(hit, "headline"));
                    Check.Equal(JsonValueKind.Object, hit.GetProperty("otherMember").ValueKind, "otherMember");
                    Check.Equal(JsonValueKind.Object, hit.GetProperty("message").ValueKind, "message");

                    JsonElement filtered = await Search(f, a, new { search = "pineapple", filters = new { conversationId = ac } }, ct).ConfigureAwait(false);
                    Check.Equal(1, filtered.GetProperty("objects").GetArrayLength(), "conversationId filter");
                    JsonElement withB = await Search(f, a, new { search = "pineapple", filters = new { withUserId = b.Id } }, ct).ConfigureAwait(false);
                    Check.Equal(ab, Api.Str(withB.GetProperty("objects")[0], "conversationId"), "withUserId filter");
                    JsonElement created = await Search(f, a, new { search = "pineapple", ordering = "CreatedAscending" }, ct).ConfigureAwait(false);
                    Check.Equal(ab, Api.Str(created.GetProperty("objects")[0], "conversationId"), "CreatedAscending: the oldest first");
                    JsonElement paged = await Search(f, a, new { search = "pineapple", maxResults = 1 }, ct).ConfigureAwait(false);
                    Check.Equal(1, paged.GetProperty("objects").GetArrayLength(), "Bounded page");
                    JsonElement next = await Search(f, a, new { search = "pineapple", maxResults = 1, continuationToken = paged.GetProperty("continuationToken").GetString() }, ct).ConfigureAwait(false);
                    Check.True(Api.Str(next.GetProperty("objects")[0], "conversationId") != Api.Str(paged.GetProperty("objects")[0], "conversationId"), "Continuation gives the next hit");
                    JsonElement big = await Search(f, a, new { search = "pineapple", maxResults = 5000 }, ct).ConfigureAwait(false);
                    Check.True(big.GetProperty("maxResults").GetInt32() <= f.Settings.Messaging.SearchMaxResults, "Clamped to messaging.searchMaxResults");

                    await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/users/" + c.Id + "/block", a.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.Equal(1, (await Search(f, a, new { search = "pineapple" }, ct).ConfigureAwait(false)).GetProperty("objects").GetArrayLength(), "Blocked member's conversation excluded");
                    await Api.ExpectAsync(f, HttpMethod.Delete, "/api/v1/users/" + c.Id + "/block", a.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.Equal(HttpStatusCode.NoContent, (await Api.SendAsync(f, HttpMethod.Delete, MessagingTestKit.Conversations + "/" + ac, a.AccessToken, null, ct).ConfigureAwait(false)).Status, "Delete for me");
                    Check.Equal(1, (await Search(f, a, new { search = "pineapple" }, ct).ConfigureAwait(false)).GetProperty("objects").GetArrayLength(), "Hidden range excluded");
                    Check.Equal(2, (await Search(f, c, new { search = "pineapple" }, ct).ConfigureAwait(false)).GetProperty("objects").GetArrayLength(), "The other member still finds their copy");
                }),
                Case("Search.Validation", "Search needs 2 to 200 characters and validates orderings and continuation tokens; the admin token cannot search", async (f, ct) =>
                {
                    TestMember a = await MessagingTestKit.MemberAsync(f, ct).ConfigureAwait(false);
                    await Api.ExpectErrorAsync(f, HttpMethod.Post, "/api/v1/messages/search/enumerate", a.AccessToken, new { }, HttpStatusCode.BadRequest, "ValidationFailed", ct).ConfigureAwait(false);
                    await Api.ExpectErrorAsync(f, HttpMethod.Post, "/api/v1/messages/search/enumerate", a.AccessToken, new { search = "a" }, HttpStatusCode.BadRequest, "ValidationFailed", ct).ConfigureAwait(false);
                    await Api.ExpectErrorAsync(f, HttpMethod.Post, "/api/v1/messages/search/enumerate", a.AccessToken, new { search = new string('x', 201) }, HttpStatusCode.BadRequest, "ValidationFailed", ct).ConfigureAwait(false);
                    await Api.ExpectErrorAsync(f, HttpMethod.Post, "/api/v1/messages/search/enumerate", a.AccessToken, new { search = "hello", ordering = "Sideways" }, HttpStatusCode.BadRequest, "ValidationFailed", ct).ConfigureAwait(false);
                    await Api.ExpectErrorAsync(f, HttpMethod.Post, "/api/v1/messages/search/enumerate", a.AccessToken, new { search = "hello", continuationToken = "garbage" }, HttpStatusCode.BadRequest, "InvalidContinuationToken", ct).ConfigureAwait(false);
                    await Api.ExpectErrorAsync(f, HttpMethod.Post, "/api/v1/messages/search/enumerate", HttpFixture.AdminToken, new { search = "hello" }, HttpStatusCode.Forbidden, "Forbidden", ct).ConfigureAwait(false);
                    JsonElement empty = await Search(f, a, new { search = "nothing here" }, ct).ConfigureAwait(false);
                    Check.Equal(0, empty.GetProperty("objects").GetArrayLength(), "No hits");
                })
            });
        }

        #endregion

        #region Private-Methods

        private static Task<JsonElement> Search(HttpFixture f, TestMember member, object query, CancellationToken ct)
        {
            return Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/messages/search/enumerate", member.AccessToken, query, HttpStatusCode.OK, ct);
        }

        private static TestCaseDescriptor Case(string caseId, string name, Func<HttpFixture, CancellationToken, Task> body)
        {
            return SuiteCases.Http(Id, caseId, name, body);
        }

        #endregion
    }
}
