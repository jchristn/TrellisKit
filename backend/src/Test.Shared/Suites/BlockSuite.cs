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
    using TrellisKit.Core.Models;
    using TrellisKit.Server.Services;

    /// <summary>
    /// Phase 2 member blocks (contract section 9.2): block and unblock, staff protection, the enumerated block list,
    /// isBlockedByMe on profiles, rate limits, the block limit, and the public block queries.
    /// </summary>
    public static class BlockSuite
    {
        #region Public-Members

        /// <summary>
        /// Suite identifier.
        /// </summary>
        public const string Id = "Blocks";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the suite.
        /// </summary>
        /// <returns>Suite.</returns>
        public static TestSuiteDescriptor Build()
        {
            return new TestSuiteDescriptor(Id, "Member blocks (" + TestEnvironment.Provider + ")", new[]
            {
                Case("Blocks.BlockUnblock", "Block and unblock are idempotent and the profile shows isBlockedByMe, isStaff, labels, and tags", async (f, ct) =>
                {
                    TestMember a = await SafetyHelpers.MemberAsync(f, ct, "Ann Blocker").ConfigureAwait(false);
                    TestMember b = await SafetyHelpers.MemberAsync(f, ct, "Bob Blocked").ConfigureAwait(false);
                    JsonElement profile = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/users/" + b.Id, a.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.True(!profile.GetProperty("isBlockedByMe").GetBoolean(), "Not blocked yet");
                    Check.True(!profile.GetProperty("isStaff").GetBoolean(), "Not staff");
                    Check.Equal(JsonValueKind.Array, profile.GetProperty("labels").ValueKind, "labels present");
                    Check.Equal(JsonValueKind.Object, profile.GetProperty("tags").ValueKind, "tags present");
                    for (int i = 0; i < 2; i++)
                    {
                        JsonElement blocked = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/users/" + b.Id + "/block", a.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                        Check.True(blocked.GetProperty("blocked").GetBoolean(), "blocked true (pass " + i + ")");
                    }
                    Check.Equal(1L, await f.Database.Blocks.CountByBlockerAsync(a.Id, ct).ConfigureAwait(false), "One row");
                    profile = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/users/" + b.Id, a.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.True(profile.GetProperty("isBlockedByMe").GetBoolean(), "isBlockedByMe");
                    JsonElement reverse = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/users/" + a.Id, b.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.True(!reverse.GetProperty("isBlockedByMe").GetBoolean(), "The blocked member is never told");
                    for (int i = 0; i < 2; i++)
                    {
                        JsonElement unblocked = await Api.ExpectAsync(f, HttpMethod.Delete, "/api/v1/users/" + b.Id + "/block", a.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                        Check.True(!unblocked.GetProperty("blocked").GetBoolean(), "blocked false (pass " + i + ")");
                    }
                    profile = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/users/" + b.Id, a.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.True(!profile.GetProperty("isBlockedByMe").GetBoolean(), "Unblocked");
                }),
                Case("Blocks.Errors", "Self, unknown, deleted, and staff targets (every staff role, including DirectoryModerator, EventManager, LibraryEditor, Communicator, and SupportAgent) are refused; the admin token cannot block", async (f, ct) =>
                {
                    TestMember a = await SafetyHelpers.MemberAsync(f, ct).ConfigureAwait(false);
                    await Api.ExpectErrorAsync(f, HttpMethod.Post, "/api/v1/users/" + a.Id + "/block", a.AccessToken, null, HttpStatusCode.BadRequest, "CannotBlockSelf", ct).ConfigureAwait(false);
                    await Api.ExpectErrorAsync(f, HttpMethod.Post, "/api/v1/users/usr_unknown/block", a.AccessToken, null, HttpStatusCode.NotFound, "NotFound", ct).ConfigureAwait(false);
                    TestMember gone = await SafetyHelpers.MemberAsync(f, ct).ConfigureAwait(false);
                    await SafetyHelpers.UpdateUserAsync(f, gone.Id, u => { u.IsDeleted = true; u.DeletedUtc = DateTime.UtcNow; }, ct).ConfigureAwait(false);
                    await Api.ExpectErrorAsync(f, HttpMethod.Post, "/api/v1/users/" + gone.Id + "/block", a.AccessToken, null, HttpStatusCode.NotFound, "NotFound", ct).ConfigureAwait(false);
                    foreach (RoleEnum role in new[] { RoleEnum.SystemAdmin, RoleEnum.Moderator, RoleEnum.FeedModerator, RoleEnum.DirectoryModerator, RoleEnum.EventManager, RoleEnum.LibraryEditor, RoleEnum.Communicator, RoleEnum.SupportAgent })
                    {
                        TestMember staff = await SafetyHelpers.MemberAsync(f, ct).ConfigureAwait(false);
                        await SafetyHelpers.GrantRoleAsync(f, staff.Id, role, ct).ConfigureAwait(false);
                        await Api.ExpectErrorAsync(f, HttpMethod.Post, "/api/v1/users/" + staff.Id + "/block", a.AccessToken, null, HttpStatusCode.BadRequest, "CannotBlockStaff", ct).ConfigureAwait(false);
                        JsonElement profile = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/users/" + staff.Id, a.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                        Check.True(profile.GetProperty("isStaff").GetBoolean(), role + " profile isStaff");
                    }
                    await Api.ExpectErrorAsync(f, HttpMethod.Post, "/api/v1/users/" + a.Id + "/block", HttpFixture.AdminToken, null, HttpStatusCode.Forbidden, "Forbidden", ct).ConfigureAwait(false);
                    TestMember noMembership = await Api.RegisterAsync(f, ct).ConfigureAwait(false);
                    await Api.ExpectErrorAsync(f, HttpMethod.Post, "/api/v1/users/" + a.Id + "/block", noMembership.AccessToken, null, HttpStatusCode.PaymentRequired, "MembershipRequired", ct).ConfigureAwait(false);
                }),
                Case("Blocks.MutedAndRestricted", "Muted members and members without canPost can still block", async (f, ct) =>
                {
                    TestMember a = await SafetyHelpers.MemberAsync(f, ct).ConfigureAwait(false);
                    TestMember b = await SafetyHelpers.MemberAsync(f, ct).ConfigureAwait(false);
                    TestMember c = await SafetyHelpers.MemberAsync(f, ct).ConfigureAwait(false);
                    await SafetyHelpers.UpdateUserAsync(f, a.Id, u => { u.IsMuted = true; u.MutedUntilUtc = DateTime.UtcNow.AddDays(1); u.MutedReason = "test"; }, ct).ConfigureAwait(false);
                    await f.Database.UserPermissions.UpsertAsync(new UserPermission { UserId = a.Id, CanPost = false, CanPostMedia = false }, ct).ConfigureAwait(false);
                    await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/users/" + b.Id + "/block", a.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/users/" + c.Id + "/block", a.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    await Api.ExpectAsync(f, HttpMethod.Delete, "/api/v1/users/" + c.Id + "/block", a.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.True(await f.Services.Blocks.HasBlockedAsync(a.Id, b.Id, ct).ConfigureAwait(false), "Blocked while muted");
                }),
                Case("Blocks.RateLimit", "Block and unblock share moderation.blocksPerHour and 429 carries Retry-After", async (f, ct) =>
                {
                    f.Settings.Moderation.BlocksPerHour = 3;
                    TestMember a = await SafetyHelpers.MemberAsync(f, ct).ConfigureAwait(false);
                    TestMember b = await SafetyHelpers.MemberAsync(f, ct).ConfigureAwait(false);
                    await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/users/" + b.Id + "/block", a.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    await Api.ExpectAsync(f, HttpMethod.Delete, "/api/v1/users/" + b.Id + "/block", a.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/users/" + b.Id + "/block", a.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    ApiResult limited = await Api.SendAsync(f, HttpMethod.Delete, "/api/v1/users/" + b.Id + "/block", a.AccessToken, null, ct).ConfigureAwait(false);
                    Check.Equal(HttpStatusCode.TooManyRequests, limited.Status, "429");
                    Check.Equal("RateLimited", limited.Error, "Error code");
                    Check.True(Int32.TryParse(limited.RetryAfter, out int seconds) && seconds > 0, "Retry-After seconds (" + limited.RetryAfter + ")");
                }),
                Case("Blocks.Limit", "Past the block limit returns 409 BlockLimitReached while existing blocks stay idempotent", async (f, ct) =>
                {
                    f.Services.Blocks.MaxBlocksPerMember = 2;
                    TestMember a = await SafetyHelpers.MemberAsync(f, ct).ConfigureAwait(false);
                    List<TestMember> others = new List<TestMember>();
                    for (int i = 0; i < 3; i++) others.Add(await SafetyHelpers.MemberAsync(f, ct).ConfigureAwait(false));
                    await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/users/" + others[0].Id + "/block", a.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/users/" + others[1].Id + "/block", a.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    await Api.ExpectErrorAsync(f, HttpMethod.Post, "/api/v1/users/" + others[2].Id + "/block", a.AccessToken, null, HttpStatusCode.Conflict, "BlockLimitReached", ct).ConfigureAwait(false);
                    await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/users/" + others[1].Id + "/block", a.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                }),
                Case("Blocks.Concurrent", "Concurrent blocks of the same member store one row", async (f, ct) =>
                {
                    TestMember a = await SafetyHelpers.MemberAsync(f, ct).ConfigureAwait(false);
                    TestMember b = await SafetyHelpers.MemberAsync(f, ct).ConfigureAwait(false);
                    List<Task<ApiResult>> tasks = Enumerable.Range(0, 8).Select(_ => Api.SendAsync(f, HttpMethod.Post, "/api/v1/users/" + b.Id + "/block", a.AccessToken, null, ct)).ToList();
                    ApiResult[] results = await Task.WhenAll(tasks).ConfigureAwait(false);
                    Check.True(results.All(r => r.Status == HttpStatusCode.OK), "All 200");
                    Check.Equal(1L, await f.Database.Blocks.CountByBlockerAsync(a.Id, ct).ConfigureAwait(false), "One row");
                }),
                Case("Blocks.List", "GET and POST enumerate list blocks newest first with continuation, search, and validation", async (f, ct) =>
                {
                    TestMember a = await SafetyHelpers.MemberAsync(f, ct).ConfigureAwait(false);
                    List<string> blocked = new List<string>();
                    for (int i = 0; i < 5; i++)
                    {
                        TestMember other = await SafetyHelpers.MemberAsync(f, ct, "Listed Person" + i).ConfigureAwait(false);
                        await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/users/" + other.Id + "/block", a.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                        blocked.Add(other.Id);
                        await Task.Delay(5, ct).ConfigureAwait(false);
                    }
                    List<string> seen = new List<string>();
                    string? token = null;
                    int pages = 0;
                    do
                    {
                        string path = "/api/v1/me/blocks?maxResults=2" + (token == null ? String.Empty : "&continuationToken=" + Uri.EscapeDataString(token));
                        JsonElement page = await Api.ExpectAsync(f, HttpMethod.Get, path, a.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                        foreach (JsonElement item in page.GetProperty("objects").EnumerateArray())
                        {
                            seen.Add(Api.Str(item.GetProperty("user"), "id"));
                            Check.Equal(JsonValueKind.Array, item.GetProperty("labels").ValueKind, "labels");
                            Check.Equal(JsonValueKind.Object, item.GetProperty("tags").ValueKind, "tags");
                            Check.True(item.TryGetProperty("createdUtc", out JsonElement _), "createdUtc");
                        }
                        token = page.GetProperty("continuationToken").ValueKind == JsonValueKind.String ? page.GetProperty("continuationToken").GetString() : null;
                        pages++;
                    }
                    while (token != null && pages < 10);
                    Check.Equal(3, pages, "Three pages of 2, 2, 1");
                    Check.Equal(String.Join(",", Enumerable.Reverse(blocked)), String.Join(",", seen), "Newest first, no duplicates or gaps");

                    JsonElement oldest = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/me/blocks/enumerate", a.AccessToken, new { maxResults = 1, ordering = "CreatedAscending" }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.Equal(blocked[0], Api.Str(oldest.GetProperty("objects")[0].GetProperty("user"), "id"), "CreatedAscending");
                    JsonElement searched = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/me/blocks?search=person3", a.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.Equal(1, searched.GetProperty("objects").GetArrayLength(), "Search by display name");
                    await Api.ExpectErrorAsync(f, HttpMethod.Get, "/api/v1/me/blocks?ordering=Sideways", a.AccessToken, null, HttpStatusCode.BadRequest, "ValidationFailed", ct).ConfigureAwait(false);
                    await Api.ExpectErrorAsync(f, HttpMethod.Get, "/api/v1/me/blocks?continuationToken=garbage", a.AccessToken, null, HttpStatusCode.BadRequest, "InvalidContinuationToken", ct).ConfigureAwait(false);
                    JsonElement clamped = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/me/blocks?maxResults=5000", a.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.True(clamped.GetProperty("maxResults").GetInt32() <= 100, "maxResults clamped");
                    TestMember b = await SafetyHelpers.MemberAsync(f, ct).ConfigureAwait(false);
                    JsonElement firstPage = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/me/blocks?maxResults=2", a.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    string otherToken = firstPage.GetProperty("continuationToken").GetString()!;
                    await Api.ExpectErrorAsync(f, HttpMethod.Get, "/api/v1/me/blocks?continuationToken=" + Uri.EscapeDataString(otherToken), b.AccessToken, null, HttpStatusCode.BadRequest, "InvalidContinuationToken", ct).ConfigureAwait(false);
                }),
                Case("Blocks.PublicApi", "The block queries for other services, including hidden authors that ignore current staff", async (f, ct) =>
                {
                    TestMember a = await SafetyHelpers.MemberAsync(f, ct).ConfigureAwait(false);
                    TestMember b = await SafetyHelpers.MemberAsync(f, ct).ConfigureAwait(false);
                    TestMember c = await SafetyHelpers.MemberAsync(f, ct).ConfigureAwait(false);
                    TestMember d = await SafetyHelpers.MemberAsync(f, ct).ConfigureAwait(false);
                    BlockService blocks = f.Services.Blocks;
                    await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/users/" + b.Id + "/block", a.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/users/" + c.Id + "/block", a.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/users/" + a.Id + "/block", d.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.True(await blocks.HasBlockedAsync(a.Id, b.Id, ct).ConfigureAwait(false), "a blocked b");
                    Check.True(!await blocks.HasBlockedAsync(b.Id, a.Id, ct).ConfigureAwait(false), "b did not block a");
                    Check.True(await blocks.IsEitherBlockedAsync(b.Id, a.Id, ct).ConfigureAwait(false), "Either direction");
                    Check.True(!await blocks.IsEitherBlockedAsync(b.Id, c.Id, ct).ConfigureAwait(false), "Unrelated pair");
                    Check.True(!await blocks.HasBlockedAsync(null, b.Id, ct).ConfigureAwait(false), "Null viewer");
                    Check.Equal("2", (await blocks.BlockedByAsync(a.Id, ct).ConfigureAwait(false)).Count.ToString(), "BlockedBy");
                    Check.True((await blocks.BlockersOfAsync(a.Id, ct).ConfigureAwait(false)).SetEquals(new[] { d.Id }), "BlockersOf");
                    Check.True((await blocks.RelatedAsync(a.Id, ct).ConfigureAwait(false)).SetEquals(new[] { b.Id, c.Id, d.Id }), "Related");
                    Check.True((await blocks.RelatedAmongAsync(a.Id, new[] { b.Id, d.Id, "usr_x" }, ct).ConfigureAwait(false)).SetEquals(new[] { b.Id, d.Id }), "RelatedAmong");
                    Check.True((await blocks.HiddenAuthorsForAsync(a.Id, ct).ConfigureAwait(false)).SetEquals(new[] { b.Id, c.Id }), "Hidden authors");
                    await SafetyHelpers.GrantRoleAsync(f, c.Id, RoleEnum.FeedModerator, ct).ConfigureAwait(false);
                    Check.True((await blocks.HiddenAuthorsForAsync(a.Id, ct).ConfigureAwait(false)).SetEquals(new[] { b.Id }), "A blocked member who became staff is shown again");
                    await SafetyHelpers.GrantRoleAsync(f, b.Id, RoleEnum.DirectoryModerator, ct).ConfigureAwait(false);
                    Check.True((await blocks.HiddenAuthorsForAsync(a.Id, ct).ConfigureAwait(false)).Count == 0, "Every staff role is shown again (DirectoryModerator)");
                    Check.True(await blocks.HasBlockedAsync(a.Id, c.Id, ct).ConfigureAwait(false), "The block itself is kept");
                    Check.Equal(3, await blocks.DeleteAllForUserAsync(a.Id, ct).ConfigureAwait(false), "Account deletion removes both directions");
                    Check.True((await blocks.RelatedAsync(a.Id, ct).ConfigureAwait(false)).Count == 0, "Nothing left");
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
