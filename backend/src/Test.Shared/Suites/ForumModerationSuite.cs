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
    using TrellisKit.Server.Services;
    using TrellisKit.Server.Workers;

    /// <summary>
    /// Phase 3 forum moderation and cross-cutting behavior (docs/PHASE3_CONTRACT.md sections 8, 9, 11 to 14): lock,
    /// move, forum mutes, reports in Forum scope, scoped forum moderators, forum to feed portability (promote and
    /// discuss with one conversation), trending, search, public samples, access rules, and account deletion and export.
    /// </summary>
    public static class ForumModerationSuite
    {
        #region Public-Members

        /// <summary>
        /// Suite identifier.
        /// </summary>
        public const string Id = "ForumModeration";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the suite.
        /// </summary>
        /// <returns>Suite.</returns>
        public static TestSuiteDescriptor Build()
        {
            return new TestSuiteDescriptor(Id, "Forum moderation (" + TestEnvironment.Provider + ")", new[]
            {
                Case("Moderation.LockAndMove", "Locked threads refuse comments and votes; moving narrows to the target forum and is recorded as a moderation action", LockAndMoveAsync),
                Case("Moderation.ForumMute", "A forum mute blocks posting, commenting, reacting, and voting in that forum only; lifting it restores access", ForumMuteAsync),
                Case("Moderation.ScopedModerators", "A forum moderator acts only in their forum; reports on threads file in Forum scope; the queue filters by scope", ScopedModeratorAsync),
                Case("Moderation.HideThread", "Hiding a thread through moderation actions hides it from members and keeps it for staff", HideThreadAsync),
                Case("Portability.Promote", "Promoting a thread makes a feed card whose conversation is the thread; card comments answer UseCommentTarget; unpromote removes it", PromoteAsync),
                Case("Portability.Discuss", "Discussing a feed post makes one LinkedFeedPost thread whose conversation is the feed post", DiscussAsync),
                Case("Trending.Recompute", "The trending pass scores active threads, zeroes old ones, and the admin queue orders by score", TrendingAsync),
                Case("Search.Index", "The indexer makes threads, comments, and posts searchable with snippets; deleted and hidden content drops out", SearchAsync),
                Case("Public.Samples", "Public threads in Public forums are listed anonymously with Cache-Control; member threads are not", PublicAsync),
                Case("Access.Rules", "Anonymous callers get 401; members without membership get 402; members cannot use moderator routes", AccessAsync),
                Case("Account.EraseAndExport", "Export carries threads, poll votes, follows, and mutes; deleting the account removes threads, votes, follows, and mutes", AccountAsync)
            });
        }

        #endregion

        #region Private-Methods

        private static TestCaseDescriptor Case(string caseId, string name, Func<HttpFixture, CancellationToken, Task> body)
        {
            return SuiteCases.Http(Id, caseId, name, (f, ct) =>
            {
                ForumTestKit.Relax(f);
                return body(f, ct);
            });
        }

        private static async Task LockAndMoveAsync(HttpFixture f, CancellationToken ct)
        {
            TestMember admin = await ContentTestKit.AdminAsync(f, ct).ConfigureAwait(false);
            TestMember author = await ContentTestKit.MemberAsync(f, ct, "Lock Author").ConfigureAwait(false);
            TestMember other = await ContentTestKit.MemberAsync(f, ct, "Lock Other").ConfigureAwait(false);
            string forumId = await ForumTestKit.DefaultForumIdAsync(f, author.AccessToken, ct).ConfigureAwait(false);
            JsonElement poll = await ForumTestKit.PollThreadAsync(f, author.AccessToken, forumId, ct).ConfigureAwait(false);
            string threadId = Api.Str(poll, "id");
            string pollId = Api.Str(poll.GetProperty("poll"), "id");
            string option = Api.Str(poll.GetProperty("poll").GetProperty("options")[0], "id");
            await Api.ExpectErrorAsync(f, HttpMethod.Post, "/api/v1/threads/" + threadId + "/lock", other.AccessToken, new { reason = "No" }, HttpStatusCode.Forbidden, "Forbidden", ct).ConfigureAwait(false);
            JsonElement locked = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/threads/" + threadId + "/lock", admin.AccessToken, new { reason = "Heated" }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.True(locked.GetProperty("isLocked").GetBoolean(), "Locked");
            await Api.ExpectErrorAsync(f, HttpMethod.Post, "/api/v1/threads/" + threadId + "/comments", other.AccessToken, new { body = "Late reply" }, HttpStatusCode.Conflict, "ThreadLocked", ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Put, "/api/v1/polls/" + pollId + "/votes", other.AccessToken, new { optionIds = new[] { option } }, HttpStatusCode.Conflict, "ThreadLocked", ct).ConfigureAwait(false);
            JsonElement seen = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/threads/" + threadId, other.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.True(!seen.GetProperty("viewer").GetProperty("canComment").GetBoolean(), "viewer.canComment false when locked");
            await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/threads/" + threadId + "/unlock", admin.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            await ForumTestKit.CommentIdAsync(f, other.AccessToken, threadId, "Now it works", ct).ConfigureAwait(false);
            JsonElement target = await ForumTestKit.CreateForumAsync(f, admin.AccessToken, "Polls Corner", ct).ConfigureAwait(false);
            string targetId = Api.Str(target, "id");
            JsonElement moved = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/threads/" + threadId + "/move", admin.AccessToken, new { forumId = targetId, reason = "Better fit", notifyAuthor = true }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal(targetId, moved.GetProperty("forum").GetProperty("id").GetString(), "Moved");
            long actions = await ForumTestKit.CountAsync(f, "SELECT COUNT(*) AS cnt FROM moderationactions WHERE targetid = '" + threadId + "' AND action IN ('Lock', 'Unlock', 'Move');", ct).ConfigureAwait(false);
            Check.Equal(3L, actions, "Lock, unlock, and move are moderation actions");
            List<Notification> movedNote = await ForumTestKit.NotificationsAsync(f, author.Id, NotificationTypeEnum.ThreadMoved, ct).ConfigureAwait(false);
            Check.Equal(1, movedNote.Count, "The author is told about the move");
            JsonElement history = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/admin/moderation/actions/enumerate", admin.AccessToken, new { filters = new { targetId = threadId } }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.True(history.GetProperty("objects").GetArrayLength() >= 3, "Actions log lists thread actions");
        }

        private static async Task ForumMuteAsync(HttpFixture f, CancellationToken ct)
        {
            TestMember admin = await ContentTestKit.AdminAsync(f, ct).ConfigureAwait(false);
            TestMember author = await ContentTestKit.MemberAsync(f, ct, "Mute Author").ConfigureAwait(false);
            TestMember muted = await ContentTestKit.MemberAsync(f, ct, "Muted Member").ConfigureAwait(false);
            string forumId = await ForumTestKit.DefaultForumIdAsync(f, author.AccessToken, ct).ConfigureAwait(false);
            JsonElement otherForum = await ForumTestKit.CreateForumAsync(f, admin.AccessToken, "Elsewhere", ct).ConfigureAwait(false);
            string otherForumId = Api.Str(otherForum, "id");
            JsonElement poll = await ForumTestKit.PollThreadAsync(f, author.AccessToken, forumId, ct).ConfigureAwait(false);
            string threadId = Api.Str(poll, "id");
            string pollId = Api.Str(poll.GetProperty("poll"), "id");
            string option = Api.Str(poll.GetProperty("poll").GetProperty("options")[0], "id");
            JsonElement mute = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/admin/moderation/forum-mutes", admin.AccessToken, new { userId = muted.Id, forumId, reason = "Cool off for a while" }, HttpStatusCode.Created, ct).ConfigureAwait(false);
            string muteId = Api.Str(mute, "id");
            await Api.ExpectErrorAsync(f, HttpMethod.Post, "/api/v1/forums/" + forumId + "/threads", muted.AccessToken, new { type = "Discussion", title = "Muted post", body = "x" }, HttpStatusCode.Forbidden, "ForumMuted", ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Post, "/api/v1/threads/" + threadId + "/comments", muted.AccessToken, new { body = "Muted comment" }, HttpStatusCode.Forbidden, "ForumMuted", ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Put, "/api/v1/threads/" + threadId + "/reactions/" + f.Property.Reactions[0].Key, muted.AccessToken, null, HttpStatusCode.Forbidden, "ForumMuted", ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Put, "/api/v1/polls/" + pollId + "/votes", muted.AccessToken, new { optionIds = new[] { option } }, HttpStatusCode.Forbidden, "ForumMuted", ct).ConfigureAwait(false);
            await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/threads/" + threadId, muted.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            await ForumTestKit.ThreadIdAsync(f, muted.AccessToken, otherForumId, "Allowed elsewhere", ct).ConfigureAwait(false);
            JsonElement list = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/admin/moderation/forum-mutes/enumerate", admin.AccessToken, new { filters = new { forumId } }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal(1, list.GetProperty("objects").GetArrayLength(), "Mute listed");
            List<Notification> notes = await NotificationTestKit.ListAsync(f, muted.Id, ct).ConfigureAwait(false);
            Check.True(notes.Any(n => n.Type == NotificationTypeEnum.ModerationAction), "The member is told about the mute");
            await Api.ExpectAsync(f, HttpMethod.Delete, "/api/v1/admin/moderation/forum-mutes/" + muteId, admin.AccessToken, new { reason = "Done" }, HttpStatusCode.NoContent, ct).ConfigureAwait(false);
            await ForumTestKit.CommentIdAsync(f, muted.AccessToken, threadId, "Back again", ct).ConfigureAwait(false);
            Check.Equal(1L, await ForumTestKit.CountAsync(f, "SELECT COUNT(*) AS cnt FROM moderationactions WHERE action = 'ForumMute';", ct).ConfigureAwait(false), "ForumMute recorded");
            Check.Equal(1L, await ForumTestKit.CountAsync(f, "SELECT COUNT(*) AS cnt FROM moderationactions WHERE action = 'ForumUnmute';", ct).ConfigureAwait(false), "ForumUnmute recorded");
        }

        private static async Task ScopedModeratorAsync(HttpFixture f, CancellationToken ct)
        {
            TestMember admin = await ContentTestKit.AdminAsync(f, ct).ConfigureAwait(false);
            TestMember author = await ContentTestKit.MemberAsync(f, ct, "Scoped Author").ConfigureAwait(false);
            TestMember reporter = await ContentTestKit.MemberAsync(f, ct, "Reporter").ConfigureAwait(false);
            TestMember moderator = await ContentTestKit.MemberAsync(f, ct, "Forum Moderator").ConfigureAwait(false);
            string forumId = await ForumTestKit.DefaultForumIdAsync(f, author.AccessToken, ct).ConfigureAwait(false);
            JsonElement other = await ForumTestKit.CreateForumAsync(f, admin.AccessToken, "Off Limits", ct).ConfigureAwait(false);
            string otherId = Api.Str(other, "id");
            string inScope = await ForumTestKit.ThreadIdAsync(f, author.AccessToken, forumId, "In scope thread", ct).ConfigureAwait(false);
            string outScope = await ForumTestKit.ThreadIdAsync(f, author.AccessToken, otherId, "Out of scope thread", ct).ConfigureAwait(false);
            await ForumTestKit.GrantForumModeratorAsync(f, moderator.Id, forumId, ct).ConfigureAwait(false);
            JsonElement moderators = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/admin/forums/" + forumId + "/moderators/enumerate", admin.AccessToken, new { }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.True(moderators.GetProperty("objects").EnumerateArray().Any(m => m.GetProperty("user").GetProperty("id").GetString() == moderator.Id), "Listed as the forum's moderator");
            await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/threads/" + inScope + "/pin", moderator.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Post, "/api/v1/threads/" + outScope + "/pin", moderator.AccessToken, null, HttpStatusCode.Forbidden, "Forbidden", ct).ConfigureAwait(false);
            JsonElement admins = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/admin/threads/enumerate", moderator.AccessToken, new { }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            List<string> visible = ForumTestKit.Ids(admins);
            Check.True(visible.Contains(inScope) && !visible.Contains(outScope), "Admin thread list is limited to the moderator's forums");
            await ModerationTestKit.ReportOkAsync(f, reporter.AccessToken, "Thread", inScope, ct).ConfigureAwait(false);
            await ModerationTestKit.ReportOkAsync(f, reporter.AccessToken, "Thread", outScope, ct).ConfigureAwait(false);
            Check.Equal(2L, await ForumTestKit.CountAsync(f, "SELECT COUNT(*) AS cnt FROM reports WHERE scopetype = 'Forum';", ct).ConfigureAwait(false), "Thread reports file in Forum scope");
            JsonElement queue = await Api.ExpectAsync(f, HttpMethod.Post, ModerationTestKit.Queue + "/enumerate", moderator.AccessToken, new { }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            List<string> queued = ModerationTestKit.TargetIds(queue);
            Check.True(queued.Contains(inScope) && !queued.Contains(outScope), "The moderator's queue holds only their forum");
            JsonElement row = queue.GetProperty("objects").EnumerateArray().First(o => Api.Str(o, "targetId") == inScope);
            Check.Equal("Forum", row.GetProperty("scope").GetProperty("type").GetString(), "Queue row scope");
            JsonElement adminQueue = await Api.ExpectAsync(f, HttpMethod.Post, ModerationTestKit.Queue + "/enumerate", admin.AccessToken, new { filters = new { scopeType = "Forum", forumIds = new[] { otherId } } }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            List<string> filtered = ModerationTestKit.TargetIds(adminQueue);
            Check.True(filtered.Contains(outScope) && !filtered.Contains(inScope), "forumIds filter");
            await Api.ExpectErrorAsync(f, HttpMethod.Post, "/api/v1/admin/forums", moderator.AccessToken, new { name = "Nope" }, HttpStatusCode.Forbidden, "Forbidden", ct).ConfigureAwait(false);
            await Api.ExpectAsync(f, HttpMethod.Delete, "/api/v1/admin/moderators/" + moderator.Id + "?resourceType=Forum&resourceId=" + forumId, HttpFixture.AdminToken, null, HttpStatusCode.NoContent, ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Post, "/api/v1/threads/" + inScope + "/unpin", moderator.AccessToken, null, HttpStatusCode.Forbidden, "Forbidden", ct).ConfigureAwait(false);
        }

        private static async Task HideThreadAsync(HttpFixture f, CancellationToken ct)
        {
            TestMember admin = await ContentTestKit.AdminAsync(f, ct).ConfigureAwait(false);
            TestMember author = await ContentTestKit.MemberAsync(f, ct, "Hidden Author").ConfigureAwait(false);
            TestMember reader = await ContentTestKit.MemberAsync(f, ct, "Hidden Reader").ConfigureAwait(false);
            string forumId = await ForumTestKit.DefaultForumIdAsync(f, author.AccessToken, ct).ConfigureAwait(false);
            string threadId = await ForumTestKit.ThreadIdAsync(f, author.AccessToken, forumId, "Something to hide", ct).ConfigureAwait(false);
            await ModerationTestKit.ActOkAsync(f, admin.AccessToken, "Thread", threadId, "Hide", ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Get, "/api/v1/threads/" + threadId, reader.AccessToken, null, HttpStatusCode.NotFound, "NotFound", ct).ConfigureAwait(false);
            JsonElement own = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/threads/" + threadId, author.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal("Hidden", Api.Str(own, "state"), "The author sees the Hidden state");
            JsonElement staff = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/admin/threads/enumerate", admin.AccessToken, new { filters = new { state = "Hidden" } }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.True(ForumTestKit.Ids(staff).Contains(threadId), "Staff list Hidden threads");
            JsonElement members = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/forums/" + forumId + "/threads/enumerate", reader.AccessToken, new { }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.True(!ForumTestKit.Ids(members).Contains(threadId), "Hidden threads leave member lists");
            await ModerationTestKit.ActOkAsync(f, admin.AccessToken, "Thread", threadId, "Unhide", ct).ConfigureAwait(false);
            await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/threads/" + threadId, reader.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
        }

        private static async Task PromoteAsync(HttpFixture f, CancellationToken ct)
        {
            TestMember admin = await ContentTestKit.AdminAsync(f, ct).ConfigureAwait(false);
            TestMember author = await ContentTestKit.MemberAsync(f, ct, "Promoted Author").ConfigureAwait(false);
            TestMember reader = await ContentTestKit.MemberAsync(f, ct, "Feed Reader").ConfigureAwait(false);
            string forumId = await ForumTestKit.DefaultForumIdAsync(f, author.AccessToken, ct).ConfigureAwait(false);
            string threadId = await ForumTestKit.ThreadIdAsync(f, author.AccessToken, forumId, "A great breakdown", ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Post, "/api/v1/threads/" + threadId + "/promote", reader.AccessToken, new { }, HttpStatusCode.Forbidden, "PromotionNotAllowed", ct).ConfigureAwait(false);
            JsonElement card = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/threads/" + threadId + "/promote", admin.AccessToken, new { title = "Featured breakdown" }, HttpStatusCode.Created, ct).ConfigureAwait(false);
            string cardId = Api.Str(card, "id");
            Check.Equal("Thread", card.GetProperty("commentTarget").GetProperty("type").GetString(), "The card's conversation is the thread");
            Check.Equal(threadId, card.GetProperty("commentTarget").GetProperty("id").GetString(), "commentTarget id");
            await Api.ExpectErrorAsync(f, HttpMethod.Post, "/api/v1/threads/" + threadId + "/promote", admin.AccessToken, new { }, HttpStatusCode.Conflict, "AlreadyPromoted", ct).ConfigureAwait(false);
            JsonElement feed = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/feed/enumerate", reader.AccessToken, new { }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.True(ContentTestKit.Ids(feed).Contains(cardId), "The card is in the feed");
            await Api.ExpectErrorAsync(f, HttpMethod.Post, "/api/v1/feed/" + cardId + "/comments", reader.AccessToken, new { body = "On the card" }, HttpStatusCode.Conflict, "UseCommentTarget", ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Put, "/api/v1/feed/" + cardId + "/reactions/" + f.Property.Reactions[0].Key, reader.AccessToken, null, HttpStatusCode.Conflict, "UseCommentTarget", ct).ConfigureAwait(false);
            await ForumTestKit.CommentIdAsync(f, reader.AccessToken, threadId, "On the thread", ct).ConfigureAwait(false);
            JsonElement thread = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/threads/" + threadId, reader.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal(cardId, thread.GetProperty("promotion").GetProperty("feedPostId").GetString(), "Thread shows its promotion");
            List<Notification> promoted = await ForumTestKit.NotificationsAsync(f, author.Id, NotificationTypeEnum.ThreadPromoted, ct).ConfigureAwait(false);
            Check.Equal(1, promoted.Count, "The author is told about the promotion");
            await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/threads/" + threadId + "/unpromote", admin.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            JsonElement after = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/feed/enumerate", reader.AccessToken, new { }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.True(!ContentTestKit.Ids(after).Contains(cardId), "The card leaves the feed");
        }

        private static async Task DiscussAsync(HttpFixture f, CancellationToken ct)
        {
            TestMember admin = await ContentTestKit.AdminAsync(f, ct).ConfigureAwait(false);
            TestMember reader = await ContentTestKit.MemberAsync(f, ct, "Discuss Reader").ConfigureAwait(false);
            string forumId = await ForumTestKit.DefaultForumIdAsync(f, admin.AccessToken, ct).ConfigureAwait(false);
            string postId = await ContentTestKit.PostIdAsync(f, admin.AccessToken, "Big announcement for everyone", ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Post, "/api/v1/feed/" + postId + "/discuss", reader.AccessToken, new { forumId }, HttpStatusCode.Forbidden, "Forbidden", ct).ConfigureAwait(false);
            JsonElement thread = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/feed/" + postId + "/discuss", admin.AccessToken, new { forumId, title = "Discuss the announcement" }, HttpStatusCode.Created, ct).ConfigureAwait(false);
            string threadId = Api.Str(thread, "id");
            Check.Equal("LinkedFeedPost", Api.Str(thread, "kind"), "Linked thread");
            Check.Equal("FeedPost", thread.GetProperty("commentTarget").GetProperty("type").GetString(), "The conversation is the feed post");
            Check.Equal(postId, thread.GetProperty("commentTarget").GetProperty("id").GetString(), "commentTarget id");
            ApiResult again = await Api.SendAsync(f, HttpMethod.Post, "/api/v1/feed/" + postId + "/discuss", admin.AccessToken, new { forumId }, ct).ConfigureAwait(false);
            Check.Equal(HttpStatusCode.Conflict, again.Status, "One linked thread per post");
            await Api.ExpectErrorAsync(f, HttpMethod.Post, "/api/v1/threads/" + threadId + "/comments", reader.AccessToken, new { body = "Wrong place" }, HttpStatusCode.Conflict, "UseCommentTarget", ct).ConfigureAwait(false);
            await ContentTestKit.CommentIdAsync(f, reader.AccessToken, postId, "Right place", null, ct).ConfigureAwait(false);
            JsonElement read = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/threads/" + threadId, reader.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal(1, read.GetProperty("commentCount").GetInt32(), "The linked thread shows the feed post's comment count");
            JsonElement post = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/feed/" + postId, reader.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal(threadId, post.GetProperty("discussion").GetProperty("threadId").GetString(), "The feed post links its discussion");
            await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/feed/" + postId + "/undiscuss", admin.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Get, "/api/v1/threads/" + threadId, reader.AccessToken, null, HttpStatusCode.NotFound, "NotFound", ct).ConfigureAwait(false);
            await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/feed/" + postId, reader.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
        }

        private static async Task TrendingAsync(HttpFixture f, CancellationToken ct)
        {
            TestMember admin = await ContentTestKit.AdminAsync(f, ct).ConfigureAwait(false);
            TestMember author = await ContentTestKit.MemberAsync(f, ct, "Trending Author").ConfigureAwait(false);
            List<TestMember> fans = new List<TestMember>();
            for (int i = 0; i < 3; i++) fans.Add(await ContentTestKit.MemberAsync(f, ct, "Fan " + i).ConfigureAwait(false));
            string forumId = await ForumTestKit.DefaultForumIdAsync(f, author.AccessToken, ct).ConfigureAwait(false);
            string quiet = await ForumTestKit.ThreadIdAsync(f, author.AccessToken, forumId, "Quiet thread", ct).ConfigureAwait(false);
            string busy = await ForumTestKit.ThreadIdAsync(f, author.AccessToken, forumId, "Busy thread", ct).ConfigureAwait(false);
            string old = await ForumTestKit.ThreadIdAsync(f, author.AccessToken, forumId, "Old thread", ct).ConfigureAwait(false);
            foreach (TestMember fan in fans)
            {
                await ForumTestKit.CommentIdAsync(f, fan.AccessToken, busy, "Great thread", ct).ConfigureAwait(false);
                await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/threads/" + busy + "/reactions/" + f.Property.Reactions[0].Key, fan.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                await ForumTestKit.CommentIdAsync(f, fan.AccessToken, old, "Old comment", ct).ConfigureAwait(false);
            }
            await f.Raw.ExecuteQueryAsync("UPDATE forumthreads SET publishedutc = " + NotificationTestKit.Ts(DateTime.UtcNow.AddDays(-60)) + ", lastactivityutc = " + NotificationTestKit.Ts(DateTime.UtcNow.AddDays(-60)) + ", trendingscore = 99 WHERE id = '" + old + "';", false, ct).ConfigureAwait(false);
            List<Task<int>> passes = new List<Task<int>>();
            for (int i = 0; i < 3; i++) passes.Add(new LeasedPassWorker(f.Database, "forum_trending", () => TimeSpan.FromMinutes(10), TimeSpan.Zero, c => f.Services.Forums.Trending.RecomputeAsync(c), f.Services.Logger).RunOnceAsync(ct));
            int[] scored = await Task.WhenAll(passes).ConfigureAwait(false);
            Check.True(scored.Sum() >= 2, "Concurrent passes score the active threads (lease holders do the work)");
            ForumThread busyRow = (await f.Database.Threads.ReadAsync(busy, ct).ConfigureAwait(false))!;
            ForumThread quietRow = (await f.Database.Threads.ReadAsync(quiet, ct).ConfigureAwait(false))!;
            ForumThread oldRow = (await f.Database.Threads.ReadAsync(old, ct).ConfigureAwait(false))!;
            Check.True(busyRow.TrendingScore > quietRow.TrendingScore, "Activity raises the score");
            Check.Equal(0.0, oldRow.TrendingScore, "Threads outside the window are zeroed");
            double expected = f.Services.Forums.Trending.Score(busyRow.ReactionTotal, busyRow.CommentCount, busyRow.UniqueCommenterCount, (DateTime.UtcNow - busyRow.PublishedUtc!.Value).TotalHours);
            Check.True(Math.Abs(expected - busyRow.TrendingScore) / Math.Max(expected, 0.0001) < 0.05, "Score follows the formula (" + expected + " vs " + busyRow.TrendingScore + ")");
            JsonElement queue = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/admin/trending/enumerate", admin.AccessToken, new { }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            JsonElement top = queue.GetProperty("objects")[0];
            Check.Equal(busy, top.GetProperty("thread").GetProperty("id").GetString(), "The busy thread leads the trending queue");
            Check.True(top.TryGetProperty("breakdown", out JsonElement breakdown), "Breakdown");
            double live = breakdown.GetProperty("score").GetDouble();
            double ratio = breakdown.GetProperty("numerator").GetDouble() / breakdown.GetProperty("denominator").GetDouble();
            Check.True(Math.Abs(live - ratio) / Math.Max(ratio, 0.0001) < 0.01, "Breakdown score is the live numerator over denominator (" + live + " vs " + ratio + ")");
            Check.True(top.GetProperty("computedUtc").ValueKind == JsonValueKind.String, "Stored score carries its computed time");
            TestMember member = await ContentTestKit.MemberAsync(f, ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Post, "/api/v1/admin/trending/enumerate", member.AccessToken, new { }, HttpStatusCode.Forbidden, "Forbidden", ct).ConfigureAwait(false);
        }

        private static async Task SearchAsync(HttpFixture f, CancellationToken ct)
        {
            TestMember admin = await ContentTestKit.AdminAsync(f, ct).ConfigureAwait(false);
            TestMember author = await ContentTestKit.MemberAsync(f, ct, "Search Author").ConfigureAwait(false);
            string forumId = await ForumTestKit.DefaultForumIdAsync(f, author.AccessToken, ct).ConfigureAwait(false);
            string threadId = await ForumTestKit.ThreadIdAsync(f, author.AccessToken, forumId, "Watercolor troubleshooting", ct, "My watercolors keep bleeding on thin paper").ConfigureAwait(false);
            string other = await ForumTestKit.ThreadIdAsync(f, author.AccessToken, forumId, "Pottery glazes", ct, "Kiln temperature details").ConfigureAwait(false);
            string commentId = await ForumTestKit.CommentIdAsync(f, author.AccessToken, other, "Try a wet on dry watercolor layer", ct).ConfigureAwait(false);
            string postId = await ContentTestKit.PostIdAsync(f, admin.AccessToken, "Seminar about the watercolor next week", ct).ConfigureAwait(false);
            await ForumTestKit.DrainSearchAsync(f, ct).ConfigureAwait(false);
            JsonElement result = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/search/enumerate", author.AccessToken, new { search = "watercolors" }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            List<JsonElement> items = result.GetProperty("objects").EnumerateArray().ToList();
            Check.True(items.Any(i => Api.Str(i, "type") == "Thread" && Api.Str(i, "id") == threadId), "Thread found by a lemmatized term");
            Check.True(items.Any(i => Api.Str(i, "type") == "Comment" && Api.Str(i, "id") == commentId), "Comment found");
            Check.True(items.Any(i => Api.Str(i, "type") == "FeedPost" && Api.Str(i, "id") == postId), "Feed post found");
            Check.True(items.All(i => i.GetProperty("snippet").ValueKind != JsonValueKind.Null), "Results carry snippets");
            string searchSchema = VerbexSearchIndex.SchemaFor(f.Services.Settings.Database, f.Services.Settings.Search);
            System.Data.DataTable verbexTables = await f.Services.Database.Deployment.ExecuteQueryAsync(
                "SELECT table_name FROM information_schema.tables WHERE table_schema = '" + searchSchema + "' AND table_name IN ('tenants', 'users', 'documents');", false, ct).ConfigureAwait(false);
            Check.Equal(3, verbexTables.Rows.Count, "The search index tables live in the search schema, apart from the engine tables");
            JsonElement threadsOnly = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/search/enumerate", author.AccessToken, new { search = "watercolor", filters = new { types = "Thread" } }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.True(threadsOnly.GetProperty("objects").EnumerateArray().All(i => Api.Str(i, "type") == "Thread"), "types filter");
            JsonElement forumSearch = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/forums/" + forumId + "/threads/enumerate", author.AccessToken, new { search = "watercolor", ordering = "Relevance" }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            List<string> forumIds = ForumTestKit.Ids(forumSearch);
            Check.True(forumIds.Contains(threadId), "Forum thread search");
            await Api.ExpectAsync(f, HttpMethod.Delete, "/api/v1/threads/" + threadId, author.AccessToken, null, HttpStatusCode.NoContent, ct).ConfigureAwait(false);
            await ForumTestKit.DrainSearchAsync(f, ct).ConfigureAwait(false);
            JsonElement afterDelete = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/search/enumerate", author.AccessToken, new { search = "watercolors" }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.True(!afterDelete.GetProperty("objects").EnumerateArray().Any(i => Api.Str(i, "id") == threadId), "Deleted threads drop out");
            JsonElement rebuild = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/admin/search/rebuild", admin.AccessToken, null, HttpStatusCode.Accepted, ct).ConfigureAwait(false);
            Check.True(rebuild.GetProperty("queued").GetInt32() >= 3, "Rebuild queues every object");
            await ForumTestKit.DrainSearchAsync(f, ct).ConfigureAwait(false);
            JsonElement rebuilt = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/search/enumerate", author.AccessToken, new { search = "pottery" }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.True(rebuilt.GetProperty("objects").EnumerateArray().Any(i => Api.Str(i, "id") == other), "Searchable after the rebuild");
            await Api.ExpectErrorAsync(f, HttpMethod.Post, "/api/v1/admin/search/rebuild", author.AccessToken, null, HttpStatusCode.Forbidden, "Forbidden", ct).ConfigureAwait(false);
        }

        private static async Task PublicAsync(HttpFixture f, CancellationToken ct)
        {
            TestMember admin = await ContentTestKit.AdminAsync(f, ct).ConfigureAwait(false);
            TestMember member = await ContentTestKit.MemberAsync(f, ct).ConfigureAwait(false);
            JsonElement forum = await ForumTestKit.CreateForumAsync(f, admin.AccessToken, "Open House", ct, new { audience = "Public" }).ConfigureAwait(false);
            string publicForum = Api.Str(forum, "id");
            JsonElement sample = await ForumTestKit.ThreadAsync(f, admin.AccessToken, publicForum, new { type = "Discussion", title = "Welcome visitors", body = "Public sample", audience = "Public" }, ct).ConfigureAwait(false);
            string sampleId = Api.Str(sample, "id");
            string defaultForum = await ForumTestKit.DefaultForumIdAsync(f, member.AccessToken, ct).ConfigureAwait(false);
            string privateId = await ForumTestKit.ThreadIdAsync(f, member.AccessToken, defaultForum, "Members only thread", ct).ConfigureAwait(false);
            ApiResult list = await Api.SendAsync(f, HttpMethod.Post, "/api/v1/public/threads/enumerate", null, new { }, ct).ConfigureAwait(false);
            Check.Equal(HttpStatusCode.OK, list.Status, "Anonymous list (" + list.Text + ")");
            List<string> ids = ForumTestKit.Ids(list.Body);
            Check.True(ids.Contains(sampleId) && !ids.Contains(privateId), "Only public threads in public forums");
            Check.True(list.Headers.TryGetValue("Cache-Control", out string? cache) && cache.Contains("max-age=60", StringComparison.Ordinal), "Cache-Control");
            ApiResult one = await Api.SendAsync(f, HttpMethod.Get, "/api/v1/public/threads/" + sampleId, null, null, ct).ConfigureAwait(false);
            Check.Equal(HttpStatusCode.OK, one.Status, "Anonymous read");
            Check.True(!one.Body.TryGetProperty("viewer", out JsonElement _), "No viewer on public threads");
            await Api.ExpectErrorAsync(f, HttpMethod.Get, "/api/v1/public/threads/" + privateId, null, null, HttpStatusCode.NotFound, "NotFound", ct).ConfigureAwait(false);
            f.Property.Features.PublicSamples = false;
            ApiResult off = await Api.SendAsync(f, HttpMethod.Post, "/api/v1/public/threads/enumerate", null, new { }, ct).ConfigureAwait(false);
            Check.Equal(HttpStatusCode.NotFound, off.Status, "Public samples off");
        }

        private static async Task AccessAsync(HttpFixture f, CancellationToken ct)
        {
            TestMember member = await ContentTestKit.MemberAsync(f, ct).ConfigureAwait(false);
            string forumId = await ForumTestKit.DefaultForumIdAsync(f, member.AccessToken, ct).ConfigureAwait(false);
            string threadId = await ForumTestKit.ThreadIdAsync(f, member.AccessToken, forumId, "Gated thread", ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Post, "/api/v1/forums/enumerate", null, new { }, HttpStatusCode.Unauthorized, "Unauthorized", ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Get, "/api/v1/threads/" + threadId, null, null, HttpStatusCode.Unauthorized, "Unauthorized", ct).ConfigureAwait(false);
            TestMember lapsed = await Api.RegisterAsync(f, ct).ConfigureAwait(false);
            ApiResult gated = await Api.SendAsync(f, HttpMethod.Get, "/api/v1/threads/" + threadId, lapsed.AccessToken, null, ct).ConfigureAwait(false);
            Check.Equal(HttpStatusCode.PaymentRequired, gated.Status, "Members without membership are sent to membership (402), as for the feed");
            await Api.ExpectErrorAsync(f, HttpMethod.Post, "/api/v1/admin/threads/enumerate", member.AccessToken, new { }, HttpStatusCode.Forbidden, "Forbidden", ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Post, "/api/v1/admin/moderation/forum-mutes", member.AccessToken, new { userId = member.Id, forumId, reason = "Self mute" }, HttpStatusCode.Forbidden, "Forbidden", ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Get, "/api/v1/admin/metrics/forums", member.AccessToken, null, HttpStatusCode.Forbidden, "Forbidden", ct).ConfigureAwait(false);
            Check.Equal(RouteGroupEnum.Moderator, AuthorizationService.Classify("/api/v1/admin/threads/enumerate"), "admin/threads is a moderator route");
            Check.Equal(RouteGroupEnum.Moderator, AuthorizationService.Classify("/api/v1/admin/trending/enumerate"), "admin/trending is a moderator route");
            Check.Equal(RouteGroupEnum.Admin, AuthorizationService.Classify("/api/v1/admin/forums"), "admin/forums is an admin route");
            Check.Equal(RouteGroupEnum.Anonymous, AuthorizationService.Classify("/api/v1/public/threads/enumerate"), "public threads are anonymous");
            JsonElement metrics = await MetricEnumerationTestKit.DailyAsync(f, "/api/v1/admin/metrics/forums?days=7", HttpFixture.AdminToken, ct).ConfigureAwait(false);
            Check.True(metrics.ValueKind == JsonValueKind.Object, "Metrics");
            await Api.ExpectErrorAsync(f, HttpMethod.Get, "/api/v1/admin/metrics/forums?days=3", HttpFixture.AdminToken, null, HttpStatusCode.BadRequest, "ValidationFailed", ct).ConfigureAwait(false);
            f.Property.Features.Forums = false;
            ApiResult off = await Api.SendAsync(f, HttpMethod.Post, "/api/v1/forums/enumerate", member.AccessToken, new { }, ct).ConfigureAwait(false);
            Check.Equal(HttpStatusCode.NotFound, off.Status, "The property turns forums off");
        }

        private static async Task AccountAsync(HttpFixture f, CancellationToken ct)
        {
            TestMember admin = await ContentTestKit.AdminAsync(f, ct).ConfigureAwait(false);
            TestMember author = await ContentTestKit.MemberAsync(f, ct, "Leaving Member").ConfigureAwait(false);
            TestMember other = await ContentTestKit.MemberAsync(f, ct, "Staying Member").ConfigureAwait(false);
            string forumId = await ForumTestKit.DefaultForumIdAsync(f, author.AccessToken, ct).ConfigureAwait(false);
            string threadId = await ForumTestKit.ThreadIdAsync(f, author.AccessToken, forumId, "My thread before leaving", ct).ConfigureAwait(false);
            JsonElement poll = await ForumTestKit.PollThreadAsync(f, other.AccessToken, forumId, ct).ConfigureAwait(false);
            string pollId = Api.Str(poll.GetProperty("poll"), "id");
            await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/polls/" + pollId + "/votes", author.AccessToken, new { optionIds = new[] { Api.Str(poll.GetProperty("poll").GetProperty("options")[0], "id") } }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/forums/" + forumId + "/follow", author.AccessToken, new { level = "NewThreads" }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            JsonElement otherForum = await ForumTestKit.CreateForumAsync(f, admin.AccessToken, "Mute Room", ct).ConfigureAwait(false);
            await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/admin/moderation/forum-mutes", admin.AccessToken, new { userId = author.Id, forumId = Api.Str(otherForum, "id"), reason = "Testing the export" }, HttpStatusCode.Created, ct).ConfigureAwait(false);
            string exportId = Api.Str(await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/me/export", author.AccessToken, null, HttpStatusCode.Accepted, ct).ConfigureAwait(false), "exportId");
            AccountExportWorker worker = new AccountExportWorker(f.Services.AccountExports, f.Services.Logger);
            await using (worker.ConfigureAwait(false))
            {
                await worker.RunOnceAsync(ct).ConfigureAwait(false);
            }
            JsonElement status = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/me/export/" + exportId, author.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            JsonElement file = await Api.ExpectAsync(f, HttpMethod.Get, Api.Str(status, "downloadUrl"), null, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.True(file.GetProperty("threads").EnumerateArray().Any(t => Api.Str(t, "id") == threadId), "Export threads");
            Check.Equal(1, file.GetProperty("pollVotes").GetArrayLength(), "Export poll votes");
            Check.True(file.GetProperty("follows").GetArrayLength() >= 2, "Export follows (forum and own thread)");
            Check.Equal(1, file.GetProperty("forumMutes").GetArrayLength(), "Export forum mutes");
            Check.True(file.GetProperty("forumMutes")[0].GetProperty("mutedByUserId").ValueKind == JsonValueKind.Null, "The moderator is not exported");
            await Api.ExpectAsync(f, HttpMethod.Delete, "/api/v1/me", author.AccessToken, new { confirm = "DELETE" }, HttpStatusCode.Accepted, ct).ConfigureAwait(false);
            ForumThread thread = (await f.Database.Threads.ReadAsync(threadId, ct).ConfigureAwait(false))!;
            Check.True(thread.DeletedUtc != null, "Threads are soft-deleted");
            Check.Equal(0L, await ForumTestKit.CountAsync(f, "SELECT COUNT(*) AS cnt FROM pollvotes WHERE userid = '" + author.Id + "';", ct).ConfigureAwait(false), "Votes deleted");
            Poll stored = (await f.Database.Polls.ReadAsync(pollId, ct).ConfigureAwait(false))!;
            Check.Equal(0, stored.TotalVoters, "Poll counts recomputed");
            Check.Equal(0L, await ForumTestKit.CountAsync(f, "SELECT COUNT(*) AS cnt FROM forumfollows WHERE userid = '" + author.Id + "';", ct).ConfigureAwait(false), "Forum follows deleted");
            Check.Equal(0L, await ForumTestKit.CountAsync(f, "SELECT COUNT(*) AS cnt FROM threadfollows WHERE userid = '" + author.Id + "';", ct).ConfigureAwait(false), "Thread follows deleted");
            Check.Equal(0L, await ForumTestKit.CountAsync(f, "SELECT COUNT(*) AS cnt FROM forummutes WHERE userid = '" + author.Id + "';", ct).ConfigureAwait(false), "Mutes deleted");
        }

        #endregion
    }
}
