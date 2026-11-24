namespace Test.Shared.Suites
{
    using System;
    using System.Net;
    using System.Net.Http;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core.Enums;
    using TrellisKit.Server.Routes;

    /// <summary>
    /// Helpers of the engagement suites: install a <see cref="TestTargetProvider"/> on a fixture's property services
    /// (replacing a feature provider of the same types) and register the generic conversation and promotion routes
    /// under test-only prefixes.
    /// </summary>
    internal static class EngagementTestKit
    {
        internal const string LessonPrefix = "/api/v1/testlessons";

        internal const string EventPrefix = "/api/v1/testevents";

        internal const string VenuePrefix = "/api/v1/testvenues";

        internal static TestTargetProvider Install(HttpFixture f, string scope, params TargetTypeEnum[] types)
        {
            TestTargetProvider provider = new TestTargetProvider(scope, types);
            foreach (TargetTypeEnum type in types) f.Services.ContentTargets.Unregister(type);
            f.Services.ContentTargets.Register(provider);
            return provider;
        }

        internal static void Routes(HttpFixture f, string prefix, TargetTypeEnum type, bool promotion)
        {
            WatsonWebserver.Core.Routing.RoutingGroup post = f.Server.Server.Routes.PostAuthentication;
            TargetConversationRoutes.Register(post, f.Server.Directory, prefix, type, "Engagement tests", "a test " + type.ToString().ToLowerInvariant());
            if (promotion) TargetPromotionRoutes.Register(post, f.Server.Directory, prefix, type, "Engagement tests");
        }

        internal static async Task<TestTargetProvider> LibraryAsync(HttpFixture f, CancellationToken ct)
        {
            ContentTestKit.Relax(f);
            TestTargetProvider provider = Install(f, "Library", TargetTypeEnum.Lesson, TargetTypeEnum.Series);
            Routes(f, LessonPrefix, TargetTypeEnum.Lesson, true);
            await Task.CompletedTask.ConfigureAwait(false);
            return provider;
        }

        internal static async Task<string> CommentAsync(HttpFixture f, string bearer, string prefix, string id, string text, CancellationToken ct, string? parentId = null)
        {
            JsonElement comment = await Api.ExpectAsync(f, HttpMethod.Post, prefix + "/" + id + "/comments", bearer, new { body = text, parentCommentId = parentId }, HttpStatusCode.Created, ct).ConfigureAwait(false);
            return Api.Str(comment, "id");
        }

        internal static async Task<TestMember> RoleAsync(HttpFixture f, RoleEnum role, ResourceTypeEnum resource, CancellationToken ct, string displayName = "Scoped Staff")
        {
            TestMember member = await ContentTestKit.MemberAsync(f, ct, displayName).ConfigureAwait(false);
            await f.Database.RoleAssignments.AddAsync(new TrellisKit.Core.Models.RoleAssignment
            {
                Id = TrellisKit.Core.Helpers.IdGenerator.GenerateRoleAssignmentId(),
                UserId = member.Id,
                Role = role,
                ResourceType = resource,
                ResourceId = "*"
            }, ct).ConfigureAwait(false);
            f.Services.Tokens.InvalidateUser(member.Id);
            return member;
        }

        internal static async Task<string> ScalarAsync(HttpFixture f, string sql, CancellationToken ct)
        {
            System.Data.DataTable table = await f.Raw.ExecuteQueryAsync(sql, false, ct).ConfigureAwait(false);
            return table.Rows.Count == 0 ? String.Empty : Convert.ToString(table.Rows[0][0], System.Globalization.CultureInfo.InvariantCulture) ?? String.Empty;
        }
    }
}
