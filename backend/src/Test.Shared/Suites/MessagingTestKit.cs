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
    /// Shared helpers of the direct messaging suites.
    /// </summary>
    internal static class MessagingTestKit
    {
        internal const string Conversations = "/api/v1/conversations";

        /// <summary>
        /// A member with a membership and a display name.
        /// </summary>
        internal static Task<TestMember> MemberAsync(HttpFixture f, CancellationToken ct, string name = "Dm Member")
        {
            return SafetyHelpers.MemberAsync(f, ct, name);
        }

        /// <summary>
        /// Start a conversation (optionally with a first message) and return the response body.
        /// </summary>
        internal static async Task<JsonElement> StartAsync(HttpFixture f, TestMember sender, string recipientId, string? body, CancellationToken ct, HttpStatusCode expected = HttpStatusCode.Created)
        {
            object request = body == null ? new { recipientUserId = recipientId } : new { recipientUserId = recipientId, message = new { clientMessageId = Guid.NewGuid().ToString(), body } };
            return await Api.ExpectAsync(f, HttpMethod.Post, Conversations, sender.AccessToken, request, expected, ct).ConfigureAwait(false);
        }

        /// <summary>
        /// Start a conversation and return its id.
        /// </summary>
        internal static async Task<string> StartIdAsync(HttpFixture f, TestMember sender, string recipientId, string? body, CancellationToken ct)
        {
            JsonElement started = await StartAsync(f, sender, recipientId, body, ct).ConfigureAwait(false);
            return Api.Str(started.GetProperty("conversation"), "id");
        }

        /// <summary>
        /// A conversation both members accepted (the recipient replied).
        /// </summary>
        internal static async Task<string> PairAsync(HttpFixture f, TestMember a, TestMember b, CancellationToken ct)
        {
            string id = await StartIdAsync(f, a, b.Id, "hello", ct).ConfigureAwait(false);
            await SendOkAsync(f, b, id, "hi back", ct).ConfigureAwait(false);
            return id;
        }

        /// <summary>
        /// Send a message (raw result).
        /// </summary>
        internal static Task<ApiResult> SendAsync(HttpFixture f, TestMember sender, string conversationId, string? body, CancellationToken ct, string? clientMessageId = null, object? extra = null)
        {
            object request = extra ?? new { clientMessageId = clientMessageId ?? Guid.NewGuid().ToString(), body };
            return Api.SendAsync(f, HttpMethod.Post, Conversations + "/" + conversationId + "/messages", sender.AccessToken, request, ct);
        }

        /// <summary>
        /// Send a message expecting 201 and return it.
        /// </summary>
        internal static async Task<JsonElement> SendOkAsync(HttpFixture f, TestMember sender, string conversationId, string body, CancellationToken ct)
        {
            ApiResult result = await SendAsync(f, sender, conversationId, body, ct).ConfigureAwait(false);
            Check.Equal(HttpStatusCode.Created, result.Status, "send (" + result.Text + ")");
            return result.Body;
        }

        /// <summary>
        /// A history page.
        /// </summary>
        internal static Task<JsonElement> HistoryAsync(HttpFixture f, TestMember member, string conversationId, object? query, CancellationToken ct)
        {
            return Api.ExpectAsync(f, HttpMethod.Post, Conversations + "/" + conversationId + "/messages/enumerate", member.AccessToken, query ?? new { }, HttpStatusCode.OK, ct);
        }

        /// <summary>
        /// The ids of a conversation list page.
        /// </summary>
        internal static async Task<List<string>> ListIdsAsync(HttpFixture f, TestMember member, object? query, CancellationToken ct)
        {
            JsonElement page = await Api.ExpectAsync(f, HttpMethod.Post, Conversations + "/enumerate", member.AccessToken, query ?? new { }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            return page.GetProperty("objects").EnumerateArray().Select(o => Api.Str(o, "id")).ToList();
        }

        /// <summary>
        /// The seq values of a page of messages.
        /// </summary>
        internal static List<long> Seqs(JsonElement page)
        {
            return page.GetProperty("objects").EnumerateArray().Select(o => o.GetProperty("seq").GetInt64()).ToList();
        }

        /// <summary>
        /// Grant a role with a resource type.
        /// </summary>
        internal static async Task GrantAsync(HttpFixture f, string userId, RoleEnum role, ResourceTypeEnum resourceType, CancellationToken ct)
        {
            await f.Database.RoleAssignments.AddAsync(new TrellisKit.Core.Models.RoleAssignment
            {
                Id = TrellisKit.Core.Helpers.IdGenerator.GenerateRoleAssignmentId(),
                UserId = userId,
                Role = role,
                ResourceType = resourceType,
                ResourceId = "*"
            }, ct).ConfigureAwait(false);
            f.Services.Tokens.InvalidateUser(userId);
        }

        /// <summary>
        /// End a member's memberships (lapse).
        /// </summary>
        internal static async Task LapseAsync(HttpFixture f, string userId, CancellationToken ct)
        {
            await f.Raw.ExecuteQueryAsync("UPDATE entitlements SET revokedutc = now() - interval '1 minute' WHERE userid = '" + userId + "';", false, ct).ConfigureAwait(false);
            f.Services.Tokens.InvalidateUser(userId);
        }

        /// <summary>
        /// Register a push token for a member.
        /// </summary>
        internal static async Task<string> PushTokenAsync(HttpFixture f, TestMember member, CancellationToken ct)
        {
            string token = NotificationTestKit.NewToken();
            ApiResult result = await Api.SendAsync(f, HttpMethod.Post, "/api/v1/me/push-tokens", member.AccessToken, new { token, platform = "Ios" }, ct).ConfigureAwait(false);
            Check.True(result.Status == HttpStatusCode.Created || result.Status == HttpStatusCode.OK, "push token registered (" + result.Text + ")");
            return token;
        }

        /// <summary>
        /// Count rows of a property table with a condition.
        /// </summary>
        internal static async Task<long> CountAsync(HttpFixture f, string table, string condition, CancellationToken ct)
        {
            System.Data.DataTable rows = await f.Raw.ExecuteQueryAsync("SELECT COUNT(*) AS cnt FROM " + table + " WHERE propertyid = '" + (f.PropertyId ?? TestProperty.SampleId) + "' AND " + condition + ";", false, ct).ConfigureAwait(false);
            return TrellisKit.Core.Database.RowReader.GetLong(rows.Rows[0], "cnt");
        }

        /// <summary>
        /// One scalar text value of a property table.
        /// </summary>
        internal static async Task<string?> ScalarAsync(HttpFixture f, string column, string table, string condition, CancellationToken ct)
        {
            System.Data.DataTable rows = await f.Raw.ExecuteQueryAsync("SELECT " + column + " AS v FROM " + table + " WHERE propertyid = '" + (f.PropertyId ?? TestProperty.SampleId) + "' AND " + condition + " LIMIT 1;", false, ct).ConfigureAwait(false);
            return rows.Rows.Count == 0 ? null : TrellisKit.Core.Database.RowReader.GetNullableString(rows.Rows[0], "v");
        }
    }
}
