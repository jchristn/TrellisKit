namespace Test.Shared.Suites
{
    using System;
    using System.Collections.Generic;
    using System.Data;
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
    /// Helpers of the content suites (feed posts, comments, reactions).
    /// </summary>
    internal static class ContentTestKit
    {
        internal static Task<TestMember> MemberAsync(HttpFixture f, CancellationToken ct, string displayName = "Content Member", string? handle = null)
        {
            return SafetyHelpers.MemberAsync(f, ct, displayName, handle);
        }

        internal static void Relax(HttpFixture f)
        {
            f.Settings.Comments.NewAccountPerMinute = 10000;
            f.Settings.Comments.NewAccountPerDay = 10000;
            f.Settings.Comments.PerMinute = 1000;
            f.Settings.Comments.PerDay = 100000;
        }

        internal static async Task<TestMember> PosterAsync(HttpFixture f, CancellationToken ct, string displayName = "Feed Poster", string? handle = null)
        {
            TestMember member = await SafetyHelpers.MemberAsync(f, ct, displayName, handle).ConfigureAwait(false);
            await SafetyHelpers.UpdateUserAsync(f, member.Id, u => u.CanPostToFeed = true, ct).ConfigureAwait(false);
            return member;
        }

        internal static async Task<TestMember> AdminAsync(HttpFixture f, CancellationToken ct)
        {
            TestMember admin = await Api.AdminAsync(f, ct).ConfigureAwait(false);
            return admin;
        }

        internal static async Task<JsonElement> PostAsync(HttpFixture f, string bearer, object body, CancellationToken ct)
        {
            return await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/feed", bearer, body, HttpStatusCode.Created, ct).ConfigureAwait(false);
        }

        internal static async Task<string> PostIdAsync(HttpFixture f, string bearer, string text, CancellationToken ct)
        {
            JsonElement post = await PostAsync(f, bearer, new { body = text }, ct).ConfigureAwait(false);
            return Api.Str(post, "id");
        }

        internal static async Task<JsonElement> CommentAsync(HttpFixture f, string bearer, string postId, string text, string? parentId, CancellationToken ct)
        {
            return await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/feed/" + postId + "/comments", bearer, new { body = text, parentCommentId = parentId }, HttpStatusCode.Created, ct).ConfigureAwait(false);
        }

        internal static async Task<string> CommentIdAsync(HttpFixture f, string bearer, string postId, string text, string? parentId, CancellationToken ct)
        {
            return Api.Str(await CommentAsync(f, bearer, postId, text, parentId, ct).ConfigureAwait(false), "id");
        }

        internal static async Task<string> MediaAsync(HttpFixture f, string ownerUserId, MediaKindEnum kind, CancellationToken ct, MediaStatusEnum status = MediaStatusEnum.Ready, string? altText = null)
        {
            DateTime now = DateTime.UtcNow;
            MediaAttachment media = new MediaAttachment
            {
                Id = IdGenerator.Generate(Constants.MediaIdPrefix),
                OwnerUserId = ownerUserId,
                Kind = kind,
                Status = status,
                ContentType = kind == MediaKindEnum.Video ? "video/mp4" : "image/png",
                SizeBytes = 1000,
                Width = 40,
                Height = 40,
                AltText = altText,
                SourceKey = "test/" + Guid.NewGuid().ToString("N"),
                ReadyUtc = status == MediaStatusEnum.Ready ? now : null,
                UploadExpiresUtc = now.AddHours(1),
                CreatedUtc = now,
                UpdatedUtc = now
            };
            await f.Database.Media.CreateAsync(media, ct).ConfigureAwait(false);
            return media.Id;
        }

        internal static List<string> Ids(JsonElement page)
        {
            return page.GetProperty("objects").EnumerateArray().Select(o => Api.Str(o, "id")).ToList();
        }

        internal static async Task<long> ScalarAsync(HttpFixture f, string sql, CancellationToken ct)
        {
            DataTable table = await f.Raw.ExecuteQueryAsync(sql, false, ct).ConfigureAwait(false);
            return table.Rows.Count == 0 ? 0 : RowReader.GetLong(table.Rows[0], "cnt");
        }

        internal static Task<int> PublishAsync(HttpFixture f, CancellationToken ct)
        {
            FeedPublishWorker worker = new FeedPublishWorker(f.Database, f.Services.Feed, f.Settings, f.Services.Logger);
            return worker.RunOnceAsync(ct);
        }

        internal static void CheckMetadata(JsonElement item, string what)
        {
            Check.Equal(JsonValueKind.Array, item.GetProperty("labels").ValueKind, what + ": labels is a list");
            Check.Equal(JsonValueKind.Object, item.GetProperty("tags").ValueKind, what + ": tags is an object");
        }

        internal static async Task VerifyCountsAsync(HttpFixture f, string postId, CancellationToken ct)
        {
            DataTable rows = await f.Raw.ExecuteQueryAsync("SELECT id, parentcommentid, path, childcount, descendantcount, reactiontotal, score, deletedutc, moderationstate FROM comments WHERE targetid = '" + postId + "';", false, ct).ConfigureAwait(false);
            List<CommentRow> comments = rows.Rows.Cast<DataRow>().Select(r => new CommentRow
            {
                Id = RowReader.GetString(r, "id"),
                ParentId = RowReader.GetNullableString(r, "parentcommentid"),
                Path = RowReader.GetString(r, "path"),
                ChildCount = RowReader.GetInt(r, "childcount"),
                DescendantCount = RowReader.GetInt(r, "descendantcount"),
                ReactionTotal = RowReader.GetInt(r, "reactiontotal"),
                Score = RowReader.GetInt(r, "score"),
                Visible = RowReader.GetNullableDateTime(r, "deletedutc") == null && RowReader.GetString(r, "moderationstate") == "None"
            }).ToList();
            foreach (CommentRow c in comments)
            {
                int children = comments.Count(k => k.ParentId == c.Id && k.Visible);
                int descendants = comments.Count(k => k.Visible && k.Path.Length > c.Path.Length && k.Path.StartsWith(c.Path, StringComparison.Ordinal));
                long reactions = await ScalarAsync(f, "SELECT COUNT(*) AS cnt FROM reactions WHERE targettype = 'Comment' AND targetid = '" + c.Id + "';", ct).ConfigureAwait(false);
                Check.Equal(children, c.ChildCount, "childcount of " + c.Id);
                Check.Equal(descendants, c.DescendantCount, "descendantcount of " + c.Id);
                Check.Equal((int)reactions, c.ReactionTotal, "reactiontotal of " + c.Id);
                Check.Equal(c.ReactionTotal + 2 * children, c.Score, "score of " + c.Id);
            }
            FeedPost post = (await f.Database.FeedPosts.ReadAsync(postId, ct).ConfigureAwait(false))!;
            Check.Equal(comments.Count(c => c.Visible), post.CommentCount, "commentcount of " + postId);
            long postReactions = await ScalarAsync(f, "SELECT COUNT(*) AS cnt FROM reactions WHERE targettype = 'FeedPost' AND targetid = '" + postId + "';", ct).ConfigureAwait(false);
            Check.Equal((int)postReactions, post.ReactionTotal, "reactiontotal of " + postId);
        }

        internal static async Task<ContentActor> ActorAsync(HttpFixture f, string userId, CancellationToken ct)
        {
            return await f.Services.Actors.ForUserAsync(userId, ct).ConfigureAwait(false);
        }
    }
}
