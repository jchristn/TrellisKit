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
    using TrellisKit.Core.Responses;
    using TrellisKit.Server.Services;

    /// <summary>
    /// Phase 2 content lifecycle (contract section 11) and the moderation hooks the moderation feature calls: account
    /// erasure of posts, comments, reactions, and mentions with counts recomputed; the keyset export batches; and the
    /// moderation state, moderator view, and open report hooks.
    /// </summary>
    public static class ContentLifecycleSuite
    {
        #region Public-Members

        /// <summary>
        /// Suite identifier.
        /// </summary>
        public const string Id = "ContentLifecycle";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the suite.
        /// </summary>
        /// <returns>Suite.</returns>
        public static TestSuiteDescriptor Build()
        {
            return new TestSuiteDescriptor(Id, "Content lifecycle and moderation hooks (" + TestEnvironment.Provider + ")", new[]
            {
                Case("ContentLifecycle.Erase", "Account erasure turns comments into Deleted placeholders, deletes posts, reactions, and mentions, and recomputes every count", EraseAsync),
                Case("ContentLifecycle.Export", "Export batches page through posts and comments with revisions and reactions by identifier", ExportAsync),
                Case("ContentLifecycle.ModerationHooks", "Moderation state hooks hide and restore posts and comments with counts recomputed; the moderator view carries the moderation object", HooksAsync)
            });
        }

        #endregion

        #region Private-Methods

        private static TestCaseDescriptor Case(string caseId, string name, Func<HttpFixture, CancellationToken, Task> body)
        {
            return SuiteCases.Http(Id, caseId, name, (f, ct) =>
            {
                ContentTestKit.Relax(f);
                return body(f, ct);
            });
        }

        private static async Task EraseAsync(HttpFixture f, CancellationToken ct)
        {
            TestMember leaving = await ContentTestKit.PosterAsync(f, ct, "Leaving Member", "leaving").ConfigureAwait(false);
            TestMember staying = await ContentTestKit.PosterAsync(f, ct, "Staying Member", "staying").ConfigureAwait(false);
            f.Settings.Content.EditGraceSeconds = 0;
            string theirPost = await ContentTestKit.PostIdAsync(f, leaving.AccessToken, "My post", ct).ConfigureAwait(false);
            string otherPost = await ContentTestKit.PostIdAsync(f, staying.AccessToken, "Hello @leaving", ct).ConfigureAwait(false);
            string root = await ContentTestKit.CommentIdAsync(f, leaving.AccessToken, otherPost, "My comment", null, ct).ConfigureAwait(false);
            await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/comments/" + root, leaving.AccessToken, new { body = "My edited comment", labels = new[] { "mine" } }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            string reply = await ContentTestKit.CommentIdAsync(f, staying.AccessToken, otherPost, "A reply", root, ct).ConfigureAwait(false);
            string leaf = await ContentTestKit.CommentIdAsync(f, leaving.AccessToken, otherPost, "My leaf", reply, ct).ConfigureAwait(false);
            await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/feed/" + otherPost + "/reactions/like", leaving.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/comments/" + reply + "/reactions/heart", leaving.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/feed/" + otherPost + "/reactions/idea", staying.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            ContentErasureResult result = await f.Services.ContentLifecycle.EraseForAccountAsync(leaving.Id, ct).ConfigureAwait(false);
            Check.Equal(2, result.Comments, "Comments erased");
            Check.Equal(1, result.FeedPosts, "Posts deleted");
            Check.Equal(2, result.Reactions, "Reactions deleted");
            Check.Equal(1, result.Mentions, "Mentions of the member deleted");
            Comment erased = (await f.Database.Comments.ReadAsync(root, ct).ConfigureAwait(false))!;
            Check.True(erased.DeletedUtc != null && erased.Body == null && erased.Excerpt == null && erased.DeletedReason == CommentDeletedReasonEnum.AccountDeleted && erased.RevisionCount == 0, "Comment erased");
            Check.Equal(0L, await ContentTestKit.ScalarAsync(f, "SELECT COUNT(*) AS cnt FROM commentrevisions WHERE commentid = '" + root + "';", ct).ConfigureAwait(false), "Revisions cleared");
            Check.Equal(0, (await f.Database.Metadata.ReadAsync(TargetTypeEnum.Comment, root, ct).ConfigureAwait(false)).Labels.Count, "Comment labels deleted");
            Check.True((await f.Database.FeedPosts.ReadAsync(theirPost, ct).ConfigureAwait(false))!.DeletedUtc != null, "Post soft-deleted");
            Check.Equal(0L, await ContentTestKit.ScalarAsync(f, "SELECT COUNT(*) AS cnt FROM mentions WHERE mentioneduserid = '" + leaving.Id + "';", ct).ConfigureAwait(false), "Mentions gone");
            Check.Equal(0L, await ContentTestKit.ScalarAsync(f, "SELECT COUNT(*) AS cnt FROM reactions WHERE userid = '" + leaving.Id + "';", ct).ConfigureAwait(false), "Reactions gone");
            await ContentTestKit.VerifyCountsAsync(f, otherPost, ct).ConfigureAwait(false);
            JsonElement tree = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/feed/" + otherPost + "/comments", staying.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            JsonElement placeholder = tree.GetProperty("objects")[0];
            Check.Equal("Deleted", Api.Str(placeholder, "state"), "Shown as a Deleted placeholder");
            Check.Equal(reply, Api.Str(placeholder.GetProperty("children")[0], "id"), "Replies stay readable");
            Check.Equal(0, placeholder.GetProperty("children")[0].GetProperty("children").GetArrayLength(), "The erased leaf is omitted");
            JsonElement post = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/feed/" + otherPost, staying.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal(1L, post.GetProperty("reactionTotal").GetInt64(), "Post reactions recomputed");
            Check.Equal(1, post.GetProperty("commentCount").GetInt32(), "commentCount recomputed");
            ContentErasureResult again = await f.Services.ContentLifecycle.EraseForAccountAsync(leaving.Id, ct).ConfigureAwait(false);
            Check.True(again.Comments == 0 && again.FeedPosts == 0 && again.Reactions == 0, "Idempotent");
            Check.True(leaf.Length > 0, "leaf");
        }

        private static async Task ExportAsync(HttpFixture f, CancellationToken ct)
        {
            TestMember m = await ContentTestKit.PosterAsync(f, ct).ConfigureAwait(false);
            f.Settings.Content.EditGraceSeconds = 0;
            List<string> posts = new List<string>();
            for (int i = 0; i < 3; i++) posts.Add(await ContentTestKit.PostIdAsync(f, m.AccessToken, "post " + i, ct).ConfigureAwait(false));
            await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/feed/" + posts[0], m.AccessToken, new { body = "post 0 edited" }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            string comment = await ContentTestKit.CommentIdAsync(f, m.AccessToken, posts[1], "comment", null, ct).ConfigureAwait(false);
            await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/comments/" + comment, m.AccessToken, new { body = "comment edited" }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            foreach (string p in posts) await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/feed/" + p + "/reactions/like", m.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            ContentLifecycleService lifecycle = f.Services.ContentLifecycle;
            List<FeedPostExportItem> exported = new List<FeedPostExportItem>();
            string? after = null;
            do
            {
                ContentExportPage<FeedPostExportItem> page = await lifecycle.ExportFeedPostsAsync(m.Id, after, 2, ct).ConfigureAwait(false);
                exported.AddRange(page.Objects);
                after = page.NextAfterId;
            }
            while (after != null);
            Check.True(exported.Select(p => p.Id).SequenceEqual(posts.OrderBy(p => p, StringComparer.Ordinal)), "Every post once, by identifier");
            FeedPostExportItem edited = exported.First(p => p.Id == posts[0]);
            Check.True(edited.Revisions.Count == 1 && edited.Revisions[0].Body == "post 0", "Post revisions exported");
            ContentExportPage<CommentExportItem> comments = await lifecycle.ExportCommentsAsync(m.Id, null, 10, ct).ConfigureAwait(false);
            Check.True(comments.Objects.Count == 1 && comments.Objects[0].Revisions.Count == 1 && comments.NextAfterId == null, "Comment with revisions");
            ContentExportPage<ReactionExportItem> first = await lifecycle.ExportReactionsAsync(m.Id, null, 2, ct).ConfigureAwait(false);
            ContentExportPage<ReactionExportItem> second = await lifecycle.ExportReactionsAsync(m.Id, first.NextAfterId, 2, ct).ConfigureAwait(false);
            Check.True(first.Objects.Count == 2 && second.Objects.Count == 1 && second.NextAfterId == null, "Reactions in keyset batches");
        }

        private static async Task HooksAsync(HttpFixture f, CancellationToken ct)
        {
            TestMember poster = await ContentTestKit.PosterAsync(f, ct).ConfigureAwait(false);
            TestMember member = await ContentTestKit.MemberAsync(f, ct).ConfigureAwait(false);
            TestMember moderator = await ContentTestKit.MemberAsync(f, ct).ConfigureAwait(false);
            await SafetyHelpers.GrantRoleAsync(f, moderator.Id, RoleEnum.FeedModerator, ct).ConfigureAwait(false);
            string post = await ContentTestKit.PostIdAsync(f, poster.AccessToken, "Hook", ct).ConfigureAwait(false);
            string root = await ContentTestKit.CommentIdAsync(f, member.AccessToken, post, "root", null, ct).ConfigureAwait(false);
            string child = await ContentTestKit.CommentIdAsync(f, member.AccessToken, post, "child", root, ct).ConfigureAwait(false);
            await ContentTestKit.CommentIdAsync(f, member.AccessToken, post, "grandchild", child, ct).ConfigureAwait(false);
            Comment hidden = await f.Services.Comments.SetModerationStateAsync(child, ModerationStateEnum.Hidden, moderator.Id, "Rude", ct).ConfigureAwait(false);
            Check.True(hidden.ModerationState == ModerationStateEnum.Hidden && hidden.ModeratedByUserId == moderator.Id && hidden.ModerationReason == "Rude" && hidden.ModeratedUtc != null, "Comment state stored");
            await ContentTestKit.VerifyCountsAsync(f, post, ct).ConfigureAwait(false);
            Comment rootRow = (await f.Database.Comments.ReadAsync(root, ct).ConfigureAwait(false))!;
            Check.True(rootRow.ChildCount == 0 && rootRow.DescendantCount == 1, "Ancestors recomputed");
            CommentView modComment = await f.Services.Comments.ModeratorViewAsync(await ContentTestKit.ActorAsync(f, moderator.Id, ct).ConfigureAwait(false), child, ct).ConfigureAwait(false);
            Check.True(modComment.Body == "child" && modComment.Moderation!.State == ModerationStateEnum.Hidden, "Moderator comment view");
            JsonElement moderatorTree = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/feed/" + post + "/comments", moderator.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            JsonElement seen = moderatorTree.GetProperty("objects")[0].GetProperty("children")[0];
            Check.True(Api.Str(seen, "body") == "child" && seen.GetProperty("viewer").GetProperty("canModerate").GetBoolean(), "FeedModerators see hidden content and may moderate");
            await f.Services.Comments.SetModerationStateAsync(child, ModerationStateEnum.None, moderator.Id, null, ct).ConfigureAwait(false);
            await ContentTestKit.VerifyCountsAsync(f, post, ct).ConfigureAwait(false);
            await Check.ThrowsAsync<TrellisKit.Core.Exceptions.ApiErrorException>(() => f.Services.Comments.SetModerationStateAsync("cmt_missing", ModerationStateEnum.Hidden, null, null, ct), "Unknown comment").ConfigureAwait(false);

            FeedPost removed = await f.Services.Feed.SetModerationStateAsync(post, ModerationStateEnum.Removed, moderator.Id, "Spam", ct).ConfigureAwait(false);
            Check.Equal(ModerationStateEnum.Removed, removed.ModerationState, "Post removed");
            await Api.ExpectErrorAsync(f, HttpMethod.Get, "/api/v1/feed/" + post, member.AccessToken, null, HttpStatusCode.NotFound, "NotFound", ct).ConfigureAwait(false);
            JsonElement byModerator = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/feed/" + post, moderator.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal("Removed", Api.Str(byModerator, "state"), "FeedModerators see Removed posts");
            Check.Equal("Spam", Api.Str(byModerator.GetProperty("moderation"), "reason"), "Moderation object for moderators");
            JsonElement byAuthor = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/feed/" + post, poster.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal("Removed", Api.Str(byAuthor.GetProperty("moderation"), "state"), "Moderation object for the author");
            Check.True(!ContentTestKit.Ids(await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/feed", member.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false)).Contains(post), "Removed posts leave the feed");
            FeedPostView modPost = await f.Services.Feed.ModeratorViewAsync(await ContentTestKit.ActorAsync(f, moderator.Id, ct).ConfigureAwait(false), post, ct).ConfigureAwait(false);
            Check.True(modPost.Moderation != null && modPost.Moderation.ActedUtc != null && modPost.State == FeedPostStateEnum.Removed, "Moderator post view");
            await f.Services.Feed.SetModerationStateAsync(post, ModerationStateEnum.None, moderator.Id, null, ct).ConfigureAwait(false);
            await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/feed/" + post, member.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal(0, await f.Services.Feed.RecomputeOpenReportCountAsync(post, ct).ConfigureAwait(false), "No open reports");
            await Check.ThrowsAsync<TrellisKit.Core.Exceptions.ApiErrorException>(() => f.Services.Feed.SetModerationStateAsync("fpo_missing", ModerationStateEnum.Hidden, null, null, ct), "Unknown post").ConfigureAwait(false);
        }

        #endregion
    }
}
