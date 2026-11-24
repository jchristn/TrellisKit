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
    using TrellisKit.Core.Helpers;
    using TrellisKit.Core.Models;
    using TrellisKit.Server.Services;

    /// <summary>
    /// Helpers of the broadcast suite.
    /// </summary>
    internal static class BroadcastTestKit
    {
        internal const string Base = "/api/v1/admin/broadcasts";

        internal static void Prepare(HttpFixture f)
        {
            NotificationTestKit.AllDay(f);
            // Tests run the send passes themselves; the send route must not start a background pass that holds the lease.
            f.Services.Broadcasts.Broadcasts.SendSignal = null;
        }

        internal static async Task<TestMember> CommunicatorAsync(HttpFixture f, CancellationToken ct)
        {
            TestMember member = await Api.RegisterAsync(f, ct, Api.UniqueEmail("communicator"), "Comms Person").ConfigureAwait(false);
            await f.Database.RoleAssignments.AddAsync(new RoleAssignment
            {
                Id = IdGenerator.GenerateRoleAssignmentId(),
                UserId = member.Id,
                Role = RoleEnum.Communicator,
                ResourceType = ResourceTypeEnum.Global,
                ResourceId = "*"
            }, ct).ConfigureAwait(false);
            f.Services.Tokens.InvalidateUser(member.Id);
            return member;
        }

        internal static object Filter(string field, string op, object value)
        {
            return new Dictionary<string, object> { { "field", field }, { "operator", op }, { "value", value } };
        }

        internal static object Segment(params object[] filters)
        {
            return new { filters };
        }

        internal static object Draft(string title, object segment, bool push = false, bool email = false, string? category = null, string? reason = null)
        {
            return new
            {
                title,
                channels = new { inApp = true, push, email },
                inApp = new { title = title + " title", body = "Hello **everyone**, details below.\n\n- one\n- two" },
                push = new { title = title + " push", body = "Short push body" },
                email = new { subject = title + " subject", preheader = "Preheader text", body = "Email **body** with [a link](https://example.test/x)." },
                segment,
                category,
                essentialReason = reason
            };
        }

        internal static async Task<string> CreateAsync(HttpFixture f, string bearer, object body, CancellationToken ct)
        {
            JsonElement created = await Api.ExpectAsync(f, HttpMethod.Post, Base, bearer, body, HttpStatusCode.Created, ct).ConfigureAwait(false);
            return Api.Str(created, "id");
        }

        internal static async Task<JsonElement> PreviewAsync(HttpFixture f, string bearer, object segment, CancellationToken ct, string? category = null)
        {
            return await Api.ExpectAsync(f, HttpMethod.Post, Base + "/segments/preview", bearer, new { segment, category }, HttpStatusCode.OK, ct).ConfigureAwait(false);
        }

        internal static async Task<long> CountAsync(HttpFixture f, string bearer, object segment, CancellationToken ct)
        {
            return (await PreviewAsync(f, bearer, segment, ct).ConfigureAwait(false)).GetProperty("count").GetInt64();
        }

        internal static async Task<JsonElement> SendAsync(HttpFixture f, string bearer, string id, long confirm, CancellationToken ct)
        {
            return await Api.ExpectAsync(f, HttpMethod.Post, Base + "/" + id + "/send", bearer, new { confirmCount = confirm }, HttpStatusCode.OK, ct).ConfigureAwait(false);
        }

        internal static async Task DrainAsync(HttpFixture f, CancellationToken ct, BroadcastSendService? sender = null)
        {
            BroadcastSendService s = sender ?? f.Services.Broadcasts.Sender;
            for (int i = 0; i < 400; i++)
            {
                await s.RunPassAsync(ct).ConfigureAwait(false);
                if (await ScalarAsync(f, "SELECT COUNT(*) AS cnt FROM broadcasts WHERE status IN ('Sending') OR (status = 'Scheduled' AND sendatutc <= now());", ct).ConfigureAwait(false) == 0) return;
                await Task.Delay(25, ct).ConfigureAwait(false);
            }
            throw new InvalidOperationException("Broadcasts did not finish sending.");
        }

        internal static Task<long> ScalarAsync(HttpFixture f, string sql, CancellationToken ct)
        {
            return NotificationTestKit.CountAsync(f, sql, ct);
        }

        internal static Task<long> DeliveriesAsync(HttpFixture f, string broadcastId, CancellationToken ct, string? condition = null)
        {
            return ScalarAsync(f, "SELECT COUNT(*) AS cnt FROM broadcastdeliveries WHERE broadcastid = '" + broadcastId + "'" + (condition == null ? String.Empty : " AND " + condition) + ";", ct);
        }

        internal static Task<long> NotificationsAsync(HttpFixture f, string broadcastId, CancellationToken ct)
        {
            return ScalarAsync(f, "SELECT COUNT(*) AS cnt FROM notifications WHERE type = 'Broadcast' AND targetid = '" + broadcastId + "';", ct);
        }

        internal static async Task<string> StatusAsync(HttpFixture f, string bearer, string id, CancellationToken ct)
        {
            JsonElement b = await Api.ExpectAsync(f, HttpMethod.Get, Base + "/" + id, bearer, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            return Api.Str(b, "status");
        }

        internal static async Task LabelAsync(HttpFixture f, string userId, CancellationToken ct, params string[] labels)
        {
            await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/metadata/User/" + userId, HttpFixture.AdminToken, new { labels }, HttpStatusCode.OK, ct).ConfigureAwait(false);
        }

        internal static async Task OpenModelAsync(HttpFixture f, CancellationToken ct)
        {
            await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/admin/property/access", HttpFixture.AdminToken, new { model = "Open", anonymousBrowsing = false }, HttpStatusCode.OK, ct).ConfigureAwait(false);
        }

        /// <summary>
        /// Insert age-attested accounts with verified addresses directly (fast bulk audiences), created at a fixed time so
        /// a signedUpBeforeUtc filter selects exactly them.
        /// </summary>
        internal static async Task<List<string>> InsertUsersAsync(HttpFixture f, int count, DateTime createdUtc, CancellationToken ct)
        {
            List<string> ids = new List<string>();
            for (int i = 0; i < count; i++)
            {
                User user = new User
                {
                    Id = IdGenerator.GenerateUserId(),
                    Email = Api.UniqueEmail("bulk"),
                    EmailVerified = true,
                    EmailVerifiedUtc = createdUtc,
                    DisplayName = "Bulk Member " + i,
                    AgeAttestedUtc = createdUtc,
                    CreatedUtc = createdUtc,
                    UpdatedUtc = createdUtc
                };
                await f.Database.Users.CreateAsync(user, ct).ConfigureAwait(false);
                ids.Add(user.Id);
            }
            return ids;
        }

        internal static object BulkSegment(DateTime createdUtc)
        {
            return Segment(Filter("signedUpBeforeUtc", "eq", createdUtc.AddDays(1).ToString("O")), Filter("signedUpAfterUtc", "eq", createdUtc.AddDays(-1).ToString("O")));
        }

        internal static List<string> Ids(JsonElement page)
        {
            return page.GetProperty("objects").EnumerateArray().Select(o => Api.Str(o, "id")).ToList();
        }
    }
}
