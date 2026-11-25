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
    using TrellisKit.Core.Models;

    /// <summary>
    /// Helpers of the rank, curriculum, and attendance suites. The sample property's Rank field "rank" has the options
    /// Beginner, Intermediate, and Advanced and rank.maxLevel 3.
    /// </summary>
    internal static class RankTestKit
    {
        internal const string Awards = "/api/v1/admin/ranks/awards";

        internal static void Enable(HttpFixture f)
        {
            ContentTestKit.Relax(f);
            LibraryTestKit.Relax(f);
            f.Settings.Ranks.Enabled = true;
            f.Settings.RateLimits.MemberReadsPerMinute = 100000;
        }

        internal static async Task<ProfileField> FieldAsync(HttpFixture f, CancellationToken ct)
        {
            IReadOnlyList<ProfileField> all = await f.Services.ProfileFields.AllAsync(ct).ConfigureAwait(false);
            return all.First(x => x.Type == ProfileFieldTypeEnum.Rank && x.Key == "rank");
        }

        /// <summary>
        /// Replace the rank settings (and optionally per-option maxLevel values) of the sample Rank field through the admin route.
        /// </summary>
        internal static async Task<JsonElement> ConfigureAsync(HttpFixture f, object rank, CancellationToken ct, int?[]? optionMaxLevels = null, HttpStatusCode expected = HttpStatusCode.OK)
        {
            ProfileField field = await FieldAsync(f, ct).ConfigureAwait(false);
            Dictionary<string, object?> body = new Dictionary<string, object?> { { "rank", rank } };
            if (optionMaxLevels != null)
            {
                body["options"] = field.Options.Select((o, i) => new { id = o.Id, key = o.Key, label = o.Label, color = o.Color, maxLevel = i < optionMaxLevels.Length ? optionMaxLevels[i] : null }).ToList();
            }
            ApiResult result = await Api.SendAsync(f, HttpMethod.Put, "/api/v1/admin/profile-fields/" + field.Id, HttpFixture.AdminToken, body, ct).ConfigureAwait(false);
            Check.Equal(expected, result.Status, "Configure the Rank field (" + result.Text + ")");
            f.Services.ProfileFields.Invalidate();
            return result.Body;
        }

        internal static Task<TestMember> AwarderAsync(HttpFixture f, CancellationToken ct, string name = "Rank Awarder")
        {
            return EngagementTestKit.RoleAsync(f, RoleEnum.RankAwarder, ResourceTypeEnum.Ranks, ct, name);
        }

        internal static async Task<JsonElement> AwardAsync(HttpFixture f, string bearer, string userId, string option, int level, CancellationToken ct, string? note = null)
        {
            return await Api.ExpectAsync(f, HttpMethod.Post, Awards, bearer, new { userId, optionKey = option, level, note }, HttpStatusCode.Created, ct).ConfigureAwait(false);
        }

        internal static async Task AwardFailsAsync(HttpFixture f, string bearer, string userId, string option, int level, HttpStatusCode status, string error, CancellationToken ct)
        {
            await Api.ExpectErrorAsync(f, HttpMethod.Post, Awards, bearer, new { userId, optionKey = option, level }, status, error, ct).ConfigureAwait(false);
        }

        internal static async Task<JsonElement> MineAsync(HttpFixture f, TestMember member, CancellationToken ct)
        {
            JsonElement mine = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/me/rank", member.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            return mine.GetProperty("fields")[0];
        }

        internal static async Task SetProfileRankAsync(HttpFixture f, TestMember member, string option, int level, HttpStatusCode expected, CancellationToken ct)
        {
            ApiResult result = await Api.SendAsync(f, HttpMethod.Put, "/api/v1/me/profile", member.AccessToken, new { profile = new Dictionary<string, object> { { "rank", new { key = option, level } } } }, ct).ConfigureAwait(false);
            Check.Equal(expected, result.Status, "Profile rank edit (" + result.Text + ")");
        }

        internal static Task SqlAsync(HttpFixture f, string sql, CancellationToken ct)
        {
            return LibraryTestKit.SqlAsync(f, sql, ct);
        }

        internal static Task<long> CountAsync(HttpFixture f, string sql, CancellationToken ct)
        {
            return ContentTestKit.ScalarAsync(f, sql.Replace("SELECT COUNT(*) FROM", "SELECT COUNT(*) AS cnt FROM", StringComparison.Ordinal), ct);
        }

        internal static string Pid(HttpFixture f)
        {
            return LibraryTestKit.Pid(f);
        }
    }
}
