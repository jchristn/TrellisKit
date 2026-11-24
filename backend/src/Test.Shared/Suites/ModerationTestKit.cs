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
    using TrellisKit.Core.Enums;

    /// <summary>
    /// Helpers of the moderation suites.
    /// </summary>
    internal static class ModerationTestKit
    {
        internal const string Queue = "/api/v1/admin/moderation/queue";

        internal const string Actions = "/api/v1/admin/moderation/actions";

        internal static Task<ApiResult> ReportAsync(HttpFixture f, string bearer, string targetType, string targetId, CancellationToken ct, string reason = "Spam", string? details = null)
        {
            return Api.SendAsync(f, HttpMethod.Post, "/api/v1/reports", bearer, new { targetType, targetId, reason, details }, ct);
        }

        internal static async Task<JsonElement> ReportOkAsync(HttpFixture f, string bearer, string targetType, string targetId, CancellationToken ct, string reason = "Spam", HttpStatusCode expected = HttpStatusCode.Created)
        {
            ApiResult result = await ReportAsync(f, bearer, targetType, targetId, ct, reason).ConfigureAwait(false);
            Check.Equal(expected, result.Status, "Report " + targetType + " " + targetId + " (body: " + result.Text + ")");
            return result.Body;
        }

        internal static Task<ApiResult> ActAsync(HttpFixture f, string bearer, string targetType, string targetId, string action, CancellationToken ct, string reason = "Spam", bool? notifyAuthor = null, bool? resolveReports = null, string? note = null)
        {
            return Api.SendAsync(f, HttpMethod.Post, Actions, bearer, new { targetType, targetId, action, reason, notifyAuthor, resolveReports, note }, ct);
        }

        internal static async Task<JsonElement> ActOkAsync(HttpFixture f, string bearer, string targetType, string targetId, string action, CancellationToken ct, string reason = "Spam", bool? notifyAuthor = null, bool? resolveReports = null)
        {
            ApiResult result = await ActAsync(f, bearer, targetType, targetId, action, ct, reason, notifyAuthor, resolveReports).ConfigureAwait(false);
            Check.Equal(HttpStatusCode.OK, result.Status, action + " " + targetType + " " + targetId + " (body: " + result.Text + ")");
            return result.Body;
        }

        internal static async Task<TestMember> ModeratorAsync(HttpFixture f, CancellationToken ct, string displayName = "Feed Moderator")
        {
            TestMember member = await ContentTestKit.MemberAsync(f, ct, displayName).ConfigureAwait(false);
            await SafetyHelpers.GrantRoleAsync(f, member.Id, RoleEnum.FeedModerator, ct).ConfigureAwait(false);
            return member;
        }

        internal static async Task<List<TestMember>> MembersAsync(HttpFixture f, int count, CancellationToken ct, string prefix = "Reporter")
        {
            List<TestMember> members = new List<TestMember>();
            for (int i = 0; i < count; i++) members.Add(await ContentTestKit.MemberAsync(f, ct, prefix + " " + i).ConfigureAwait(false));
            return members;
        }

        internal static Task<long> CountAsync(HttpFixture f, string sql, CancellationToken ct)
        {
            return ContentTestKit.ScalarAsync(f, sql, ct);
        }

        internal static async Task<string> StateAsync(HttpFixture f, string table, string id, CancellationToken ct)
        {
            System.Data.DataTable rows = await f.Raw.ExecuteQueryAsync("SELECT moderationstate FROM " + table + " WHERE id = '" + id + "';", false, ct).ConfigureAwait(false);
            return rows.Rows.Count == 0 ? String.Empty : TrellisKit.Core.Database.RowReader.GetString(rows.Rows[0], "moderationstate");
        }

        internal static Task<long> AutoHideCountAsync(HttpFixture f, string targetId, CancellationToken ct)
        {
            return CountAsync(f, "SELECT COUNT(*) AS cnt FROM moderationactions WHERE targetid = '" + targetId + "' AND action = 'AutoHide';", ct);
        }

        internal static List<string> TargetIds(JsonElement page)
        {
            return page.GetProperty("objects").EnumerateArray().Select(o => Api.Str(o, "targetId")).ToList();
        }

        internal static async Task<long> AuditCountAsync(HttpFixture f, string action, string targetId, CancellationToken ct)
        {
            return await CountAsync(f, "SELECT COUNT(*) AS cnt FROM auditrecords WHERE action = '" + action + "' AND targetid = '" + targetId + "';", ct).ConfigureAwait(false);
        }
    }
}
