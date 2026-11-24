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

    /// <summary>
    /// Helpers of the Phase 3 forum suites.
    /// </summary>
    internal static class ForumTestKit
    {
        internal const string DefaultSlug = "general-discussion";

        internal static void Relax(HttpFixture f)
        {
            ContentTestKit.Relax(f);
            f.Settings.Forums.ThreadsPerHour = 10000;
            f.Settings.Forums.ThreadsPerDay = 100000;
            f.Settings.Forums.NewAccountThreadsPerHour = 10000;
            f.Settings.Forums.NewAccountThreadsPerDay = 100000;
            f.Settings.Forums.FollowsPerMinute = 10000;
            f.Settings.Forums.SearchPerMinute = 10000;
            f.Settings.Forums.PromotionsPerHour = 10000;
            f.Settings.Polls.VotesPerMinute = 10000;
            f.Settings.Search.PerMinute = 10000;
            f.Settings.Reactions.PerMinute = 10000;
        }

        internal static async Task<string> DefaultForumIdAsync(HttpFixture f, string bearer, CancellationToken ct)
        {
            JsonElement forum = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/forums/" + DefaultSlug, bearer, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            return Api.Str(forum, "id");
        }

        internal static async Task<JsonElement> CreateForumAsync(HttpFixture f, string adminBearer, string name, CancellationToken ct, object? extra = null)
        {
            Dictionary<string, object?> body = new Dictionary<string, object?> { { "name", name }, { "description", name + " description" } };
            if (extra != null)
            {
                foreach (System.Reflection.PropertyInfo p in extra.GetType().GetProperties()) body[p.Name] = p.GetValue(extra);
            }
            return await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/admin/forums", adminBearer, body, HttpStatusCode.Created, ct).ConfigureAwait(false);
        }

        internal static async Task<JsonElement> ThreadAsync(HttpFixture f, string bearer, string forumId, object body, CancellationToken ct)
        {
            return await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/forums/" + forumId + "/threads", bearer, body, HttpStatusCode.Created, ct).ConfigureAwait(false);
        }

        internal static async Task<string> ThreadIdAsync(HttpFixture f, string bearer, string forumId, string title, CancellationToken ct, string body = "Thread body text")
        {
            JsonElement thread = await ThreadAsync(f, bearer, forumId, new { type = "Discussion", title, body }, ct).ConfigureAwait(false);
            return Api.Str(thread, "id");
        }

        internal static async Task<JsonElement> PollThreadAsync(HttpFixture f, string bearer, string forumId, CancellationToken ct, bool multiple = false, bool allowChange = true, string resultsVisibility = "Always", string voterVisibility = "Visible")
        {
            return await ThreadAsync(f, bearer, forumId, new
            {
                type = "Poll",
                title = "Which meetup time works best?",
                body = "Pick one.",
                poll = new
                {
                    options = new[] { new { label = "Closed" }, new { label = "Half" }, new { label = "Open" } },
                    multipleChoice = multiple,
                    maxChoices = multiple ? (int?)2 : null,
                    allowVoteChange = allowChange,
                    resultsVisibility,
                    voterVisibility
                }
            }, ct).ConfigureAwait(false);
        }

        internal static async Task<JsonElement> CommentAsync(HttpFixture f, string bearer, string threadId, string text, string? parentId, CancellationToken ct)
        {
            return await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/threads/" + threadId + "/comments", bearer, new { body = text, parentCommentId = parentId }, HttpStatusCode.Created, ct).ConfigureAwait(false);
        }

        internal static async Task<string> CommentIdAsync(HttpFixture f, string bearer, string threadId, string text, CancellationToken ct, string? parentId = null)
        {
            return Api.Str(await CommentAsync(f, bearer, threadId, text, parentId, ct).ConfigureAwait(false), "id");
        }

        internal static async Task GrantForumModeratorAsync(HttpFixture f, string userId, string forumId, CancellationToken ct)
        {
            await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/admin/moderators", HttpFixture.AdminToken, new { userId, resourceType = "Forum", resourceId = forumId }, HttpStatusCode.OK, ct).ConfigureAwait(false);
        }

        internal static List<string> Ids(JsonElement page)
        {
            return page.GetProperty("objects").EnumerateArray().Select(o => Api.Str(o, "id")).ToList();
        }

        internal static Task<long> CountAsync(HttpFixture f, string sql, CancellationToken ct)
        {
            return ContentTestKit.ScalarAsync(f, sql, ct);
        }

        internal static async Task<string> FeedbackThreadAsync(HttpFixture f, TestMember author, string forumId, CancellationToken ct)
        {
            string video = await ContentTestKit.MediaAsync(f, author.Id, MediaKindEnum.Video, ct).ConfigureAwait(false);
            JsonElement thread = await ThreadAsync(f, author.AccessToken, forumId, new { type = "Feedback", title = "Review my presentation", body = "Recorded today.", mediaIds = new[] { video }, feedback = new { focus = "Pacing" } }, ct).ConfigureAwait(false);
            return Api.Str(thread, "id");
        }

        internal static async Task<int> DrainSearchAsync(HttpFixture f, CancellationToken ct)
        {
            await f.Services.Forums.Search.SweepAsync(ct).ConfigureAwait(false);
            int total = 0;
            for (int i = 0; i < 20; i++)
            {
                int n = await f.Services.Forums.Search.DrainAsync(500, ct).ConfigureAwait(false);
                total += n;
                if (n == 0) break;
            }
            return total;
        }

        internal static async Task<List<Notification>> NotificationsAsync(HttpFixture f, string userId, NotificationTypeEnum type, CancellationToken ct)
        {
            List<Notification> all = await NotificationTestKit.ListAsync(f, userId, ct).ConfigureAwait(false);
            return all.Where(n => n.Type == type).ToList();
        }

        internal static async Task<int> FanoutAsync(HttpFixture f, CancellationToken ct)
        {
            int total = 0;
            for (int i = 0; i < 10; i++)
            {
                int n = await NotificationTestKit.FanoutWorker(f).RunOnceAsync(ct).ConfigureAwait(false);
                total += n;
                if (n == 0) break;
            }
            return total;
        }

        internal static string NewId(string prefix)
        {
            return IdGenerator.Generate(prefix);
        }
    }
}
