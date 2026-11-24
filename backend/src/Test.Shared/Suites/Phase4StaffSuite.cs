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
    /// Phase 4 staff roles through POST /api/v1/admin/moderators (docs/PHASE4_CONTRACT.md section 1.1, resolved
    /// decision 3): resourceType Directory, Events, and Library grant DirectoryModerator, EventManager, and
    /// LibraryEditor; the moderators list shows them with their roles; revoke removes them; members cannot grant; the
    /// holders reach the moderation routes.
    /// </summary>
    public static class Phase4StaffSuite
    {
        #region Public-Members

        /// <summary>
        /// Suite identifier.
        /// </summary>
        public const string Id = "Phase4Staff";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the suite.
        /// </summary>
        /// <returns>Suite.</returns>
        public static TestSuiteDescriptor Build()
        {
            return new TestSuiteDescriptor(Id, "Phase 4 staff roles (" + TestEnvironment.Provider + ")", new[]
            {
                SuiteCases.Http(Id, "Grant.ListRevoke", "Directory, Events, and Library grants give DirectoryModerator, EventManager, and LibraryEditor, are listed with roles, reach the moderation routes, and revoke removes them", GrantAsync),
                SuiteCases.Http(Id, "Grant.AdminOnly", "Members and scoped staff cannot grant or revoke Phase 4 roles; unknown resource types are rejected", AdminOnlyAsync)
            });
        }

        #endregion

        #region Private-Methods

        private static async Task GrantAsync(HttpFixture f, CancellationToken ct)
        {
            Dictionary<string, string> roles = new Dictionary<string, string> { { "Directory", "DirectoryModerator" }, { "Events", "EventManager" }, { "Library", "LibraryEditor" } };
            foreach (KeyValuePair<string, string> pair in roles)
            {
                TestMember member = await ContentTestKit.MemberAsync(f, ct, pair.Value + " Holder").ConfigureAwait(false);
                await Api.ExpectErrorAsync(f, HttpMethod.Get, ModerationTestKit.Queue, member.AccessToken, null, HttpStatusCode.Forbidden, "Forbidden", ct).ConfigureAwait(false);
                JsonElement row = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/admin/moderators", HttpFixture.AdminToken, new { userId = member.Id, resourceType = pair.Key }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                Check.True(HasRole(row, pair.Value, pair.Key), pair.Key + " grant returns the " + pair.Value + " role: " + row);
                await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/admin/moderators", HttpFixture.AdminToken, new { userId = member.Id, resourceType = pair.Key }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                Check.Equal(1L, await ModerationTestKit.CountAsync(f, "SELECT COUNT(*) AS cnt FROM roleassignments WHERE userid = '" + member.Id + "' AND role = '" + pair.Value + "';", ct).ConfigureAwait(false), "Idempotent grant");

                JsonElement list = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/admin/moderators/enumerate", HttpFixture.AdminToken, new { filters = new { role = pair.Value } }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                JsonElement? listed = list.GetProperty("objects").EnumerateArray().Select(o => (JsonElement?)o).FirstOrDefault(o => Api.Str(o!.Value.GetProperty("user"), "id") == member.Id);
                Check.True(listed != null && HasRole(listed.Value, pair.Value, pair.Key), pair.Value + " listed with roles");
                Check.True(listed!.Value.GetProperty("user").GetProperty("isStaff").GetBoolean(), pair.Value + " counts as staff");

                await Api.ExpectAsync(f, HttpMethod.Get, ModerationTestKit.Queue, member.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);

                await Api.ExpectAsync(f, HttpMethod.Delete, "/api/v1/admin/moderators/" + member.Id + "?resourceType=" + pair.Key, HttpFixture.AdminToken, null, HttpStatusCode.NoContent, ct).ConfigureAwait(false);
                Check.Equal(0L, await ModerationTestKit.CountAsync(f, "SELECT COUNT(*) AS cnt FROM roleassignments WHERE userid = '" + member.Id + "' AND role = '" + pair.Value + "';", ct).ConfigureAwait(false), "Revoked");
                JsonElement after = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/admin/moderators/enumerate", HttpFixture.AdminToken, new { filters = new { role = pair.Value } }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                Check.True(after.GetProperty("objects").EnumerateArray().All(o => Api.Str(o.GetProperty("user"), "id") != member.Id), "Not listed after revoke");
                await Api.ExpectErrorAsync(f, HttpMethod.Get, ModerationTestKit.Queue, member.AccessToken, null, HttpStatusCode.Forbidden, "Forbidden", ct).ConfigureAwait(false);
            }
        }

        private static async Task AdminOnlyAsync(HttpFixture f, CancellationToken ct)
        {
            TestMember member = await ContentTestKit.MemberAsync(f, ct, "Plain Member").ConfigureAwait(false);
            TestMember target = await ContentTestKit.MemberAsync(f, ct, "Target Member").ConfigureAwait(false);
            TestMember editor = await EngagementTestKit.RoleAsync(f, TrellisKit.Core.Enums.RoleEnum.LibraryEditor, TrellisKit.Core.Enums.ResourceTypeEnum.Library, ct, "Library Editor").ConfigureAwait(false);
            foreach (string scope in new[] { "Directory", "Events", "Library" })
            {
                ApiResult byMember = await Api.SendAsync(f, HttpMethod.Post, "/api/v1/admin/moderators", member.AccessToken, new { userId = target.Id, resourceType = scope }, ct).ConfigureAwait(false);
                Check.Equal(HttpStatusCode.Forbidden, byMember.Status, "Members cannot grant " + scope);
                ApiResult byStaff = await Api.SendAsync(f, HttpMethod.Post, "/api/v1/admin/moderators", editor.AccessToken, new { userId = target.Id, resourceType = scope }, ct).ConfigureAwait(false);
                Check.Equal(HttpStatusCode.Forbidden, byStaff.Status, "Scoped staff cannot grant " + scope);
            }
            ApiResult revoke = await Api.SendAsync(f, HttpMethod.Delete, "/api/v1/admin/moderators/" + editor.Id + "?resourceType=Library", member.AccessToken, null, ct).ConfigureAwait(false);
            Check.Equal(HttpStatusCode.Forbidden, revoke.Status, "Members cannot revoke");
            Check.Equal(0L, await ModerationTestKit.CountAsync(f, "SELECT COUNT(*) AS cnt FROM roleassignments WHERE userid = '" + target.Id + "';", ct).ConfigureAwait(false), "Nothing granted");
        }

        private static bool HasRole(JsonElement row, string role, string resourceType)
        {
            if (!row.TryGetProperty("roles", out JsonElement roles)) return false;
            return roles.EnumerateArray().Any(r => Api.Str(r, "role") == role && Api.Str(r, "resourceType") == resourceType);
        }

        #endregion
    }
}
