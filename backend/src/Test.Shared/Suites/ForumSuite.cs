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
    using Touchstone.Core;
    using TrellisKit.Core.Database;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Models;

    /// <summary>
    /// Phase 3 forums (docs/PHASE3_CONTRACT.md sections 3 to 10): forums and their admin, threads (lifecycle, lists,
    /// pinned prefix, Hot ordering, revisions), thread conversations, polls (exact counts under concurrency, visibility,
    /// voters), questions (concurrent accepts), feedback requests, and follows with their notifications.
    /// </summary>
    public static class ForumSuite
    {
        #region Public-Members

        /// <summary>
        /// Suite identifier.
        /// </summary>
        public const string Id = "Forums";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the suite.
        /// </summary>
        /// <returns>Suite.</returns>
        public static TestSuiteDescriptor Build()
        {
            return new TestSuiteDescriptor(Id, "Forums (" + TestEnvironment.Provider + ")", new[]
            {
                Case("Forums.SeedAndRead", "The default forum is seeded once; members list and read it by id and slug; property features report forums and search", SeedAsync),
                Case("Forums.Admin", "Admins create, edit, reorder, archive, unarchive, and delete forums; slugs are unique; forums with live threads cannot be deleted, forums whose threads were all deleted can; members cannot use admin routes", AdminAsync),
                Case("Forums.FollowAndRead", "Forum follow and unfollow are idempotent; marking a forum read clears its unread count", FollowForumAsync),
                Case("Threads.Lifecycle", "Create, read, edit with revisions, list in the forum and across forums, and delete a thread", ThreadLifecycleAsync),
                Case("Regression.Threads.RejectedAttemptsAreFree", "A rejected thread (validation error) does not spend the hourly thread allowance", RejectedThreadsAreFreeAsync),
                Case("Threads.PinnedAndHot", "The first page of a forum leads with pinned threads; Hot orders by trending score; pin requires a moderator", PinnedAndHotAsync),
                Case("Threads.Conversation", "Comments and reactions on a thread update its counts, and the comment tree carries the thread fields", ConversationAsync),
                Case("Polls.ConcurrentVotes", "Concurrent votes from many members leave exact option counts and voter totals; vote change and removal keep them exact", PollConcurrencyAsync),
                Case("Polls.EditBeforeFirstVote", "Before the first vote the author replaces options and choice settings through PUT /threads/{id}; after it only closesUtc changes and other poll fields are 409 PollOptionsLocked", PollEditAsync),
                Case("Filters.BooleanForms", "Boolean enumeration filters accept JSON booleans and the strings true and false with the same results; other values are 400 ValidationFailed", BooleanFiltersAsync),
                Case("Polls.Visibility", "Results stay hidden until voting or closing as configured; voters are listed only when visible; closing stops votes", PollVisibilityAsync),
                Case("Regression.Polls.VotersFollowResults", "Visible voters of an AfterVote poll stay hidden from members who cannot see the results yet (author and voters see them)", PollVotersFollowResultsAsync),
                Case("Questions.ConcurrentAccept", "Concurrent accepts on a question leave exactly one accepted answer that matches the thread", QuestionAsync),
                Case("Feedback.Workflow", "Feedback requests need a video; moderators move the status; the author can close; Reviewed notifies the author", FeedbackAsync),
                Case("Follows.Notifications", "Thread followers get ThreadReply, forum followers get NewThreadInForum, and me/follows lists both", FollowNotificationsAsync),
                Case("Follows.EmailDigest", "Members who turn on Following or Forums email get one branded digest per interval with their unread notifications of that category, with one-click unsubscribe; nothing for members who did not opt in, unverified addresses, or empty windows", EmailDigestAsync)
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

        private static async Task SeedAsync(HttpFixture f, CancellationToken ct)
        {
            TestMember member = await ContentTestKit.MemberAsync(f, ct).ConfigureAwait(false);
            JsonElement page = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/forums/enumerate", member.AccessToken, new { }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            List<JsonElement> forums = page.GetProperty("objects").EnumerateArray().ToList();
            Check.Equal(1, forums.Count, "One seeded forum");
            Check.Equal(ForumTestKit.DefaultSlug, Api.Str(forums[0], "slug"), "Seeded slug");
            ContentTestKit.CheckMetadata(forums[0], "forum");
            await f.Services.Forums.Catalog.EnsureSeededAsync(ct).ConfigureAwait(false);
            Check.Equal(1L, await ForumTestKit.CountAsync(f, "SELECT COUNT(*) AS cnt FROM forums;", ct).ConfigureAwait(false), "Seeding is idempotent");
            string id = Api.Str(forums[0], "id");
            JsonElement byId = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/forums/" + id, member.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal(id, Api.Str(byId, "id"), "Read by id");
            Check.True(byId.GetProperty("viewer").GetProperty("canCreateThread").GetBoolean(), "Members may create threads in the default forum");
            await Api.ExpectErrorAsync(f, HttpMethod.Get, "/api/v1/forums/no-such-forum", member.AccessToken, null, HttpStatusCode.NotFound, "NotFound", ct).ConfigureAwait(false);
            JsonElement property = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/property", null, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.True(property.GetProperty("features").GetProperty("forums").GetBoolean(), "features.forums");
            Check.True(property.GetProperty("features").GetProperty("search").GetBoolean(), "features.search");
        }

        private static async Task AdminAsync(HttpFixture f, CancellationToken ct)
        {
            TestMember admin = await ContentTestKit.AdminAsync(f, ct).ConfigureAwait(false);
            TestMember member = await ContentTestKit.MemberAsync(f, ct).ConfigureAwait(false);
            JsonElement created = await ForumTestKit.CreateForumAsync(f, admin.AccessToken, "Technique Lab", ct, new { slug = "technique-lab", section = "Training" }).ConfigureAwait(false);
            string forumId = Api.Str(created, "id");
            Check.Equal("technique-lab", Api.Str(created, "slug"), "Slug");
            ApiResult duplicate = await Api.SendAsync(f, HttpMethod.Post, "/api/v1/admin/forums", admin.AccessToken, new { name = "Other", slug = "technique-lab" }, ct).ConfigureAwait(false);
            Check.Equal(HttpStatusCode.Conflict, duplicate.Status, "Duplicate slug");
            Check.Equal("SlugInUse", duplicate.Error, "SlugInUse");
            await Api.ExpectErrorAsync(f, HttpMethod.Post, "/api/v1/admin/forums", member.AccessToken, new { name = "Nope" }, HttpStatusCode.Forbidden, "Forbidden", ct).ConfigureAwait(false);
            JsonElement updated = await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/admin/forums/" + forumId, admin.AccessToken, new { description = "Drills and details" }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal("Drills and details", Api.Str(updated, "description"), "Edited description");
            Check.Equal("Technique Lab", Api.Str(updated, "name"), "Omitted fields stay");
            JsonElement detail = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/admin/forums/" + forumId, admin.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.True(detail.TryGetProperty("moderators", out JsonElement moderators) && moderators.TryGetProperty("enumerate", out JsonElement _), "Detail has a moderators preview");
            string defaultId = await ForumTestKit.DefaultForumIdAsync(f, admin.AccessToken, ct).ConfigureAwait(false);
            JsonElement order = await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/admin/forums/order", admin.AccessToken, new { forumIds = new[] { forumId, defaultId } }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal(2, order.GetProperty("updated").GetInt32(), "Reordered");
            JsonElement list = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/forums/enumerate", member.AccessToken, new { }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal(forumId, ForumTestKit.Ids(list)[0], "Member list follows the admin order");
            await Api.ExpectErrorAsync(f, HttpMethod.Put, "/api/v1/admin/forums/order", admin.AccessToken, new { forumIds = new[] { forumId } }, HttpStatusCode.BadRequest, "ValidationFailed", ct).ConfigureAwait(false);
            await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/admin/forums/" + forumId + "/archive", admin.AccessToken, new { reason = "Seasonal" }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            JsonElement afterArchive = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/forums/enumerate", member.AccessToken, new { }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.True(!ForumTestKit.Ids(afterArchive).Contains(forumId), "Archived forums leave the default list");
            ApiResult archivedPost = await Api.SendAsync(f, HttpMethod.Post, "/api/v1/forums/" + forumId + "/threads", member.AccessToken, new { type = "Discussion", title = "Hello there", body = "Body" }, ct).ConfigureAwait(false);
            Check.True(archivedPost.Status == HttpStatusCode.Conflict || archivedPost.Status == HttpStatusCode.Forbidden, "No threads in an archived forum (" + (int)archivedPost.Status + ")");
            await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/admin/forums/" + forumId + "/unarchive", admin.AccessToken, new { }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            await ForumTestKit.ThreadIdAsync(f, member.AccessToken, forumId, "First thread here", ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Delete, "/api/v1/admin/forums/" + forumId, admin.AccessToken, null, HttpStatusCode.Conflict, "ForumNotEmpty", ct).ConfigureAwait(false);
            JsonElement used = await ForumTestKit.CreateForumAsync(f, admin.AccessToken, "Used Room", ct).ConfigureAwait(false);
            string usedThreadId = await ForumTestKit.ThreadIdAsync(f, member.AccessToken, Api.Str(used, "id"), "Soon deleted thread", ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Delete, "/api/v1/admin/forums/" + Api.Str(used, "id"), admin.AccessToken, null, HttpStatusCode.Conflict, "ForumNotEmpty", ct).ConfigureAwait(false);
            await Api.ExpectAsync(f, HttpMethod.Delete, "/api/v1/threads/" + usedThreadId, member.AccessToken, null, HttpStatusCode.NoContent, ct).ConfigureAwait(false);
            await Api.ExpectAsync(f, HttpMethod.Delete, "/api/v1/admin/forums/" + Api.Str(used, "id"), admin.AccessToken, null, HttpStatusCode.NoContent, ct).ConfigureAwait(false);
            JsonElement empty = await ForumTestKit.CreateForumAsync(f, admin.AccessToken, "Empty Room", ct).ConfigureAwait(false);
            await Api.ExpectAsync(f, HttpMethod.Delete, "/api/v1/admin/forums/" + Api.Str(empty, "id"), admin.AccessToken, null, HttpStatusCode.NoContent, ct).ConfigureAwait(false);
            JsonElement adminList = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/admin/forums/enumerate", admin.AccessToken, new { }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal(2, ForumTestKit.Ids(adminList).Count, "Admin list after delete");
            Check.True(await ModerationTestKit.AuditCountAsync(f, "forum.create", forumId, ct).ConfigureAwait(false) >= 1, "forum.create audited");
        }

        private static async Task FollowForumAsync(HttpFixture f, CancellationToken ct)
        {
            TestMember member = await ContentTestKit.MemberAsync(f, ct).ConfigureAwait(false);
            TestMember other = await ContentTestKit.MemberAsync(f, ct, "Other Member").ConfigureAwait(false);
            string forumId = await ForumTestKit.DefaultForumIdAsync(f, member.AccessToken, ct).ConfigureAwait(false);
            JsonElement followed = await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/forums/" + forumId + "/follow", member.AccessToken, new { level = "NewThreads" }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.True(followed.GetProperty("isFollowed").GetBoolean(), "Followed");
            await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/forums/" + forumId + "/follow", member.AccessToken, new { level = "NewThreads" }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal(1L, await ForumTestKit.CountAsync(f, "SELECT COUNT(*) AS cnt FROM forumfollows WHERE userid = '" + member.Id + "';", ct).ConfigureAwait(false), "Follow is idempotent");
            await ForumTestKit.ThreadIdAsync(f, other.AccessToken, forumId, "A new thread to read", ct).ConfigureAwait(false);
            JsonElement before = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/forums/" + forumId, member.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.True(before.GetProperty("unreadThreadCount").GetInt32() >= 1, "Unread thread counted");
            await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/forums/" + forumId + "/read", member.AccessToken, null, HttpStatusCode.NoContent, ct).ConfigureAwait(false);
            JsonElement after = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/forums/" + forumId, member.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal(0, after.GetProperty("unreadThreadCount").GetInt32(), "Marked read");
            Check.Equal(1L, await ForumTestKit.CountAsync(f, "SELECT COUNT(*) AS cnt FROM forumreads WHERE userid = '" + member.Id + "';", ct).ConfigureAwait(false), "Forum read marker stored");
            await f.Raw.ExecuteQueryAsync("UPDATE forumreads SET lastreadutc = now() - interval '400 days' WHERE userid = '" + member.Id + "';", false, ct).ConfigureAwait(false);
            int pruned = await f.Services.Database.Threads.PruneReadsAsync(DateTime.UtcNow.AddDays(-90), 5000, ct).ConfigureAwait(false);
            Check.True(pruned >= 1, "The prune counts forum read markers (" + pruned + ")");
            Check.Equal(0L, await ForumTestKit.CountAsync(f, "SELECT COUNT(*) AS cnt FROM forumreads WHERE userid = '" + member.Id + "';", ct).ConfigureAwait(false), "Old forum read markers are pruned");
            JsonElement unfollowed = await Api.ExpectAsync(f, HttpMethod.Delete, "/api/v1/forums/" + forumId + "/follow", member.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.True(!unfollowed.GetProperty("isFollowed").GetBoolean(), "Unfollowed");
            await Api.ExpectAsync(f, HttpMethod.Delete, "/api/v1/forums/" + forumId + "/follow", member.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
        }

        private static async Task ThreadLifecycleAsync(HttpFixture f, CancellationToken ct)
        {
            TestMember author = await ContentTestKit.MemberAsync(f, ct, "Thread Author").ConfigureAwait(false);
            TestMember reader = await ContentTestKit.MemberAsync(f, ct, "Thread Reader").ConfigureAwait(false);
            string forumId = await ForumTestKit.DefaultForumIdAsync(f, author.AccessToken, ct).ConfigureAwait(false);
            JsonElement created = await ForumTestKit.ThreadAsync(f, author.AccessToken, forumId, new { type = "Discussion", title = "Best warmups for the meetup", body = "What do you all do before a session?", labels = new[] { "warmup" } }, ct).ConfigureAwait(false);
            string threadId = Api.Str(created, "id");
            Check.Equal("Published", Api.Str(created, "state"), "Published without media");
            Check.Equal("Thread", created.GetProperty("commentTarget").GetProperty("type").GetString(), "commentTarget is the thread");
            Check.True(created.GetProperty("isFollowed").GetBoolean(), "The author follows the thread");
            ContentTestKit.CheckMetadata(created, "thread");
            JsonElement read = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/threads/" + threadId, reader.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal("What do you all do before a session?", Api.Str(read, "body"), "Body");
            Check.True(!read.GetProperty("viewer").GetProperty("canEdit").GetBoolean(), "Readers cannot edit");
            await Api.ExpectErrorAsync(f, HttpMethod.Put, "/api/v1/threads/" + threadId, reader.AccessToken, new { title = "Hijacked title" }, HttpStatusCode.Forbidden, "Forbidden", ct).ConfigureAwait(false);
            f.Settings.Content.EditGraceSeconds = 0;
            JsonElement edited = await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/threads/" + threadId, author.AccessToken, new { body = "Edited: what warmups?" }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal("Edited: what warmups?", Api.Str(edited, "body"), "Edited body");
            Check.Equal("Best warmups for the meetup", Api.Str(edited, "title"), "Title kept");
            JsonElement revisions = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/threads/" + threadId + "/revisions/enumerate", reader.AccessToken, new { }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.True(revisions.GetProperty("objects").GetArrayLength() >= 1, "Revision stored after the grace period");
            JsonElement forumList = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/forums/" + forumId + "/threads/enumerate", reader.AccessToken, new { }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.True(ForumTestKit.Ids(forumList).Contains(threadId), "Listed in the forum");
            JsonElement labeled = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/threads/enumerate", reader.AccessToken, new { labels = new[] { "warmup" } }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal(1, ForumTestKit.Ids(labeled).Count, "Label filter");
            JsonElement mine = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/threads/enumerate", author.AccessToken, new { filters = new { authorUserId = author.Id } }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.True(ForumTestKit.Ids(mine).Contains(threadId), "Own threads");
            await Api.ExpectErrorAsync(f, HttpMethod.Delete, "/api/v1/threads/" + threadId, reader.AccessToken, null, HttpStatusCode.Forbidden, "Forbidden", ct).ConfigureAwait(false);
            await Api.ExpectAsync(f, HttpMethod.Delete, "/api/v1/threads/" + threadId, author.AccessToken, null, HttpStatusCode.NoContent, ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Get, "/api/v1/threads/" + threadId, reader.AccessToken, null, HttpStatusCode.NotFound, "NotFound", ct).ConfigureAwait(false);
            JsonElement afterDelete = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/forums/" + forumId + "/threads/enumerate", reader.AccessToken, new { }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.True(!ForumTestKit.Ids(afterDelete).Contains(threadId), "Deleted threads leave the list");
            ApiResult invalid = await Api.SendAsync(f, HttpMethod.Post, "/api/v1/forums/" + forumId + "/threads", author.AccessToken, new { type = "Discussion", title = "" }, ct).ConfigureAwait(false);
            Check.Equal(HttpStatusCode.BadRequest, invalid.Status, "Title required");
        }

        private static async Task PinnedAndHotAsync(HttpFixture f, CancellationToken ct)
        {
            TestMember admin = await ContentTestKit.AdminAsync(f, ct).ConfigureAwait(false);
            TestMember member = await ContentTestKit.MemberAsync(f, ct).ConfigureAwait(false);
            string forumId = await ForumTestKit.DefaultForumIdAsync(f, member.AccessToken, ct).ConfigureAwait(false);
            List<string> ids = new List<string>();
            for (int i = 0; i < 4; i++) ids.Add(await ForumTestKit.ThreadIdAsync(f, member.AccessToken, forumId, "Thread number " + i, ct).ConfigureAwait(false));
            await Api.ExpectErrorAsync(f, HttpMethod.Post, "/api/v1/threads/" + ids[0] + "/pin", member.AccessToken, null, HttpStatusCode.Forbidden, "Forbidden", ct).ConfigureAwait(false);
            JsonElement pinned = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/threads/" + ids[0] + "/pin", admin.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.True(pinned.GetProperty("isPinned").GetBoolean(), "Pinned");
            JsonElement page = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/forums/" + forumId + "/threads/enumerate", member.AccessToken, new { maxResults = 2 }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            List<string> first = ForumTestKit.Ids(page);
            Check.Equal(ids[0], first[0], "Pinned thread leads the first page");
            Check.Equal(2, first.Count, "Pinned threads count toward maxResults");
            List<string> seen = new List<string>(first);
            string? token = page.GetProperty("continuationToken").ValueKind == JsonValueKind.String ? page.GetProperty("continuationToken").GetString() : null;
            while (token != null)
            {
                JsonElement next = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/forums/" + forumId + "/threads/enumerate", member.AccessToken, new { maxResults = 2, continuationToken = token }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                seen.AddRange(ForumTestKit.Ids(next));
                token = next.GetProperty("continuationToken").ValueKind == JsonValueKind.String ? next.GetProperty("continuationToken").GetString() : null;
            }
            Check.Equal(4, seen.Distinct().Count(), "Every thread exactly once across pages");
            Check.Equal(4, seen.Count, "No duplicates across pages");
            await f.Raw.ExecuteQueryAsync("UPDATE forumthreads SET trendingscore = 50 WHERE id = '" + ids[2] + "';", false, ct).ConfigureAwait(false);
            await f.Raw.ExecuteQueryAsync("UPDATE forumthreads SET trendingscore = 10 WHERE id = '" + ids[1] + "';", false, ct).ConfigureAwait(false);
            JsonElement hot = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/threads/enumerate", member.AccessToken, new { ordering = "Hot" }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            List<string> hotIds = ForumTestKit.Ids(hot);
            Check.Equal(ids[2], hotIds[0], "Hot orders by trending score");
            List<string> hotSeen = new List<string>();
            string? hotCursor = null;
            do
            {
                JsonElement hotPage = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/forums/" + forumId + "/threads/enumerate", member.AccessToken,
                    new { ordering = "Hot", maxResults = 1, continuationToken = hotCursor }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                Check.True(hotPage.GetProperty("objects").GetArrayLength() <= 1, "Hot pinned page respects cap");
                hotSeen.AddRange(ForumTestKit.Ids(hotPage));
                hotCursor = hotPage.GetProperty("continuationToken").GetString();
            } while (hotCursor != null);
            Check.Equal(ids[0], hotSeen[0], "Pinned item leads Hot ordering");
            Check.Equal(ids[2], hotSeen[1], "Hot continuation crosses from pins to ranked threads");
            Check.Equal(4, hotSeen.Distinct().Count(), "Hot enumeration reaches every thread once");

            Check.Equal(ids[1], hotIds[1], "Second by score");
            await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/threads/" + ids[0] + "/unpin", admin.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
        }

        private static async Task ConversationAsync(HttpFixture f, CancellationToken ct)
        {
            TestMember author = await ContentTestKit.MemberAsync(f, ct, "Conversation Author").ConfigureAwait(false);
            TestMember other = await ContentTestKit.MemberAsync(f, ct, "Conversation Other").ConfigureAwait(false);
            string forumId = await ForumTestKit.DefaultForumIdAsync(f, author.AccessToken, ct).ConfigureAwait(false);
            string threadId = await ForumTestKit.ThreadIdAsync(f, author.AccessToken, forumId, "Talk about news", ct).ConfigureAwait(false);
            string top = await ForumTestKit.CommentIdAsync(f, other.AccessToken, threadId, "Single legs all day", ct).ConfigureAwait(false);
            await ForumTestKit.CommentIdAsync(f, author.AccessToken, threadId, "Agreed", ct, top).ConfigureAwait(false);
            await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/threads/" + threadId + "/reactions/" + f.Property.Reactions[0].Key, other.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/comments/" + top + "/reactions/" + f.Property.Reactions[0].Key, author.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            JsonElement thread = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/threads/" + threadId, other.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal(2, thread.GetProperty("commentCount").GetInt32(), "commentCount");
            Check.Equal(1, thread.GetProperty("reactionTotal").GetInt32(), "reactionTotal");
            Check.Equal(1, thread.GetProperty("uniqueCommenterCount").GetInt32(), "uniqueCommenterCount excludes the thread author");
            JsonElement tree = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/threads/" + threadId + "/comments/enumerate", other.AccessToken, new { }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            JsonElement first = tree.GetProperty("objects")[0];
            Check.Equal(top, Api.Str(first, "id"), "Top-level comment");
            Check.True(!first.GetProperty("isAcceptedAnswer").GetBoolean(), "isAcceptedAnswer present");
            Check.Equal(1, first.GetProperty("children").GetArrayLength(), "Reply nested");
            JsonElement who = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/threads/" + threadId + "/reactions/enumerate", author.AccessToken, new { }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal(1, who.GetProperty("objects").GetArrayLength(), "Who reacted to the thread");
            await Api.ExpectAsync(f, HttpMethod.Delete, "/api/v1/threads/" + threadId + "/reactions/" + f.Property.Reactions[0].Key, other.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            ForumThread stored = (await f.Database.Threads.ReadAsync(threadId, ct).ConfigureAwait(false))!;
            Check.Equal(0, stored.ReactionTotal, "Reaction removed");
            Check.Equal(2, stored.CommentCount, "Stored comment count");
        }

        private static async Task PollConcurrencyAsync(HttpFixture f, CancellationToken ct)
        {
            TestMember author = await ContentTestKit.MemberAsync(f, ct, "Poll Author").ConfigureAwait(false);
            string forumId = await ForumTestKit.DefaultForumIdAsync(f, author.AccessToken, ct).ConfigureAwait(false);
            JsonElement thread = await ForumTestKit.PollThreadAsync(f, author.AccessToken, forumId, ct, multiple: true).ConfigureAwait(false);
            JsonElement poll = thread.GetProperty("poll");
            string pollId = Api.Str(poll, "id");
            List<string> options = poll.GetProperty("options").EnumerateArray().Select(o => Api.Str(o, "id")).ToList();
            Check.Equal(3, options.Count, "Three options");
            List<TestMember> voters = new List<TestMember>();
            for (int i = 0; i < 12; i++) voters.Add(await ContentTestKit.MemberAsync(f, ct, "Voter " + i).ConfigureAwait(false));
            List<Task<ApiResult>> votes = new List<Task<ApiResult>>();
            for (int i = 0; i < voters.Count; i++)
            {
                string[] choice = i % 3 == 0 ? new[] { options[0], options[1] } : new[] { options[i % 3] };
                votes.Add(Api.SendAsync(f, HttpMethod.Put, "/api/v1/polls/" + pollId + "/votes", voters[i].AccessToken, new { optionIds = choice }, ct));
                votes.Add(Api.SendAsync(f, HttpMethod.Put, "/api/v1/polls/" + pollId + "/votes", voters[i].AccessToken, new { optionIds = choice }, ct));
            }
            ApiResult[] results = await Task.WhenAll(votes).ConfigureAwait(false);
            Check.True(results.All(r => r.Status == HttpStatusCode.OK), "Every concurrent vote succeeded: " + String.Join(", ", results.Where(r => r.Status != HttpStatusCode.OK).Select(r => (int)r.Status + " " + r.Text)));
            await VerifyPollAsync(f, pollId, 12, ct).ConfigureAwait(false);
            List<Task<ApiResult>> changes = new List<Task<ApiResult>>();
            for (int i = 0; i < 6; i++) changes.Add(Api.SendAsync(f, HttpMethod.Put, "/api/v1/polls/" + pollId + "/votes", voters[i].AccessToken, new { optionIds = new[] { options[2] } }, ct));
            for (int i = 6; i < 9; i++) changes.Add(Api.SendAsync(f, HttpMethod.Delete, "/api/v1/polls/" + pollId + "/votes", voters[i].AccessToken, null, ct));
            ApiResult[] changed = await Task.WhenAll(changes).ConfigureAwait(false);
            Check.True(changed.All(r => r.Status == HttpStatusCode.OK), "Changes and removals succeeded");
            await VerifyPollAsync(f, pollId, 9, ct).ConfigureAwait(false);
            ApiResult tooMany = await Api.SendAsync(f, HttpMethod.Put, "/api/v1/polls/" + pollId + "/votes", voters[0].AccessToken, new { optionIds = options }, ct).ConfigureAwait(false);
            Check.Equal(HttpStatusCode.BadRequest, tooMany.Status, "More than maxChoices");
            ApiResult empty = await Api.SendAsync(f, HttpMethod.Put, "/api/v1/polls/" + pollId + "/votes", voters[0].AccessToken, new { optionIds = new string[0] }, ct).ConfigureAwait(false);
            Check.Equal(HttpStatusCode.BadRequest, empty.Status, "Empty vote");
            JsonElement mine = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/threads/" + Api.Str(thread, "id"), voters[0].AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal(options[2], mine.GetProperty("poll").GetProperty("myVote")[0].GetString(), "myVote");
        }

        private static async Task VerifyPollAsync(HttpFixture f, string pollId, int expectedVoters, CancellationToken ct)
        {
            Poll poll = (await f.Database.Polls.ReadAsync(pollId, ct).ConfigureAwait(false))!;
            long voters = await ForumTestKit.CountAsync(f, "SELECT COUNT(DISTINCT userid) AS cnt FROM pollvotes WHERE pollid = '" + pollId + "';", ct).ConfigureAwait(false);
            Check.Equal((long)expectedVoters, voters, "Distinct voters in rows");
            Check.Equal(expectedVoters, poll.TotalVoters, "totalVoters");
            foreach (PollOption option in poll.Options)
            {
                long rows = await ForumTestKit.CountAsync(f, "SELECT COUNT(*) AS cnt FROM pollvotes WHERE optionid = '" + option.Id + "';", ct).ConfigureAwait(false);
                Check.Equal((int)rows, option.VoteCount, "voteCount of " + option.Label);
            }
        }

        private static async Task PollEditAsync(HttpFixture f, CancellationToken ct)
        {
            TestMember author = await ContentTestKit.MemberAsync(f, ct, "Poll Editor").ConfigureAwait(false);
            TestMember voter = await ContentTestKit.MemberAsync(f, ct, "Poll Voter").ConfigureAwait(false);
            string forumId = await ForumTestKit.DefaultForumIdAsync(f, author.AccessToken, ct).ConfigureAwait(false);
            JsonElement thread = await ForumTestKit.PollThreadAsync(f, author.AccessToken, forumId, ct).ConfigureAwait(false);
            string path = "/api/v1/threads/" + Api.Str(thread, "id");
            object replaced = new
            {
                poll = new
                {
                    options = new[] { new { label = "Morning" }, new { label = "Evening" } },
                    multipleChoice = true,
                    maxChoices = 2,
                    resultsVisibility = "AfterClose",
                    allowVoteChange = false
                }
            };
            JsonElement edited = await Api.ExpectAsync(f, HttpMethod.Put, path, author.AccessToken, replaced, HttpStatusCode.OK, ct).ConfigureAwait(false);
            JsonElement poll = edited.GetProperty("poll");
            Check.Equal(2, poll.GetProperty("options").GetArrayLength(), "Options replaced before the first vote");
            Check.Equal("Morning", Api.Str(poll.GetProperty("options")[0], "label"), "New first option");
            Check.True(poll.GetProperty("multipleChoice").GetBoolean(), "multipleChoice changed");
            Check.Equal("AfterClose", Api.Str(poll, "resultsVisibility"), "resultsVisibility changed");
            Check.True(!poll.GetProperty("allowVoteChange").GetBoolean(), "allowVoteChange changed");
            await Api.ExpectErrorAsync(f, HttpMethod.Put, path, voter.AccessToken, replaced, HttpStatusCode.Forbidden, "Forbidden", ct).ConfigureAwait(false);
            string option = Api.Str(poll.GetProperty("options")[1], "id");
            await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/polls/" + Api.Str(poll, "id") + "/votes", voter.AccessToken, new { optionIds = new[] { option } }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Put, path, author.AccessToken, new { poll = new { options = new[] { new { label = "A" }, new { label = "B" } } } }, HttpStatusCode.Conflict, "PollOptionsLocked", ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Put, path, author.AccessToken, new { poll = new { multipleChoice = false } }, HttpStatusCode.Conflict, "PollOptionsLocked", ct).ConfigureAwait(false);
            DateTime closes = DateTime.UtcNow.AddDays(3);
            JsonElement later = await Api.ExpectAsync(f, HttpMethod.Put, path, author.AccessToken, new { poll = new { closesUtc = closes } }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.True(later.GetProperty("poll").GetProperty("closesUtc").ValueKind == JsonValueKind.String, "closesUtc may change after the first vote");
            Check.Equal(2, later.GetProperty("poll").GetProperty("options").GetArrayLength(), "Options kept");
            Poll stored = (await f.Database.Polls.ReadAsync(Api.Str(poll, "id"), ct).ConfigureAwait(false))!;
            Check.Equal(1, stored.Options.First(o => o.Id == option).VoteCount, "The vote survives the closing time edit");
        }

        private static async Task BooleanFiltersAsync(HttpFixture f, CancellationToken ct)
        {
            TestMember admin = await ContentTestKit.AdminAsync(f, ct).ConfigureAwait(false);
            TestMember member = await ContentTestKit.MemberAsync(f, ct, "Filter Member").ConfigureAwait(false);
            string forumId = await ForumTestKit.DefaultForumIdAsync(f, member.AccessToken, ct).ConfigureAwait(false);
            string pinned = await ForumTestKit.ThreadIdAsync(f, member.AccessToken, forumId, "Pinned one", ct).ConfigureAwait(false);
            string plain = await ForumTestKit.ThreadIdAsync(f, member.AccessToken, forumId, "Plain one", ct).ConfigureAwait(false);
            await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/threads/" + pinned + "/pin", admin.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            JsonElement asBool = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/admin/threads/enumerate", admin.AccessToken, new { filters = new { pinned = true } }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            JsonElement asText = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/admin/threads/enumerate", admin.AccessToken, new { filters = new { pinned = "true" } }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.True(ForumTestKit.Ids(asBool).Contains(pinned) && !ForumTestKit.Ids(asBool).Contains(plain), "pinned: true (JSON boolean)");
            Check.Equal(String.Join(",", ForumTestKit.Ids(asBool)), String.Join(",", ForumTestKit.Ids(asText)), "pinned: \"true\" (string) gives the same page");
            JsonElement notPinned = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/admin/threads/enumerate", admin.AccessToken, new { filters = new { pinned = false } }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.True(ForumTestKit.Ids(notPinned).Contains(plain) && !ForumTestKit.Ids(notPinned).Contains(pinned), "pinned: false (JSON boolean)");
            string archived = Api.Str(await ForumTestKit.CreateForumAsync(f, admin.AccessToken, "Archived filters", ct).ConfigureAwait(false), "id");
            await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/admin/forums/" + archived + "/archive", admin.AccessToken, new { }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            JsonElement withBool = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/admin/forums/enumerate", admin.AccessToken, new { filters = new { includeArchived = true } }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            JsonElement withText = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/admin/forums/enumerate", admin.AccessToken, new { filters = new { includeArchived = "true" } }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            JsonElement without = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/admin/forums/enumerate", admin.AccessToken, new { filters = new { includeArchived = false } }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.True(ForumTestKit.Ids(withBool).Contains(archived) && ForumTestKit.Ids(withText).Contains(archived), "includeArchived as a boolean and as a string");
            Check.True(!ForumTestKit.Ids(without).Contains(archived), "includeArchived: false");
            JsonElement answered = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/threads/enumerate", member.AccessToken, new { filters = new { answered = "false" } }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.True(answered.GetProperty("objects").ValueKind == JsonValueKind.Array, "answered as a string");
            await Api.ExpectErrorAsync(f, HttpMethod.Post, "/api/v1/admin/threads/enumerate", admin.AccessToken, new { filters = new { pinned = "maybe" } }, HttpStatusCode.BadRequest, "ValidationFailed", ct).ConfigureAwait(false);
        }

        private static async Task RejectedThreadsAreFreeAsync(HttpFixture f, CancellationToken ct)
        {
            int perHour = f.Settings.Forums.ThreadsPerHour;
            int newPerHour = f.Settings.Forums.NewAccountThreadsPerHour;
            try
            {
                f.Settings.Forums.ThreadsPerHour = 1;
                f.Settings.Forums.NewAccountThreadsPerHour = 1;
                TestMember member = await ContentTestKit.MemberAsync(f, ct, "Careful Poster").ConfigureAwait(false);
                string forumId = await ForumTestKit.DefaultForumIdAsync(f, member.AccessToken, ct).ConfigureAwait(false);
                string path = "/api/v1/forums/" + forumId + "/threads";
                await Api.ExpectErrorAsync(f, HttpMethod.Post, path, member.AccessToken, new { type = "Discussion", title = "x", body = "Too short a title." }, HttpStatusCode.BadRequest, "ValidationFailed", ct).ConfigureAwait(false);
                await Api.ExpectErrorAsync(f, HttpMethod.Post, path, member.AccessToken, new { type = "Poll", title = "A poll without options", body = "Pick one." }, HttpStatusCode.BadRequest, "PollInvalid", ct).ConfigureAwait(false);
                await Api.ExpectAsync(f, HttpMethod.Post, path, member.AccessToken, new { type = "Discussion", title = "The first valid thread", body = "Counted once." }, HttpStatusCode.Created, ct).ConfigureAwait(false);
                await Api.ExpectErrorAsync(f, HttpMethod.Post, path, member.AccessToken, new { type = "Discussion", title = "The second valid thread", body = "Over the limit." }, (HttpStatusCode)429, "RateLimited", ct).ConfigureAwait(false);
            }
            finally
            {
                f.Settings.Forums.ThreadsPerHour = perHour;
                f.Settings.Forums.NewAccountThreadsPerHour = newPerHour;
            }
        }

        private static async Task PollVotersFollowResultsAsync(HttpFixture f, CancellationToken ct)
        {
            TestMember author = await ContentTestKit.MemberAsync(f, ct, "Voters Author").ConfigureAwait(false);
            TestMember voter = await ContentTestKit.MemberAsync(f, ct, "Voters Voter").ConfigureAwait(false);
            TestMember lurker = await ContentTestKit.MemberAsync(f, ct, "Voters Lurker").ConfigureAwait(false);
            string forumId = await ForumTestKit.DefaultForumIdAsync(f, author.AccessToken, ct).ConfigureAwait(false);
            JsonElement thread = await ForumTestKit.PollThreadAsync(f, author.AccessToken, forumId, ct, resultsVisibility: "AfterVote", voterVisibility: "Visible").ConfigureAwait(false);
            string pollId = Api.Str(thread.GetProperty("poll"), "id");
            string option = Api.Str(thread.GetProperty("poll").GetProperty("options")[0], "id");
            await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/polls/" + pollId + "/votes", voter.AccessToken, new { optionIds = new[] { option } }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Post, "/api/v1/polls/" + pollId + "/voters/enumerate", lurker.AccessToken, new { }, HttpStatusCode.Forbidden, "Forbidden", ct).ConfigureAwait(false);
            JsonElement asVoter = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/polls/" + pollId + "/voters/enumerate", voter.AccessToken, new { }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal(1, asVoter.GetProperty("objects").GetArrayLength(), "A voter sees the voters");
            JsonElement asAuthor = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/polls/" + pollId + "/voters/enumerate", author.AccessToken, new { }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal(1, asAuthor.GetProperty("objects").GetArrayLength(), "The author sees the voters");
        }

        private static async Task PollVisibilityAsync(HttpFixture f, CancellationToken ct)
        {
            TestMember author = await ContentTestKit.MemberAsync(f, ct, "Poll Author").ConfigureAwait(false);
            TestMember voter = await ContentTestKit.MemberAsync(f, ct, "Poll Voter").ConfigureAwait(false);
            TestMember lurker = await ContentTestKit.MemberAsync(f, ct, "Poll Lurker").ConfigureAwait(false);
            string forumId = await ForumTestKit.DefaultForumIdAsync(f, author.AccessToken, ct).ConfigureAwait(false);
            JsonElement thread = await ForumTestKit.PollThreadAsync(f, author.AccessToken, forumId, ct, allowChange: false, resultsVisibility: "AfterVote", voterVisibility: "Anonymous").ConfigureAwait(false);
            string threadId = Api.Str(thread, "id");
            string pollId = Api.Str(thread.GetProperty("poll"), "id");
            string option = Api.Str(thread.GetProperty("poll").GetProperty("options")[0], "id");
            JsonElement before = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/threads/" + threadId, lurker.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.True(!before.GetProperty("poll").GetProperty("resultsVisible").GetBoolean(), "Results hidden before voting");
            JsonElement voted = await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/polls/" + pollId + "/votes", voter.AccessToken, new { optionIds = new[] { option } }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.True(voted.GetProperty("resultsVisible").GetBoolean(), "Results visible after voting");
            Check.Equal(1, voted.GetProperty("options")[0].GetProperty("voteCount").GetInt32(), "Count after vote");
            await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/polls/" + pollId + "/votes", voter.AccessToken, new { optionIds = new[] { option } }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            string otherOption = Api.Str(thread.GetProperty("poll").GetProperty("options")[1], "id");
            await Api.ExpectErrorAsync(f, HttpMethod.Put, "/api/v1/polls/" + pollId + "/votes", voter.AccessToken, new { optionIds = new[] { otherOption } }, HttpStatusCode.Conflict, "PollVoteLocked", ct).ConfigureAwait(false);
            ApiResult voters = await Api.SendAsync(f, HttpMethod.Post, "/api/v1/polls/" + pollId + "/voters/enumerate", lurker.AccessToken, new { }, ct).ConfigureAwait(false);
            Check.True(voters.Status == HttpStatusCode.Forbidden || voters.Status == HttpStatusCode.NotFound, "Hidden voters are not listed (" + (int)voters.Status + ")");
            await Api.ExpectErrorAsync(f, HttpMethod.Post, "/api/v1/polls/" + pollId + "/close", lurker.AccessToken, null, HttpStatusCode.Forbidden, "Forbidden", ct).ConfigureAwait(false);
            JsonElement closed = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/polls/" + pollId + "/close", author.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.True(closed.GetProperty("isClosed").GetBoolean(), "Closed");
            await Api.ExpectErrorAsync(f, HttpMethod.Put, "/api/v1/polls/" + pollId + "/votes", lurker.AccessToken, new { optionIds = new[] { option } }, HttpStatusCode.Conflict, "PollClosed", ct).ConfigureAwait(false);
            JsonElement after = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/threads/" + threadId, lurker.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.True(after.GetProperty("poll").GetProperty("resultsVisible").GetBoolean(), "Results visible once closed");
            JsonElement open = await ForumTestKit.PollThreadAsync(f, author.AccessToken, forumId, ct).ConfigureAwait(false);
            string openPoll = Api.Str(open.GetProperty("poll"), "id");
            await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/polls/" + openPoll + "/votes", voter.AccessToken, new { optionIds = new[] { Api.Str(open.GetProperty("poll").GetProperty("options")[1], "id") } }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            JsonElement visible = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/polls/" + openPoll + "/voters/enumerate", lurker.AccessToken, new { }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal(1, visible.GetProperty("objects").GetArrayLength(), "Visible voters listed");
            Check.Equal(voter.Id, visible.GetProperty("objects")[0].GetProperty("user").GetProperty("id").GetString(), "Voter identity");
            await f.Raw.ExecuteQueryAsync("UPDATE polls SET closesutc = " + NotificationTestKit.Ts(DateTime.UtcNow.AddMinutes(-1)) + " WHERE id = '" + openPoll + "';", false, ct).ConfigureAwait(false);
            int closedCount = await f.Services.Forums.Polls.CloseDueAsync(100, ct).ConfigureAwait(false);
            Check.Equal(1, closedCount, "Due poll closed by the worker pass");
            await ForumTestKit.FanoutAsync(f, ct).ConfigureAwait(false);
            List<Notification> pollClosed = await ForumTestKit.NotificationsAsync(f, voter.Id, NotificationTypeEnum.PollClosed, ct).ConfigureAwait(false);
            Check.True(pollClosed.Count >= 1, "Voters are notified when a poll closes");
        }

        private static async Task QuestionAsync(HttpFixture f, CancellationToken ct)
        {
            TestMember author = await ContentTestKit.MemberAsync(f, ct, "Question Author").ConfigureAwait(false);
            TestMember other = await ContentTestKit.MemberAsync(f, ct, "Answerer").ConfigureAwait(false);
            string forumId = await ForumTestKit.DefaultForumIdAsync(f, author.AccessToken, ct).ConfigureAwait(false);
            JsonElement thread = await ForumTestKit.ThreadAsync(f, author.AccessToken, forumId, new { type = "Question", title = "How do I escape side control?", body = "I keep getting stuck." }, ct).ConfigureAwait(false);
            string threadId = Api.Str(thread, "id");
            List<string> answers = new List<string>();
            for (int i = 0; i < 5; i++) answers.Add(await ForumTestKit.CommentIdAsync(f, other.AccessToken, threadId, "Answer " + i, ct).ConfigureAwait(false));
            string reply = await ForumTestKit.CommentIdAsync(f, author.AccessToken, threadId, "Thanks", ct, answers[0]).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Post, "/api/v1/threads/" + threadId + "/accept-answer", other.AccessToken, new { commentId = answers[0] }, HttpStatusCode.Forbidden, "Forbidden", ct).ConfigureAwait(false);
            List<Task<ApiResult>> accepts = answers.Select(a => Api.SendAsync(f, HttpMethod.Post, "/api/v1/threads/" + threadId + "/accept-answer", author.AccessToken, new { commentId = a }, ct)).ToList();
            ApiResult[] results = await Task.WhenAll(accepts).ConfigureAwait(false);
            Check.True(results.All(r => r.Status == HttpStatusCode.OK), "Every accept succeeded");
            ForumThread stored = (await f.Database.Threads.ReadAsync(threadId, ct).ConfigureAwait(false))!;
            Check.True(stored.HasAcceptedAnswer && stored.AcceptedCommentId != null, "An answer is accepted");
            long flagged = await ForumTestKit.CountAsync(f, "SELECT COUNT(*) AS cnt FROM comments WHERE targetid = '" + threadId + "' AND isacceptedanswer = true;", ct).ConfigureAwait(false);
            Check.Equal(1L, flagged, "Exactly one comment carries isAcceptedAnswer");
            long matches = await ForumTestKit.CountAsync(f, "SELECT COUNT(*) AS cnt FROM comments WHERE id = '" + stored.AcceptedCommentId + "' AND isacceptedanswer = true;", ct).ConfigureAwait(false);
            Check.Equal(1L, matches, "The flagged comment is the thread's accepted answer");
            JsonElement tree = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/threads/" + threadId + "/comments/enumerate", other.AccessToken, new { }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal(stored.AcceptedCommentId, tree.GetProperty("acceptedAnswerId").GetString(), "acceptedAnswerId on the tree");
            JsonElement replyAccepted = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/threads/" + threadId + "/accept-answer", author.AccessToken, new { commentId = reply }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal(reply, Api.Str(replyAccepted.GetProperty("acceptedAnswer"), "id"), "A reply at any depth may be accepted, replacing the previous choice");
            Check.Equal(1L, await ForumTestKit.CountAsync(f, "SELECT COUNT(*) AS cnt FROM comments WHERE targetid = '" + threadId + "' AND isacceptedanswer = true;", ct).ConfigureAwait(false), "Still exactly one accepted");
            await Api.ExpectAsync(f, HttpMethod.Delete, "/api/v1/threads/" + threadId + "/accept-answer", author.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal(0L, await ForumTestKit.CountAsync(f, "SELECT COUNT(*) AS cnt FROM comments WHERE targetid = '" + threadId + "' AND isacceptedanswer = true;", ct).ConfigureAwait(false), "Cleared");
            JsonElement unanswered = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/threads/enumerate", other.AccessToken, new { filters = new { answered = false } }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.True(ForumTestKit.Ids(unanswered).Contains(threadId), "answered false lists questions without an accepted answer");
            JsonElement noReplies = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/threads/enumerate", other.AccessToken, new { filters = new { unanswered = true } }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.True(!ForumTestKit.Ids(noReplies).Contains(threadId), "unanswered excludes questions with comments");
            string discussion = await ForumTestKit.ThreadIdAsync(f, author.AccessToken, forumId, "Not a question", ct).ConfigureAwait(false);
            string comment = await ForumTestKit.CommentIdAsync(f, other.AccessToken, discussion, "Hi", ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Post, "/api/v1/threads/" + discussion + "/accept-answer", author.AccessToken, new { commentId = comment }, HttpStatusCode.BadRequest, "InvalidAcceptedAnswer", ct).ConfigureAwait(false);
        }

        private static async Task FeedbackAsync(HttpFixture f, CancellationToken ct)
        {
            TestMember admin = await ContentTestKit.AdminAsync(f, ct).ConfigureAwait(false);
            TestMember author = await ContentTestKit.MemberAsync(f, ct, "Feedback Author").ConfigureAwait(false);
            string forumId = await ForumTestKit.DefaultForumIdAsync(f, author.AccessToken, ct).ConfigureAwait(false);
            ApiResult noVideo = await Api.SendAsync(f, HttpMethod.Post, "/api/v1/forums/" + forumId + "/threads", author.AccessToken, new { type = "Feedback", title = "Review please", body = "No video", feedback = new { focus = "Anything" } }, ct).ConfigureAwait(false);
            Check.Equal(HttpStatusCode.BadRequest, noVideo.Status, "A video is required");
            Check.Equal("FeedbackVideoRequired", noVideo.Error, "FeedbackVideoRequired");
            string threadId = await ForumTestKit.FeedbackThreadAsync(f, author, forumId, ct).ConfigureAwait(false);
            JsonElement thread = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/threads/" + threadId, author.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal("Open", thread.GetProperty("feedback").GetProperty("status").GetString(), "Opens as Open");
            await Api.ExpectErrorAsync(f, HttpMethod.Put, "/api/v1/threads/" + threadId + "/feedback", author.AccessToken, new { status = "Reviewed" }, HttpStatusCode.Forbidden, "Forbidden", ct).ConfigureAwait(false);
            JsonElement openItem = (await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/threads/enumerate", author.AccessToken, new { filters = new { type = "Feedback" } }, HttpStatusCode.OK, ct).ConfigureAwait(false))
                .GetProperty("objects").EnumerateArray().First(t => Api.Str(t, "id") == threadId);
            Check.True(openItem.GetProperty("feedback").GetProperty("reviewer").ValueKind == JsonValueKind.Null, "List item reviewer is null while Open");
            await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/threads/" + threadId + "/feedback", admin.AccessToken, new { status = "InReview" }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            JsonElement inReview = (await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/threads/enumerate", author.AccessToken, new { filters = new { type = "Feedback" } }, HttpStatusCode.OK, ct).ConfigureAwait(false))
                .GetProperty("objects").EnumerateArray().First(t => Api.Str(t, "id") == threadId);
            Check.Equal("InReview", inReview.GetProperty("feedback").GetProperty("status").GetString(), "List item status");
            Check.Equal(admin.Id, Api.Str(inReview.GetProperty("feedback").GetProperty("reviewer"), "id"), "List item reviewer is the moderator who started the review");
            string review = await ForumTestKit.CommentIdAsync(f, admin.AccessToken, threadId, "Your base is too narrow; widen your knees.", ct).ConfigureAwait(false);
            JsonElement reviewed = await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/threads/" + threadId + "/feedback", admin.AccessToken, new { status = "Reviewed", reviewCommentId = review }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal("Reviewed", reviewed.GetProperty("feedback").GetProperty("status").GetString(), "Reviewed");
            Check.Equal(review, reviewed.GetProperty("feedback").GetProperty("reviewCommentId").GetString(), "Review comment");
            List<Notification> notes = await ForumTestKit.NotificationsAsync(f, author.Id, NotificationTypeEnum.FeedbackReviewed, ct).ConfigureAwait(false);
            Check.Equal(1, notes.Count, "FeedbackReviewed notification");
            JsonElement tree = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/threads/" + threadId + "/comments/enumerate", author.AccessToken, new { }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.True(tree.GetProperty("objects").EnumerateArray().Any(c => Api.Str(c, "id") == review && c.GetProperty("isFeedbackReview").GetBoolean()), "The review comment carries isFeedbackReview");
            JsonElement closed = await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/threads/" + threadId + "/feedback", author.AccessToken, new { status = "Closed" }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal("Closed", closed.GetProperty("feedback").GetProperty("status").GetString(), "Author closed");
            JsonElement filtered = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/threads/enumerate", author.AccessToken, new { filters = new { type = "Feedback", feedbackStatus = "Closed" } }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.True(ForumTestKit.Ids(filtered).Contains(threadId), "feedbackStatus filter");
        }

        private static async Task EmailDigestAsync(HttpFixture f, CancellationToken ct)
        {
            NotificationTestKit.AllDay(f);
            TestMember author = await ContentTestKit.MemberAsync(f, ct, "Digest Author").ConfigureAwait(false);
            TestMember follower = await ContentTestKit.MemberAsync(f, ct, "Digest Follower").ConfigureAwait(false);
            TestMember quiet = await ContentTestKit.MemberAsync(f, ct, "Quiet Follower").ConfigureAwait(false);
            TestMember commenter = await ContentTestKit.MemberAsync(f, ct, "Digest Commenter").ConfigureAwait(false);
            string forumId = await ForumTestKit.DefaultForumIdAsync(f, author.AccessToken, ct).ConfigureAwait(false);
            string threadId = await ForumTestKit.ThreadIdAsync(f, author.AccessToken, forumId, "Digest worthy thread", ct).ConfigureAwait(false);
            foreach (TestMember m in new[] { follower, quiet })
                await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/threads/" + threadId + "/follow", m.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            JsonElement prefs = await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/me/notification-preferences", follower.AccessToken, new { categories = new[] { new { category = "Following", email = true } } }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.True(prefs.GetProperty("categories").EnumerateArray().Any(c => Api.Str(c, "category") == "Following" && c.GetProperty("email").GetBoolean()), "Following email opt-in stored");
            await f.Raw.ExecuteQueryAsync("UPDATE users SET emailverified = TRUE WHERE id IN ('" + follower.Id + "', '" + quiet.Id + "');", false, ct).ConfigureAwait(false);

            Check.Equal(0, await f.Services.Forums.Digests.RunAsync(DateTime.UtcNow, 500, ct).ConfigureAwait(false), "The first check starts the schedule");
            Check.Equal(1L, await ForumTestKit.CountAsync(f, "SELECT COUNT(*) AS cnt FROM forumemaildigests WHERE userid = '" + follower.Id + "' AND category = 'Following';", ct).ConfigureAwait(false), "Schedule row");
            Check.Equal(0L, await ForumTestKit.CountAsync(f, "SELECT COUNT(*) AS cnt FROM forumemaildigests WHERE userid = '" + quiet.Id + "';", ct).ConfigureAwait(false), "No schedule without the opt-in");

            await f.Raw.ExecuteQueryAsync("UPDATE forumemaildigests SET lastcheckedutc = now() - interval '8 days' WHERE userid = '" + follower.Id + "';", false, ct).ConfigureAwait(false);
            Check.Equal(0, await f.Services.Forums.Digests.RunAsync(DateTime.UtcNow, 500, ct).ConfigureAwait(false), "Nothing unread: no email");
            await ForumTestKit.CommentIdAsync(f, commenter.AccessToken, threadId, "A reply worth a digest", ct).ConfigureAwait(false);
            await ForumTestKit.FanoutAsync(f, ct).ConfigureAwait(false);
            Check.Equal(0, await f.Services.Forums.Digests.RunAsync(DateTime.UtcNow, 500, ct).ConfigureAwait(false), "Not due again inside the interval");
            await f.Raw.ExecuteQueryAsync("UPDATE forumemaildigests SET lastcheckedutc = now() - interval '8 days' WHERE userid = '" + follower.Id + "';", false, ct).ConfigureAwait(false);
            Check.Equal(1, await f.Services.Forums.Digests.RunAsync(DateTime.UtcNow, 500, ct).ConfigureAwait(false), "One digest");
            List<TrellisKit.Core.Integrations.Models.EmailMessage> digests = f.Fakes.Email.To(follower.Email, "forum_digest");
            Check.Equal(1, digests.Count, "Delivered through the administrative email queue");
            TrellisKit.Core.Integrations.Models.EmailMessage digest = digests[0];
            Check.True(digest.Subject.Contains("threads you follow", StringComparison.Ordinal), "Subject names the category (" + digest.Subject + ")");
            Check.True(digest.TextBody.Contains("Digest worthy thread", StringComparison.Ordinal) && digest.TextBody.Contains("/app/threads/" + threadId, StringComparison.Ordinal), "Lists the notification with its link");
            Check.True(digest.HtmlBody != null && digest.HtmlBody.Contains("<html", StringComparison.OrdinalIgnoreCase), "HTML part from the branded template");
            Check.True(digest.Headers.TryGetValue("List-Unsubscribe", out string? unsubscribe) && unsubscribe.Contains("/api/v1/email/unsubscribe?token=", StringComparison.Ordinal), "One-click unsubscribe header");
            Check.Equal(0, f.Fakes.Email.To(quiet.Email, "forum_digest").Count, "No digest without the opt-in");
            DataTable row = await f.Raw.ExecuteQueryAsync("SELECT category, status FROM emailmessages WHERE template = 'forum_digest' AND userid = '" + follower.Id + "';", false, ct).ConfigureAwait(false);
            Check.Equal(1, row.Rows.Count, "Recorded in the email log");
            Check.Equal("Optional", RowReader.GetString(row.Rows[0], "category"), "Optional email");
            Check.Equal(0, await f.Services.Forums.Digests.RunAsync(DateTime.UtcNow, 500, ct).ConfigureAwait(false), "One digest per interval");
        }

        private static async Task FollowNotificationsAsync(HttpFixture f, CancellationToken ct)
        {
            NotificationTestKit.AllDay(f);
            TestMember author = await ContentTestKit.MemberAsync(f, ct, "Follow Author").ConfigureAwait(false);
            TestMember follower = await ContentTestKit.MemberAsync(f, ct, "Thread Follower").ConfigureAwait(false);
            TestMember forumFan = await ContentTestKit.MemberAsync(f, ct, "Forum Follower").ConfigureAwait(false);
            TestMember commenter = await ContentTestKit.MemberAsync(f, ct, "Commenter").ConfigureAwait(false);
            string forumId = await ForumTestKit.DefaultForumIdAsync(f, author.AccessToken, ct).ConfigureAwait(false);
            await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/forums/" + forumId + "/follow", forumFan.AccessToken, new { level = "NewThreads" }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            string threadId = await ForumTestKit.ThreadIdAsync(f, author.AccessToken, forumId, "Follow me please", ct).ConfigureAwait(false);
            JsonElement follow = await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/threads/" + threadId + "/follow", follower.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.True(follow.GetProperty("isFollowed").GetBoolean(), "Following");
            await ForumTestKit.CommentIdAsync(f, commenter.AccessToken, threadId, "A reply for the followers", ct).ConfigureAwait(false);
            await ForumTestKit.FanoutAsync(f, ct).ConfigureAwait(false);
            List<Notification> replies = await ForumTestKit.NotificationsAsync(f, follower.Id, NotificationTypeEnum.ThreadReply, ct).ConfigureAwait(false);
            Check.True(replies.Count >= 1, "Thread follower got ThreadReply");
            Check.Equal(threadId, replies[0].ThreadId, "Notification carries the thread");
            List<Notification> commenterReplies = await ForumTestKit.NotificationsAsync(f, commenter.Id, NotificationTypeEnum.ThreadReply, ct).ConfigureAwait(false);
            Check.Equal(0, commenterReplies.Count, "No ThreadReply to the commenter");
            List<Notification> newThreads = await ForumTestKit.NotificationsAsync(f, forumFan.Id, NotificationTypeEnum.NewThreadInForum, ct).ConfigureAwait(false);
            Check.True(newThreads.Count >= 1, "Forum follower got NewThreadInForum");
            JsonElement follows = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/me/follows/enumerate", follower.AccessToken, new { }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.True(follows.GetProperty("objects").EnumerateArray().Any(o => Api.Str(o, "targetType") == "Thread"), "me/follows lists the thread");
            JsonElement forumFollows = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/me/follows/enumerate", forumFan.AccessToken, new { filters = new { targetType = "Forum" } }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal(1, forumFollows.GetProperty("objects").GetArrayLength(), "me/follows filters by targetType");
            await Api.ExpectAsync(f, HttpMethod.Delete, "/api/v1/threads/" + threadId + "/follow", follower.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            await ForumTestKit.CommentIdAsync(f, follower.AccessToken, threadId, "Commenting again", ct).ConfigureAwait(false);
            ThreadFollow? sticky = await f.Database.Threads.ReadFollowAsync(threadId, follower.Id, ct).ConfigureAwait(false);
            Check.True(sticky == null || sticky.IsOptedOut, "An unfollow sticks against the automatic follow on comment");
            JsonElement preferences = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/me/notification-preferences", follower.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.True(preferences.GetProperty("autoFollowOnComment").GetBoolean(), "autoFollowOnComment defaults to on");
            JsonElement changed = await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/me/notification-preferences", follower.AccessToken, new { autoFollowOnComment = false }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.True(!changed.GetProperty("autoFollowOnComment").GetBoolean(), "autoFollowOnComment stored");
        }

        #endregion
    }
}
