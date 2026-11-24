namespace Test.Shared.Suites
{
    using System;
    using System.Collections.Generic;
    using System.Data;
    using System.Globalization;
    using System.Linq;
    using System.Net;
    using System.Net.Http;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core;
    using TrellisKit.Core.Database;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Helpers;
    using TrellisKit.Core.Models;
    using TrellisKit.Server.Services;
    using TrellisKit.Server.Workers;

    /// <summary>
    /// Shared helpers of the notification, push, and feed post email suites.
    /// </summary>
    internal static class NotificationTestKit
    {
        internal static async Task<TestMember> MemberAsync(HttpFixture f, string displayName, CancellationToken ct)
        {
            TestMember member = await Api.RegisterAsync(f, ct, null, displayName).ConfigureAwait(false);
            await Api.GrantCompAsync(f, member.Id, ct).ConfigureAwait(false);
            return member;
        }

        internal static string Ts(DateTime utc)
        {
            return "'" + utc.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture) + "'";
        }

        internal static async Task BlockAsync(HttpFixture f, string blockerId, string blockedId, CancellationToken ct)
        {
            await f.Raw.ExecuteQueryAsync("INSERT INTO blocks (propertyid, blockeruserid, blockeduserid, createdutc) VALUES ('" + TestProperty.SampleId + "', '" + blockerId + "', '" + blockedId + "', " + Ts(DateTime.UtcNow) + ");", false, ct).ConfigureAwait(false);
        }

        internal static async Task<string> InsertPostAsync(HttpFixture f, string authorId, string? title, CancellationToken ct, bool published = true, string moderationState = "None", bool deleted = false)
        {
            string id = IdGenerator.Generate(Constants.FeedPostIdPrefix);
            string now = Ts(DateTime.UtcNow);
            await f.Raw.ExecuteQueryAsync("INSERT INTO feedposts (propertyid, id, authoruserid, kind, title, body, excerpt, visibility, publishedutc, moderationstate, deletedutc, createdutc, updatedutc) VALUES ('" + TestProperty.SampleId + "', '" + id + "', '" + authorId +
                "', 'Original', " + (title == null ? "NULL" : "'" + title.Replace("'", "''") + "'") + ", 'Body text of the post', 'Body text of the post', 'Members', " + (published ? now : "NULL") + ", '" + moderationState + "', " +
                (deleted ? now : "NULL") + ", " + now + ", " + now + ");", false, ct).ConfigureAwait(false);
            return id;
        }

        internal static async Task VerifyEmailAsync(HttpFixture f, string userId, CancellationToken ct)
        {
            User user = (await f.Database.Users.ReadAsync(userId, ct).ConfigureAwait(false))!;
            user.EmailVerified = true;
            user.EmailVerifiedUtc = DateTime.UtcNow;
            await f.Database.Users.UpdateAsync(user, ct).ConfigureAwait(false);
        }

        internal static string NewToken()
        {
            return "ExponentPushToken[" + Guid.NewGuid().ToString("N") + "]";
        }

        internal static async Task<string> RegisterTokenAsync(HttpFixture f, TestMember member, CancellationToken ct, string platform = "Ios")
        {
            string value = NewToken();
            await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/me/push-tokens", member.AccessToken, new { token = value, platform, deviceName = "Test phone", appVersion = "1.0.0" }, HttpStatusCode.Created, ct).ConfigureAwait(false);
            return value;
        }

        internal static PushDeliveryWorker PushWorker(HttpFixture f, Func<DateTime> clock)
        {
            return new PushDeliveryWorker(f.Database, f.Fakes.Push, f.Settings, f.Services.Logger) { Clock = clock };
        }

        internal static NotificationFanoutWorker FanoutWorker(HttpFixture f)
        {
            return new NotificationFanoutWorker(f.Database, f.Services.Notifications, f.Services.FeedPostEmails, f.Services.Summaries, f.Settings, f.Services.Logger);
        }

        internal static NotificationDraft Draft(NotificationTypeEnum type, string recipient, string? actor, TargetTypeEnum targetType = TargetTypeEnum.Comment, string? targetId = null)
        {
            return new NotificationDraft
            {
                RecipientUserId = recipient,
                Type = type,
                ActorUserId = actor,
                TargetType = targetType,
                TargetId = targetId ?? IdGenerator.Generate(targetType == TargetTypeEnum.FeedPost ? Constants.FeedPostIdPrefix : Constants.CommentIdPrefix),
                FeedPostId = "fpo_parent"
            };
        }

        internal static async Task<List<Notification>> ListAsync(HttpFixture f, string userId, CancellationToken ct)
        {
            List<Notification> all = new List<Notification>();
            DataTable table = await f.Raw.ExecuteQueryAsync("SELECT id FROM notifications WHERE recipientuserid = '" + userId + "' ORDER BY createdutc, id;", false, ct).ConfigureAwait(false);
            foreach (DataRow row in table.Rows)
            {
                Notification? n = await f.Database.Notifications.ReadAsync(RowReader.GetString(row, "id"), ct).ConfigureAwait(false);
                if (n != null) all.Add(n);
            }
            return all;
        }

        internal static async Task<long> CountAsync(HttpFixture f, string sql, CancellationToken ct)
        {
            DataTable table = await f.Raw.ExecuteQueryAsync(sql, false, ct).ConfigureAwait(false);
            return table.Rows.Count == 0 ? 0 : RowReader.GetLong(table.Rows[0], "cnt");
        }

        internal static List<string> Ids(JsonElement page)
        {
            return page.GetProperty("objects").EnumerateArray().Select(o => Api.Str(o, "id")).ToList();
        }

        internal static void AllDay(HttpFixture f)
        {
            f.Settings.Notifications.DefaultHoursStart = "00:00";
            f.Settings.Notifications.DefaultHoursEnd = "00:00";
        }
    }
}
