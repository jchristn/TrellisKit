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
    /// Phase 5 conversation history (docs/PHASE5_CONTRACT.md section 3.4 and as-built note B): keyset continuation over
    /// seq, beforeSeq, afterSeq (gap fill), aroundSeq, the page bounds, hidden ranges after delete for me, tombstones kept
    /// for gap detection, history search, and delivery positions moved by REST reads.
    /// </summary>
    public static class MessageHistorySuite
    {
        #region Public-Members

        /// <summary>
        /// Suite identifier.
        /// </summary>
        public const string Id = "MessageHistory";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the suite.
        /// </summary>
        /// <returns>Suite.</returns>
        public static TestSuiteDescriptor Build()
        {
            return new TestSuiteDescriptor(Id, "Direct message history (" + TestEnvironment.Provider + ")", new[]
            {
                Case("History.PagingAndBounds", "SeqDescending pages with continuation, SeqAscending with afterSeq, beforeSeq, aroundSeq with bounded cursor pages, the GET form, and clamped page sizes", async (f, ct) =>
                {
                    TestMember a = await MessagingTestKit.MemberAsync(f, ct).ConfigureAwait(false);
                    TestMember b = await MessagingTestKit.MemberAsync(f, ct).ConfigureAwait(false);
                    string id = await MessagingTestKit.PairAsync(f, a, b, ct).ConfigureAwait(false);
                    for (int i = 3; i <= 9; i++) await MessagingTestKit.SendOkAsync(f, i % 2 == 0 ? a : b, id, "message " + i, ct).ConfigureAwait(false);
                    List<long> seen = new List<long>();
                    string? token = null;
                    int pages = 0;
                    do
                    {
                        JsonElement page = await MessagingTestKit.HistoryAsync(f, a, id, token == null ? (object)new { maxResults = 4 } : new { maxResults = 4, continuationToken = token }, ct).ConfigureAwait(false);
                        seen.AddRange(MessagingTestKit.Seqs(page));
                        token = page.GetProperty("continuationToken").ValueKind == JsonValueKind.String ? page.GetProperty("continuationToken").GetString() : null;
                        pages++;
                    }
                    while (token != null && pages < 10);
                    Check.Equal("9,8,7,6,5,4,3,2,1", String.Join(",", seen), "Newest first without gaps or duplicates");
                    Check.Equal(3, pages, "Three pages");
                    Check.Equal("6,7,8,9", String.Join(",", MessagingTestKit.Seqs(await MessagingTestKit.HistoryAsync(f, a, id, new { ordering = "SeqAscending", filters = new { afterSeq = 5 } }, ct).ConfigureAwait(false))), "afterSeq gap fill");
                    Check.Equal("3,2,1", String.Join(",", MessagingTestKit.Seqs(await MessagingTestKit.HistoryAsync(f, a, id, new { filters = new { beforeSeq = 4 } }, ct).ConfigureAwait(false))), "beforeSeq");
                    JsonElement around = await MessagingTestKit.HistoryAsync(f, a, id, new { maxResults = 1, filters = new { aroundSeq = 5 } }, ct).ConfigureAwait(false);
                    Check.Equal("9", String.Join(",", MessagingTestKit.Seqs(around)), "aroundSeq honors page cap and default ordering");
                    string aroundToken = Api.Str(around, "continuationToken");
                    JsonElement aroundNext = await MessagingTestKit.HistoryAsync(f, a, id, new { maxResults = 2, continuationToken = aroundToken, filters = new { aroundSeq = 5 } }, ct).ConfigureAwait(false);
                    Check.Equal("8,7", String.Join(",", MessagingTestKit.Seqs(aroundNext)), "Neighborhood continues without duplicates");
                    JsonElement aroundSkip = await MessagingTestKit.HistoryAsync(f, a, id, new { maxResults = 2, skip = 4, ordering = "SeqAscending", filters = new { aroundSeq = 5 } }, ct).ConfigureAwait(false);
                    Check.Equal("5,6", String.Join(",", MessagingTestKit.Seqs(aroundSkip)), "Neighborhood honors skip and ascending ordering");
                    await Api.ExpectErrorAsync(f, HttpMethod.Post, MessagingTestKit.Conversations + "/" + id + "/messages/enumerate", a.AccessToken, new { continuationToken = aroundToken, filters = new { aroundSeq = 6 } }, HttpStatusCode.BadRequest, "InvalidContinuationToken", ct).ConfigureAwait(false);
                    await Api.ExpectErrorAsync(f, HttpMethod.Post, MessagingTestKit.Conversations + "/" + id + "/messages/enumerate", a.AccessToken, new { filters = new { aroundSeq = 5, beforeSeq = 4 } }, HttpStatusCode.BadRequest, "ValidationFailed", ct).ConfigureAwait(false);
                    JsonElement get = await Api.ExpectAsync(f, HttpMethod.Get, MessagingTestKit.Conversations + "/" + id + "/messages?maxResults=2&ordering=SeqAscending", a.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.Equal("1,2", String.Join(",", MessagingTestKit.Seqs(get)), "GET form");
                    JsonElement clamped = await MessagingTestKit.HistoryAsync(f, a, id, new { maxResults = 5000 }, ct).ConfigureAwait(false);
                    Check.True(clamped.GetProperty("maxResults").GetInt32() <= 100, "Clamped to 100");
                    JsonElement defaults = await MessagingTestKit.HistoryAsync(f, a, id, null, ct).ConfigureAwait(false);
                    Check.Equal(50, defaults.GetProperty("maxResults").GetInt32(), "Default 50");
                    await Api.ExpectErrorAsync(f, HttpMethod.Post, MessagingTestKit.Conversations + "/" + id + "/messages/enumerate", a.AccessToken, new { ordering = "Sideways" }, HttpStatusCode.BadRequest, "ValidationFailed", ct).ConfigureAwait(false);
                    await Api.ExpectErrorAsync(f, HttpMethod.Post, MessagingTestKit.Conversations + "/" + id + "/messages/enumerate", a.AccessToken, new { continuationToken = "garbage" }, HttpStatusCode.BadRequest, "InvalidContinuationToken", ct).ConfigureAwait(false);
                    TestMember outsider = await MessagingTestKit.MemberAsync(f, ct).ConfigureAwait(false);
                    await Api.ExpectErrorAsync(f, HttpMethod.Post, MessagingTestKit.Conversations + "/" + id + "/messages/enumerate", outsider.AccessToken, new { }, HttpStatusCode.NotFound, "NotFound", ct).ConfigureAwait(false);
                }),
                Case("History.HiddenRangesTombstonesDelivery", "Delete for me hides earlier messages for the caller only; tombstones stay in history; reading history moves the delivery position", async (f, ct) =>
                {
                    TestMember a = await MessagingTestKit.MemberAsync(f, ct).ConfigureAwait(false);
                    TestMember b = await MessagingTestKit.MemberAsync(f, ct).ConfigureAwait(false);
                    string id = await MessagingTestKit.PairAsync(f, a, b, ct).ConfigureAwait(false);
                    JsonElement third = await MessagingTestKit.SendOkAsync(f, a, id, "three", ct).ConfigureAwait(false);
                    await Api.ExpectAsync(f, HttpMethod.Delete, "/api/v1/messages/" + Api.Str(third, "id"), a.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    await MessagingTestKit.SendOkAsync(f, a, id, "four", ct).ConfigureAwait(false);
                    JsonElement before = await Api.ExpectAsync(f, HttpMethod.Get, MessagingTestKit.Conversations + "/" + id, b.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.True(before.GetProperty("myLastDeliveredSeq").GetInt64() < 4, "Not delivered yet");
                    JsonElement all = await MessagingTestKit.HistoryAsync(f, b, id, new { ordering = "SeqAscending" }, ct).ConfigureAwait(false);
                    Check.Equal("1,2,3,4", String.Join(",", MessagingTestKit.Seqs(all)), "Tombstone included");
                    Check.Equal("Deleted", Api.Str(all.GetProperty("objects")[2], "state"), "Tombstone state");
                    JsonElement after = await Api.ExpectAsync(f, HttpMethod.Get, MessagingTestKit.Conversations + "/" + id, b.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.Equal(4L, after.GetProperty("myLastDeliveredSeq").GetInt64(), "Delivered by the REST read");
                    Check.Equal(HttpStatusCode.NoContent, (await Api.SendAsync(f, HttpMethod.Delete, MessagingTestKit.Conversations + "/" + id, b.AccessToken, null, ct).ConfigureAwait(false)).Status, "Delete for me");
                    await MessagingTestKit.SendOkAsync(f, a, id, "five", ct).ConfigureAwait(false);
                    await MessagingTestKit.SendOkAsync(f, a, id, "six", ct).ConfigureAwait(false);
                    Check.Equal("5,6", String.Join(",", MessagingTestKit.Seqs(await MessagingTestKit.HistoryAsync(f, b, id, new { ordering = "SeqAscending" }, ct).ConfigureAwait(false))), "Hidden range excluded");
                    Check.Equal("5,6", String.Join(",", MessagingTestKit.Seqs(await MessagingTestKit.HistoryAsync(f, b, id, new { ordering = "SeqAscending", filters = new { afterSeq = 0 } }, ct).ConfigureAwait(false))), "Also with afterSeq");
                    Check.Equal(6, MessagingTestKit.Seqs(await MessagingTestKit.HistoryAsync(f, a, id, null, ct).ConfigureAwait(false)).Count, "The other member keeps everything");
                    JsonElement first = await MessagingTestKit.HistoryAsync(f, a, id, new { ordering = "SeqAscending", maxResults = 1 }, ct).ConfigureAwait(false);
                    await Api.ExpectErrorAsync(f, HttpMethod.Put, "/api/v1/messages/" + Api.Str(first.GetProperty("objects")[0], "id") + "/reactions/like", b.AccessToken, null, HttpStatusCode.NotFound, "NotFound", ct).ConfigureAwait(false);
                }),
                Case("History.Search", "History search uses full text over the conversation, excludes tombstones, and refuses aroundSeq with search and one-character searches", async (f, ct) =>
                {
                    TestMember a = await MessagingTestKit.MemberAsync(f, ct).ConfigureAwait(false);
                    TestMember b = await MessagingTestKit.MemberAsync(f, ct).ConfigureAwait(false);
                    string id = await MessagingTestKit.PairAsync(f, a, b, ct).ConfigureAwait(false);
                    await MessagingTestKit.SendOkAsync(f, a, id, "The tournament is on Saturday", ct).ConfigureAwait(false);
                    JsonElement gone = await MessagingTestKit.SendOkAsync(f, a, id, "Another tournament note", ct).ConfigureAwait(false);
                    await MessagingTestKit.SendOkAsync(f, b, id, "Nothing relevant", ct).ConfigureAwait(false);
                    await Api.ExpectAsync(f, HttpMethod.Delete, "/api/v1/messages/" + Api.Str(gone, "id"), a.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    JsonElement hits = await MessagingTestKit.HistoryAsync(f, b, id, new { search = "tournament" }, ct).ConfigureAwait(false);
                    Check.Equal("3", String.Join(",", MessagingTestKit.Seqs(hits)), "One live match");
                    await Api.ExpectErrorAsync(f, HttpMethod.Post, MessagingTestKit.Conversations + "/" + id + "/messages/enumerate", b.AccessToken, new { search = "tournament", filters = new { aroundSeq = 2 } }, HttpStatusCode.BadRequest, "ValidationFailed", ct).ConfigureAwait(false);
                    await Api.ExpectErrorAsync(f, HttpMethod.Post, MessagingTestKit.Conversations + "/" + id + "/messages/enumerate", b.AccessToken, new { search = "t" }, HttpStatusCode.BadRequest, "ValidationFailed", ct).ConfigureAwait(false);
                })
            });
        }

        #endregion

        #region Private-Methods

        private static TestCaseDescriptor Case(string caseId, string name, Func<HttpFixture, CancellationToken, Task> body)
        {
            return SuiteCases.Http(Id, caseId, name, body);
        }

        #endregion
    }
}
