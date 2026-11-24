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
    using TrellisKit.Core.Helpers;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Models;

    /// <summary>
    /// Phase 2 member search (contract section 3.4): handle and display name word prefixes, the ranking including the
    /// feed post context, every exclusion, validation and clamping, rate limiting, and displaynamewords maintenance.
    /// </summary>
    public static class MemberSearchSuite
    {
        #region Public-Members

        /// <summary>
        /// Suite identifier.
        /// </summary>
        public const string Id = "MemberSearch";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the suite.
        /// </summary>
        /// <returns>Suite.</returns>
        public static TestSuiteDescriptor Build()
        {
            return new TestSuiteDescriptor(Id, "Member search (" + TestEnvironment.Provider + ")", new[]
            {
                SuiteCases.Unit(Id, "Words.Split", "Display names split into distinct lowercase words on whitespace and punctuation, cut to 30", ct =>
                {
                    Check.Equal("alex,o,brien,chen", String.Join(",", DisplayNameWords.Split("  Alex O'Brien-Chen alex ")), "Split and dedupe");
                    Check.Equal("josé", String.Join(",", DisplayNameWords.Split("José")), "NFC");
                    Check.Equal(30, DisplayNameWords.Split(new string('a', 40)).Single().Length, "Cut to 30");
                    Check.Equal(0, DisplayNameWords.Split("!!! ...").Count, "No words");
                    Check.Equal("ann", String.Join(",", DisplayNameWords.Split("Ann 😀")), "Emoji is not a word");
                    return Task.CompletedTask;
                }),
                Case("Search.Ranking", "Exact handle, then context author and commenters, then handle prefix, then display name words", async (f, ct) =>
                {
                    TestMember caller = await SafetyHelpers.MemberAsync(f, ct, "Caller Person", "alexcaller").ConfigureAwait(false);
                    TestMember exact = await SafetyHelpers.MemberAsync(f, ct, "Zeta Person", "alex").ConfigureAwait(false);
                    TestMember prefixB = await SafetyHelpers.MemberAsync(f, ct, "Yolanda", "alexb").ConfigureAwait(false);
                    TestMember prefixC = await SafetyHelpers.MemberAsync(f, ct, "Ann", "alexc").ConfigureAwait(false);
                    TestMember word = await SafetyHelpers.MemberAsync(f, ct, "Mary Alexis Zed", "zz1").ConfigureAwait(false);
                    await SafetyHelpers.MemberAsync(f, ct, "Unrelated", "bob").ConfigureAwait(false);
                    Check.Equal(Ids(exact, prefixB, prefixC, word), Join(await SearchAsync(f, caller, "?search=alex", ct).ConfigureAwait(false)), "Ranking without context");
                    Check.Equal(Ids(exact, prefixB, prefixC, word), Join(await SearchAsync(f, caller, "?q=@ALEX", ct).ConfigureAwait(false)), "q alias, leading @, case-insensitive");
                    Check.Equal(Ids(word), Join(await SearchAsync(f, caller, "?search=zed", ct).ConfigureAwait(false)), "Any display name word");
                    Check.Equal(Ids(word), Join(await SearchAsync(f, caller, "?search=ma", ct).ConfigureAwait(false)), "First word prefix");

                    string post = await SafetyHelpers.InsertFeedPostAsync(f, word.Id, null, ct).ConfigureAwait(false);
                    await SafetyHelpers.InsertCommentAsync(f, post, prefixC.Id, null, ct).ConfigureAwait(false);
                    string otherPost = await SafetyHelpers.InsertFeedPostAsync(f, prefixB.Id, null, ct).ConfigureAwait(false);
                    Check.Equal(Ids(exact, prefixC, word, prefixB), Join(await SearchAsync(f, caller, "?search=alex&contextType=FeedPost&contextId=" + post, ct).ConfigureAwait(false)),
                        "Context members rank after the exact handle, alphabetical by display name");
                    JsonElement body = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/members/enumerate", caller.AccessToken,
                        new { search = "alex", filters = new { contextType = "FeedPost", contextId = otherPost } }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.Equal(Ids(exact, prefixB, prefixC, word), Join(Objects(body)), "POST enumerate with filters");
                    JsonElement first = body.GetProperty("objects")[0];
                    Check.Equal("alex", Api.Str(first, "handle"), "UserSummary handle");
                    Check.True(first.TryGetProperty("isStaff", out JsonElement _), "UserSummary shape");
                    Check.True(body.GetProperty("endOfResults").GetBoolean(), "One page");
                    JsonElement alias = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/members/search?q=alex", caller.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.Equal(4, alias.GetProperty("objects").GetArrayLength(), "/members/search alias");
                }),
                Case("Search.Enumeration", "Member prefix search pages ranked SQL results, binds cursors to prefix/caller/context, and honors ordering, metadata and exact staff totals", async (f, ct) =>
                {
                    TestMember caller = await SafetyHelpers.MemberAsync(f, ct, "Caller").ConfigureAwait(false);
                    List<TestMember> members = new List<TestMember>();
                    for (int index = 0; index < 9; index++) members.Add(await SafetyHelpers.MemberAsync(f, ct, "Window Person " + index.ToString("D2"), "window" + index.ToString("D2")).ConfigureAwait(false));
                    HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
                    List<string> ranked = new List<string>();
                    string? cursor = null;
                    string firstCursor = String.Empty;
                    do
                    {
                        JsonElement page = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/members/enumerate", caller.AccessToken, new { search = "window", maxResults = 2, continuationToken = cursor }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                        Check.Equal(9L, page.GetProperty("totalRecords").GetInt64(), "Exact total on every page");
                        Check.True(page.GetProperty("objects").GetArrayLength() <= 2, "Page cap enforced");
                        foreach (JsonElement row in page.GetProperty("objects").EnumerateArray())
                        {
                            string id = Api.Str(row, "id");
                            Check.True(seen.Add(id), "No duplicate member across ranked pages");
                            ranked.Add(id);
                            ContentTestKit.CheckMetadata(row, "Search member");
                        }
                        Check.Equal(9L - ranked.Count, page.GetProperty("recordsRemaining").GetInt64(), "Remaining matches count the suffix");
                        cursor = page.GetProperty("continuationToken").ValueKind == JsonValueKind.String ? page.GetProperty("continuationToken").GetString() : null;
                        if (firstCursor.Length == 0 && cursor != null) firstCursor = cursor;
                    } while (cursor != null);
                    Check.Equal(String.Join(",", members.Select(member => member.Id)), String.Join(",", ranked), "Every ranked match reachable in handle order");
                    JsonElement skipped = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/members/enumerate", caller.AccessToken, new { search = "window", maxResults = 2, skip = 3 }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.Equal(String.Join(",", ranked.Skip(3).Take(2)), Join(Objects(skipped)), "Skip uses the same ranking");
                    JsonElement byCreated = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/members/enumerate", caller.AccessToken, new { search = "window", ordering = "CreatedDescending", maxResults = 20 }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.Equal(String.Join(",", members.AsEnumerable().Reverse().Select(member => member.Id)), Join(Objects(byCreated)), "Explicit creation ordering applies");
                    await Api.ExpectErrorAsync(f, HttpMethod.Post, "/api/v1/members/enumerate", caller.AccessToken, new { search = "wind", continuationToken = firstCursor }, HttpStatusCode.BadRequest, "InvalidContinuationToken", ct).ConfigureAwait(false);
                    await Api.ExpectErrorAsync(f, HttpMethod.Post, "/api/v1/members/enumerate", members[0].AccessToken, new { search = "window", continuationToken = firstCursor }, HttpStatusCode.BadRequest, "InvalidContinuationToken", ct).ConfigureAwait(false);
                    await Api.ExpectErrorAsync(f, HttpMethod.Post, "/api/v1/members/enumerate", caller.AccessToken, new { search = "window", continuationToken = firstCursor, filters = new { contextType = "FeedPost", contextId = "fpo_other" } }, HttpStatusCode.BadRequest, "InvalidContinuationToken", ct).ConfigureAwait(false);
                    await f.Database.Metadata.WriteAsync(TargetTypeEnum.User, members[0].Id, new ObjectMetadata { Labels = new List<string> { "eligible", "staff:hidden" }, Tags = new Dictionary<string, string> { ["region"] = "west", ["staff:note"] = "internal" } }, ct).ConfigureAwait(false);
                    JsonElement filtered = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/members/enumerate", caller.AccessToken, new { search = "window", labels = new[] { "eligible" }, tags = new { region = "west" } }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.Equal(Ids(members[0]), Join(Objects(filtered)), "Metadata filters apply to source members");
                    Check.True(!filtered.GetRawText().Contains("staff:hidden", StringComparison.Ordinal) && !filtered.GetRawText().Contains("internal", StringComparison.Ordinal), "Staff metadata stays hidden");
                    JsonElement future = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/members/enumerate", caller.AccessToken, new { search = "window", createdAfterUtc = DateTime.UtcNow.AddDays(1) }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.Equal(0L, future.GetProperty("totalRecords").GetInt64(), "Created window filters source members");
                    f.Settings.Enumeration.CountMemberTotals = false;
                    JsonElement memberPage = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/members/enumerate", caller.AccessToken, new { search = "window", maxResults = 2 }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.Equal(JsonValueKind.Null, memberPage.GetProperty("totalRecords").ValueKind, "Member totals setting respected");
                    JsonElement staffPage = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/members/enumerate", HttpFixture.AdminToken, new { search = "window", maxResults = 2 }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.Equal(9L, staffPage.GetProperty("totalRecords").GetInt64(), "Staff totals remain exact");
                }),
                Case("Search.Exclusions", "Caller, deleted, banned, unattested, non-members, and both block directions are excluded", async (f, ct) =>
                {
                    TestMember caller = await SafetyHelpers.MemberAsync(f, ct, "Caller", "pickme0").ConfigureAwait(false);
                    TestMember ok = await SafetyHelpers.MemberAsync(f, ct, "Pick Ok", "pickme1").ConfigureAwait(false);
                    TestMember deleted = await SafetyHelpers.MemberAsync(f, ct, "Pick Deleted", "pickme2").ConfigureAwait(false);
                    TestMember banned = await SafetyHelpers.MemberAsync(f, ct, "Pick Banned", "pickme3").ConfigureAwait(false);
                    TestMember unattested = await SafetyHelpers.MemberAsync(f, ct, "Pick Unattested", "pickme4").ConfigureAwait(false);
                    TestMember blockedByCaller = await SafetyHelpers.MemberAsync(f, ct, "Pick Blocked", "pickme5").ConfigureAwait(false);
                    TestMember blocksCaller = await SafetyHelpers.MemberAsync(f, ct, "Pick Blocker", "pickme6").ConfigureAwait(false);
                    TestMember nonMember = await Api.RegisterAsync(f, ct, null, "Pick Nonmember").ConfigureAwait(false);
                    await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/me/profile", nonMember.AccessToken, new { handle = "pickme7" }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    TestMember admin = await Api.AdminAsync(f, ct).ConfigureAwait(false);
                    await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/me/profile", admin.AccessToken, new { handle = "pickme8" }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    await SafetyHelpers.UpdateUserAsync(f, deleted.Id, u => { u.IsDeleted = true; u.DeletedUtc = DateTime.UtcNow; }, ct).ConfigureAwait(false);
                    await SafetyHelpers.UpdateUserAsync(f, banned.Id, u => { u.IsBanned = true; u.BannedUtc = DateTime.UtcNow; }, ct).ConfigureAwait(false);
                    await SafetyHelpers.UpdateUserAsync(f, unattested.Id, u => u.AgeAttestedUtc = null, ct).ConfigureAwait(false);
                    await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/users/" + blockedByCaller.Id + "/block", caller.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/users/" + caller.Id + "/block", blocksCaller.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    List<string> found = await SearchAsync(f, caller, "?search=pickme", ct).ConfigureAwait(false);
                    Check.Equal(String.Join(",", new[] { ok.Id, admin.Id }), String.Join(",", found), "Only the eligible member and the SystemAdmin");
                    found = await SearchAsync(f, caller, "?search=pick", ct).ConfigureAwait(false);
                    Check.True(!found.Contains(deleted.Id) && !found.Contains(nonMember.Id), "Display name matches are excluded too");
                }),
                Case("Search.Validation", "search length, context pairs, maxResults default 8 and cap 20, and the rate limit", async (f, ct) =>
                {
                    TestMember caller = await SafetyHelpers.MemberAsync(f, ct).ConfigureAwait(false);
                    for (int i = 0; i < 22; i++) await SafetyHelpers.MemberAsync(f, ct, "Many Person" + i).ConfigureAwait(false);
                    await Api.ExpectErrorAsync(f, HttpMethod.Get, "/api/v1/members", caller.AccessToken, null, HttpStatusCode.BadRequest, "ValidationFailed", ct).ConfigureAwait(false);
                    await Api.ExpectErrorAsync(f, HttpMethod.Get, "/api/v1/members?search=@", caller.AccessToken, null, HttpStatusCode.BadRequest, "ValidationFailed", ct).ConfigureAwait(false);
                    await Api.ExpectErrorAsync(f, HttpMethod.Get, "/api/v1/members?search=" + new string('a', 31), caller.AccessToken, null, HttpStatusCode.BadRequest, "ValidationFailed", ct).ConfigureAwait(false);
                    await Api.ExpectErrorAsync(f, HttpMethod.Get, "/api/v1/members?search=many&contextType=FeedPost", caller.AccessToken, null, HttpStatusCode.BadRequest, "ValidationFailed", ct).ConfigureAwait(false);
                    await Api.ExpectErrorAsync(f, HttpMethod.Get, "/api/v1/members?search=many&contextId=fpo_x", caller.AccessToken, null, HttpStatusCode.BadRequest, "ValidationFailed", ct).ConfigureAwait(false);
                    await Api.ExpectErrorAsync(f, HttpMethod.Get, "/api/v1/members?search=many&contextType=Nope&contextId=x", caller.AccessToken, null, HttpStatusCode.BadRequest, "ValidationFailed", ct).ConfigureAwait(false);
                    await Api.ExpectErrorAsync(f, HttpMethod.Get, "/api/v1/members?search=many&unknown=1", caller.AccessToken, null, HttpStatusCode.BadRequest, "ValidationFailed", ct).ConfigureAwait(false);
                    JsonElement defaults = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/members?search=many", caller.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.Equal(8, defaults.GetProperty("objects").GetArrayLength(), "Default 8");
                    Check.True(!defaults.GetProperty("endOfResults").GetBoolean(), "More matches than the page: endOfResults false");
                    Check.Equal(22L, defaults.GetProperty("totalRecords").GetInt64(), "Exact matching member total");
                    Check.Equal(14L, defaults.GetProperty("recordsRemaining").GetInt64(), "Exact remaining member count");
                    Check.Equal(JsonValueKind.String, defaults.GetProperty("continuationToken").ValueKind, "More matches have a usable continuation token");
                    JsonElement single = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/members?search=person21", caller.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.Equal(1, single.GetProperty("objects").GetArrayLength(), "One match");
                    Check.True(single.GetProperty("endOfResults").GetBoolean(), "Every match on the page: endOfResults true");
                    Check.Equal(1L, single.GetProperty("totalRecords").GetInt64(), "Exact totalRecords when the page holds every match");
                    Check.Equal(0L, single.GetProperty("recordsRemaining").GetInt64(), "recordsRemaining 0");
                    JsonElement capped = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/members?search=many&maxResults=50", caller.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.Equal(20, capped.GetProperty("objects").GetArrayLength(), "Clamped to 20");
                    Check.Equal(20, capped.GetProperty("maxResults").GetInt32(), "maxResults reports the clamp");
                    f.Settings.RateLimits.MemberSearchPerMinute = 1;
                    TestMember fresh = await SafetyHelpers.MemberAsync(f, ct).ConfigureAwait(false);
                    await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/members?search=many", fresh.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    ApiResult limited = await Api.SendAsync(f, HttpMethod.Get, "/api/v1/members?search=many", fresh.AccessToken, null, ct).ConfigureAwait(false);
                    Check.Equal(HttpStatusCode.TooManyRequests, limited.Status, "429");
                    Check.True(limited.RetryAfter != null, "Retry-After");
                }),
                Case("Regression.MemberSearch.ContextType", "Mention search accepts only the contract's contextType (FeedPost); Media, Comment, and User are 400 ValidationFailed naming filters.contextType", async (f, ct) =>
                {
                    TestMember caller = await SafetyHelpers.MemberAsync(f, ct).ConfigureAwait(false);
                    foreach (string type in new[] { "Media", "Comment", "User" })
                    {
                        ApiResult result = await Api.SendAsync(f, HttpMethod.Get, "/api/v1/members?search=alex&contextType=" + type + "&contextId=x_1", caller.AccessToken, null, ct).ConfigureAwait(false);
                        Check.Equal(HttpStatusCode.BadRequest, result.Status, type + " status");
                        Check.True(result.Body.GetProperty("fieldErrors").TryGetProperty("filters.contextType", out JsonElement _), type + " names filters.contextType");
                    }
                    await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/members?search=alex&contextType=FeedPost&contextId=fpo_x", caller.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                }),
                Case("Words.Maintenance", "Registration, social sign-in, renames, deletion, and the backfill keep displaynamewords in sync", async (f, ct) =>
                {
                    TestMember member = await SafetyHelpers.MemberAsync(f, ct, "Quincy Original").ConfigureAwait(false);
                    Check.Equal("original,quincy", String.Join(",", await f.Database.MemberSearch.WordsAsync(member.Id, ct).ConfigureAwait(false)), "Words on registration");
                    TestMember caller = await SafetyHelpers.MemberAsync(f, ct).ConfigureAwait(false);
                    Check.Equal(1, (await SearchAsync(f, caller, "?search=quin", ct).ConfigureAwait(false)).Count, "Found by first word");
                    await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/me/profile", member.AccessToken, new { displayName = "Renamed Person" }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.Equal(0, (await SearchAsync(f, caller, "?search=quin", ct).ConfigureAwait(false)).Count, "Old words removed");
                    Check.Equal(1, (await SearchAsync(f, caller, "?search=renamed", ct).ConfigureAwait(false)).Count, "New words found");
                    TestMember social = await Api.SocialAsync(f, ct).ConfigureAwait(false);
                    Check.True((await f.Database.MemberSearch.WordsAsync(social.Id, ct).ConfigureAwait(false)).Count > 0, "Words on social sign-up");
                    await f.Database.MemberSearch.DeleteWordsAsync(member.Id, ct).ConfigureAwait(false);
                    Check.True(await f.Services.MemberSearch.BackfillAsync(1, ct).ConfigureAwait(false) >= 1, "Backfill ran");
                    Check.Equal("person,renamed", String.Join(",", await f.Database.MemberSearch.WordsAsync(member.Id, ct).ConfigureAwait(false)), "Backfilled");
                    Check.Equal(0, await f.Services.MemberSearch.BackfillAsync(10, ct).ConfigureAwait(false), "Nothing left to backfill");
                    await f.Services.Blocks.DeleteAllForUserAsync(member.Id, ct).ConfigureAwait(false);
                    await Api.ExpectAsync(f, HttpMethod.Delete, "/api/v1/me", member.AccessToken, new { confirm = "DELETE" }, HttpStatusCode.Accepted, ct).ConfigureAwait(false);
                    Check.Equal(0, (await f.Database.MemberSearch.WordsAsync(member.Id, ct).ConfigureAwait(false)).Count, "Words removed on account deletion");
                })
            });
        }

        #endregion

        #region Private-Methods

        private static async Task<List<string>> SearchAsync(HttpFixture f, TestMember caller, string query, CancellationToken ct)
        {
            JsonElement body = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/members" + query, caller.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            return body.GetProperty("objects").EnumerateArray().Select(o => Api.Str(o, "id")).ToList();
        }

        private static List<string> Objects(JsonElement body)
        {
            return body.GetProperty("objects").EnumerateArray().Select(o => Api.Str(o, "id")).ToList();
        }

        private static string Ids(params TestMember[] members)
        {
            return String.Join(",", members.Select(m => m.Id));
        }

        private static string Join(List<string> ids)
        {
            return String.Join(",", ids);
        }

        private static TestCaseDescriptor Case(string caseId, string name, Func<HttpFixture, CancellationToken, Task> body)
        {
            return SuiteCases.Http(Id, caseId, name, body);
        }

        #endregion
    }
}
