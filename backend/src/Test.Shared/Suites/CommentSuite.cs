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

    /// <summary>
    /// Phase 2 threaded comments (contract section 7): tree pages, children continuation through the moreChildren
    /// cursor, orderings, flattening at the maximum depth, placeholders (Deleted, Hidden, Removed, BlockedAuthor),
    /// permalinks, errors, rate limits, and counter integrity under concurrent writes.
    /// </summary>
    public static class CommentSuite
    {
        #region Public-Members

        /// <summary>
        /// Suite identifier.
        /// </summary>
        public const string Id = "Comments";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the suite.
        /// </summary>
        /// <returns>Suite.</returns>
        public static TestSuiteDescriptor Build()
        {
            return new TestSuiteDescriptor(Id, "Threaded comments (" + TestEnvironment.Provider + ")", new[]
            {
                Case("Comments.TreePage", "Tree pages nest replies to depth with childLimit, report moreChildren and commentCount, and carry labels and tags", TreeAsync),
                Case("Comments.TreeNodeCap", "A tree response holds at most CommentService.TreeNodeLimit comments across every level; parents whose replies were left out carry moreChildren, and the children route returns the rest", TreeNodeCapAsync),
                Case("Comments.Children", "The moreChildren cursor continues a comment's replies on the children route whatever depth and childLimit, also as cursor, and only with its ordering", ChildrenAsync),
                Case("Comments.Ordering", "Top orders by score then age; CreatedDescending and CreatedAscending order by time; unknown orderings are 400", OrderingAsync),
                Case("Comments.Flattening", "A reply at comments.maxDepth is stored as a sibling with replyToCommentId and notifies the replied-to author", FlatteningAsync),
                Case("Comments.Placeholders", "Deleted, Hidden, Removed, and BlockedAuthor placeholders appear only with visible descendants; authors and moderators see moderated content", PlaceholdersAsync),
                Case("Comments.Permalink", "GET /api/v1/comments/{id} returns the target, the ancestors root first, and the comment with children", PermalinkAsync),
                Case("Regression.Comments.BlockedPermalink", "The permalink of a comment by a member the caller blocked is 404; its replies and other callers are unaffected", async (f, ct) =>
                {
                    TestMember poster = await ContentTestKit.PosterAsync(f, ct).ConfigureAwait(false);
                    TestMember y = await ContentTestKit.MemberAsync(f, ct, "Blocked One").ConfigureAwait(false);
                    TestMember z = await ContentTestKit.MemberAsync(f, ct, "Blocker").ConfigureAwait(false);
                    string post = await ContentTestKit.PostIdAsync(f, poster.AccessToken, "Permalinks", ct).ConfigureAwait(false);
                    string blocked = await ContentTestKit.CommentIdAsync(f, y.AccessToken, post, "by a blocked member", null, ct).ConfigureAwait(false);
                    string reply = await ContentTestKit.CommentIdAsync(f, poster.AccessToken, post, "reply to blocked", blocked, ct).ConfigureAwait(false);
                    await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/users/" + y.Id + "/block", z.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    await Api.ExpectErrorAsync(f, HttpMethod.Get, "/api/v1/comments/" + blocked, z.AccessToken, null, HttpStatusCode.NotFound, "NotFound", ct).ConfigureAwait(false);
                    await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/comments/" + reply, z.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    JsonElement forPoster = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/comments/" + blocked, poster.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.Equal("by a blocked member", Api.Str(forPoster.GetProperty("comment"), "body"), "Other callers see the comment");
                }),
                Case("Comments.Errors", "CommentsDisabled, ParentUnavailable, InteractionBlocked, NotFound, validation, and edit and delete permissions", ErrorsAsync),
                Case("Comments.Notifications", "PostComment to the post author, CommentReply to the parent author, one notification per recipient, none for self", NotificationsAsync),
                Case("Comments.RateLimits", "New accounts get the stricter comment limits; 429 carries Retry-After", RateLimitsAsync),
                Case("Comments.IntegrityRace", "Concurrent replies at several levels, reactions, and deletes leave childCount, descendantCount, score, and commentCount equal to a recount", IntegrityAsync)
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

        private static async Task TreeAsync(HttpFixture f, CancellationToken ct)
        {
            TestMember poster = await ContentTestKit.PosterAsync(f, ct).ConfigureAwait(false);
            TestMember m = await ContentTestKit.MemberAsync(f, ct).ConfigureAwait(false);
            string post = await ContentTestKit.PostIdAsync(f, poster.AccessToken, "Discuss", ct).ConfigureAwait(false);
            string r1 = Api.Str(await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/feed/" + post + "/comments", m.AccessToken, new { body = "root one", labels = new[] { "question" } }, HttpStatusCode.Created, ct).ConfigureAwait(false), "id");
            string r2 = await ContentTestKit.CommentIdAsync(f, m.AccessToken, post, "root two", null, ct).ConfigureAwait(false);
            string c1 = await ContentTestKit.CommentIdAsync(f, m.AccessToken, post, "child one", r1, ct).ConfigureAwait(false);
            string c2 = await ContentTestKit.CommentIdAsync(f, m.AccessToken, post, "child two", r1, ct).ConfigureAwait(false);
            string c3 = await ContentTestKit.CommentIdAsync(f, m.AccessToken, post, "child three", r1, ct).ConfigureAwait(false);
            await ContentTestKit.CommentIdAsync(f, m.AccessToken, post, "grandchild one", c1, ct).ConfigureAwait(false);
            await ContentTestKit.CommentIdAsync(f, m.AccessToken, post, "grandchild two", c1, ct).ConfigureAwait(false);
            JsonElement page = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/feed/" + post + "/comments?depth=2&childLimit=2&ordering=CreatedAscending", m.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal(7L, page.GetProperty("commentCount").GetInt64(), "commentCount");
            Check.True(ContentTestKit.Ids(page).SequenceEqual(new[] { r1, r2 }), "Top level only");
            JsonElement root = page.GetProperty("objects")[0];
            Check.Equal("question", root.GetProperty("labels")[0].GetString(), "Labels on comments");
            List<string> children = root.GetProperty("children").EnumerateArray().Select(c => Api.Str(c, "id")).ToList();
            Check.True(children.SequenceEqual(new[] { c1, c2 }), "childLimit replies");
            Check.Equal(3, root.GetProperty("childCount").GetInt32(), "childCount");
            Check.Equal(5, root.GetProperty("descendantCount").GetInt32(), "descendantCount");
            JsonElement more = root.GetProperty("moreChildren");
            Check.Equal(1, more.GetProperty("remainingCount").GetInt32(), "remainingCount");
            Check.True(more.GetProperty("cursor").ValueKind == JsonValueKind.String && Api.Str(more, "cursor") == Api.Str(more, "continuationToken"), "cursor and continuationToken");
            Check.Equal("/api/v1/comments/" + r1 + "/children/enumerate", Api.Str(more, "enumerate"), "enumerate path");
            JsonElement child = root.GetProperty("children")[0];
            Check.Equal(0, child.GetProperty("children").GetArrayLength(), "Depth ran out");
            Check.Equal(2, child.GetProperty("moreChildren").GetProperty("remainingCount").GetInt32(), "Depth-limited remainingCount");
            Check.Equal(JsonValueKind.Null, child.GetProperty("moreChildren").GetProperty("cursor").ValueKind, "Null cursor when depth ran out");
            Check.Equal(JsonValueKind.Null, page.GetProperty("objects")[1].GetProperty("moreChildren").ValueKind, "No moreChildren without replies");
            foreach (JsonElement item in page.GetProperty("objects").EnumerateArray()) ContentTestKit.CheckMetadata(item, "comment");
            ContentTestKit.CheckMetadata(child, "child comment");
            Check.True(child.TryGetProperty("viewer", out JsonElement viewer) && viewer.GetProperty("canReply").GetBoolean() && viewer.GetProperty("canEdit").GetBoolean(), "Viewer of a comment");
            JsonElement byPost = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/feed/" + post + "/comments/enumerate", m.AccessToken, new { labels = new[] { "question" }, filters = new Dictionary<string, object> { { "depth", 1 } } }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.True(ContentTestKit.Ids(byPost).SequenceEqual(new[] { r1 }), "Label filter on top-level comments");
            Check.Equal(0, byPost.GetProperty("objects")[0].GetProperty("children").GetArrayLength(), "depth 1 returns no children");
            await Api.ExpectErrorAsync(f, HttpMethod.Get, "/api/v1/feed/" + post + "/comments?depth=9", m.AccessToken, null, HttpStatusCode.BadRequest, "ValidationFailed", ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Get, "/api/v1/feed/" + post + "/comments?childLimit=0", m.AccessToken, null, HttpStatusCode.BadRequest, "ValidationFailed", ct).ConfigureAwait(false);
            JsonElement paged = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/feed/" + post + "/comments?maxResults=1&ordering=CreatedAscending", m.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            JsonElement next = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/feed/" + post + "/comments?maxResults=1&childLimit=9&continuationToken=" + Uri.EscapeDataString(Api.Str(paged, "continuationToken")), m.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.True(ContentTestKit.Ids(next).SequenceEqual(new[] { r2 }), "Top-level continuation");
            Check.True(c3.Length > 0, "c3 created");
        }

        private static async Task TreeNodeCapAsync(HttpFixture f, CancellationToken ct)
        {
            TestMember poster = await ContentTestKit.PosterAsync(f, ct).ConfigureAwait(false);
            f.Settings.Comments.NewAccountPerMinute = 1000;
            string post = await ContentTestKit.PostIdAsync(f, poster.AccessToken, "Busy thread", ct).ConfigureAwait(false);
            string r1 = await ContentTestKit.CommentIdAsync(f, poster.AccessToken, post, "root one", null, ct).ConfigureAwait(false);
            string r2 = await ContentTestKit.CommentIdAsync(f, poster.AccessToken, post, "root two", null, ct).ConfigureAwait(false);
            List<string> c = new List<string>();
            for (int i = 0; i < 3; i++) c.Add(await ContentTestKit.CommentIdAsync(f, poster.AccessToken, post, "child " + i, r1, ct).ConfigureAwait(false));
            List<string> d = new List<string>();
            for (int i = 0; i < 2; i++) d.Add(await ContentTestKit.CommentIdAsync(f, poster.AccessToken, post, "other " + i, r2, ct).ConfigureAwait(false));
            for (int i = 0; i < 2; i++) await ContentTestKit.CommentIdAsync(f, poster.AccessToken, post, "grandchild " + i, c[0], ct).ConfigureAwait(false);
            Check.Equal(CommentService.MaxTreeNodes, f.Services.Comments.TreeNodeLimit, "Default budget");
            Check.Throws<ArgumentOutOfRangeException>(() => f.Services.Comments.TreeNodeLimit = 0, "Budget must be positive");
            Check.Throws<ArgumentOutOfRangeException>(() => f.Services.Comments.TreeNodeLimit = CommentService.MaxTreeNodes + 1, "Budget has a maximum");
            f.Services.Comments.TreeNodeLimit = 5;
            try
            {
                JsonElement page = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/feed/" + post + "/comments?depth=3&childLimit=5&ordering=CreatedAscending", poster.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                int nodes = 0;
                void Count(JsonElement node) { nodes++; foreach (JsonElement child in node.GetProperty("children").EnumerateArray()) Count(child); }
                foreach (JsonElement root in page.GetProperty("objects").EnumerateArray()) Count(root);
                Check.Equal(5, nodes, "Exactly the budget of comments across levels");
                JsonElement first = page.GetProperty("objects")[0];
                JsonElement second = page.GetProperty("objects")[1];
                Check.True(first.GetProperty("children").EnumerateArray().Select(x => Api.Str(x, "id")).SequenceEqual(c), "The first parent's replies fill the budget in order");
                Check.Equal(0, second.GetProperty("children").GetArrayLength(), "The second parent's replies are left out");
                JsonElement more = second.GetProperty("moreChildren");
                Check.Equal(2, more.GetProperty("remainingCount").GetInt32(), "moreChildren counts the left-out replies");
                Check.Equal("/api/v1/comments/" + r2 + "/children/enumerate", Api.Str(more, "enumerate"), "enumerate path for the rest");
                JsonElement grandParent = first.GetProperty("children")[0];
                Check.Equal(0, grandParent.GetProperty("children").GetArrayLength(), "No budget left for the next level");
                Check.Equal(2, grandParent.GetProperty("moreChildren").GetProperty("remainingCount").GetInt32(), "Grandchildren left out are counted");
                JsonElement rest = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/comments/" + r2 + "/children/enumerate", poster.AccessToken, new { ordering = "CreatedAscending" }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                Check.True(ContentTestKit.Ids(rest).SequenceEqual(d), "The children route returns the left-out replies");
                JsonElement permalink = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/comments/" + r1 + "?depth=3&childLimit=5", poster.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                Check.True(permalink.ValueKind == JsonValueKind.Object, "Permalink still answers under a small budget");
            }
            finally
            {
                f.Services.Comments.TreeNodeLimit = CommentService.MaxTreeNodes;
            }
        }

        private static async Task ChildrenAsync(HttpFixture f, CancellationToken ct)
        {
            TestMember poster = await ContentTestKit.PosterAsync(f, ct).ConfigureAwait(false);
            f.Settings.Comments.NewAccountPerMinute = 1000;
            string post = await ContentTestKit.PostIdAsync(f, poster.AccessToken, "Thread", ct).ConfigureAwait(false);
            string parent = await ContentTestKit.CommentIdAsync(f, poster.AccessToken, post, "parent", null, ct).ConfigureAwait(false);
            List<string> replies = new List<string>();
            for (int i = 0; i < 7; i++) replies.Add(await ContentTestKit.CommentIdAsync(f, poster.AccessToken, post, "reply " + i, parent, ct).ConfigureAwait(false));
            JsonElement page = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/feed/" + post + "/comments?childLimit=2&ordering=CreatedAscending", poster.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            JsonElement more = page.GetProperty("objects")[0].GetProperty("moreChildren");
            Check.Equal(5, more.GetProperty("remainingCount").GetInt32(), "Five more");
            List<string> seen = page.GetProperty("objects")[0].GetProperty("children").EnumerateArray().Select(c => Api.Str(c, "id")).ToList();
            string? cursor = Api.Str(more, "cursor");
            JsonElement byCursor = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/comments/" + parent + "/children?ordering=CreatedAscending&depth=1&childLimit=7&maxResults=2&cursor=" + Uri.EscapeDataString(cursor), poster.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            seen.AddRange(ContentTestKit.Ids(byCursor));
            string token = Api.Str(byCursor, "continuationToken");
            while (!String.IsNullOrEmpty(token))
            {
                JsonElement next = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/comments/" + parent + "/children/enumerate", poster.AccessToken,
                    new { ordering = "CreatedAscending", maxResults = 2, continuationToken = token, filters = new Dictionary<string, object> { { "depth", 3 }, { "childLimit", 1 } } }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                seen.AddRange(ContentTestKit.Ids(next));
                token = next.GetProperty("continuationToken").ValueKind == JsonValueKind.String ? next.GetProperty("continuationToken").GetString()! : String.Empty;
            }
            Check.True(seen.SequenceEqual(replies), "Every reply once, in order: " + String.Join(",", seen));
            JsonElement aliasBody = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/comments/" + parent + "/children/enumerate", poster.AccessToken, new { ordering = "CreatedAscending", cursor }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.True(ContentTestKit.Ids(aliasBody).SequenceEqual(replies.Skip(2)), "cursor in the POST body");
            Check.Equal(8L, aliasBody.GetProperty("commentCount").GetInt64(), "commentCount on children pages");
            await Api.ExpectErrorAsync(f, HttpMethod.Get, "/api/v1/comments/" + parent + "/children?ordering=CreatedDescending&cursor=" + Uri.EscapeDataString(cursor), poster.AccessToken, null, HttpStatusCode.BadRequest, "InvalidContinuationToken", ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Get, "/api/v1/comments/" + replies[0] + "/children?ordering=CreatedAscending&cursor=" + Uri.EscapeDataString(cursor), poster.AccessToken, null, HttpStatusCode.BadRequest, "InvalidContinuationToken", ct).ConfigureAwait(false);
            JsonElement fromStart = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/comments/" + parent + "/children", poster.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal(7, fromStart.GetProperty("objects").GetArrayLength(), "Null cursor starts from the first reply");
            await Api.ExpectErrorAsync(f, HttpMethod.Get, "/api/v1/comments/cmt_unknown/children", poster.AccessToken, null, HttpStatusCode.NotFound, "NotFound", ct).ConfigureAwait(false);
        }

        private static async Task OrderingAsync(HttpFixture f, CancellationToken ct)
        {
            TestMember poster = await ContentTestKit.PosterAsync(f, ct).ConfigureAwait(false);
            TestMember a = await ContentTestKit.MemberAsync(f, ct).ConfigureAwait(false);
            TestMember b = await ContentTestKit.MemberAsync(f, ct).ConfigureAwait(false);
            string post = await ContentTestKit.PostIdAsync(f, poster.AccessToken, "Vote", ct).ConfigureAwait(false);
            string r1 = await ContentTestKit.CommentIdAsync(f, poster.AccessToken, post, "first", null, ct).ConfigureAwait(false);
            string r2 = await ContentTestKit.CommentIdAsync(f, poster.AccessToken, post, "second", null, ct).ConfigureAwait(false);
            string r3 = await ContentTestKit.CommentIdAsync(f, poster.AccessToken, post, "third", null, ct).ConfigureAwait(false);
            await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/comments/" + r2 + "/reactions/like", a.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/comments/" + r2 + "/reactions/heart", b.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            await ContentTestKit.CommentIdAsync(f, a.AccessToken, post, "reply", r3, ct).ConfigureAwait(false);
            JsonElement top = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/feed/" + post + "/comments", poster.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.True(ContentTestKit.Ids(top).SequenceEqual(new[] { r2, r3, r1 }), "Top: score 2 (reactions), score 2 (reply) younger, then 0: " + String.Join(",", ContentTestKit.Ids(top)));
            JsonElement newest = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/feed/" + post + "/comments?ordering=CreatedDescending", poster.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.True(ContentTestKit.Ids(newest).SequenceEqual(new[] { r3, r2, r1 }), "CreatedDescending");
            JsonElement oldest = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/feed/" + post + "/comments?ordering=CreatedAscending", poster.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.True(ContentTestKit.Ids(oldest).SequenceEqual(new[] { r1, r2, r3 }), "CreatedAscending");
            JsonElement topPage = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/feed/" + post + "/comments?maxResults=1", poster.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            List<string> walked = ContentTestKit.Ids(topPage);
            string token = Api.Str(topPage, "continuationToken");
            while (!String.IsNullOrEmpty(token))
            {
                JsonElement next = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/feed/" + post + "/comments?maxResults=1&continuationToken=" + Uri.EscapeDataString(token), poster.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                walked.AddRange(ContentTestKit.Ids(next));
                token = next.GetProperty("continuationToken").ValueKind == JsonValueKind.String ? next.GetProperty("continuationToken").GetString()! : String.Empty;
            }
            Check.True(walked.SequenceEqual(new[] { r2, r3, r1 }), "Top keyset paging");
            await Api.ExpectErrorAsync(f, HttpMethod.Get, "/api/v1/feed/" + post + "/comments?ordering=Hot", poster.AccessToken, null, HttpStatusCode.BadRequest, "ValidationFailed", ct).ConfigureAwait(false);
        }

        private static async Task FlatteningAsync(HttpFixture f, CancellationToken ct)
        {
            TestMember poster = await ContentTestKit.PosterAsync(f, ct).ConfigureAwait(false);
            TestMember a = await ContentTestKit.MemberAsync(f, ct, "Ada Deep").ConfigureAwait(false);
            TestMember b = await ContentTestKit.MemberAsync(f, ct, "Ben Deeper").ConfigureAwait(false);
            f.Settings.Comments.MaxDepth = 2;
            string post = await ContentTestKit.PostIdAsync(f, poster.AccessToken, "Deep", ct).ConfigureAwait(false);
            string d0 = await ContentTestKit.CommentIdAsync(f, a.AccessToken, post, "depth 0", null, ct).ConfigureAwait(false);
            string d1 = await ContentTestKit.CommentIdAsync(f, b.AccessToken, post, "depth 1", d0, ct).ConfigureAwait(false);
            JsonElement d2 = await ContentTestKit.CommentAsync(f, a.AccessToken, post, "depth 2", d1, ct).ConfigureAwait(false);
            Check.Equal(2, d2.GetProperty("depth").GetInt32(), "Depth 2 allowed");
            Check.Equal(JsonValueKind.Null, d2.GetProperty("replyToCommentId").ValueKind, "Not flattened");
            JsonElement flat = await ContentTestKit.CommentAsync(f, b.AccessToken, post, "reply to depth 2", Api.Str(d2, "id"), ct).ConfigureAwait(false);
            Check.Equal(2, flat.GetProperty("depth").GetInt32(), "Kept at max depth");
            Check.Equal(d1, Api.Str(flat, "parentCommentId"), "Sibling of the replied-to comment");
            Check.Equal(Api.Str(d2, "id"), Api.Str(flat, "replyToCommentId"), "replyToCommentId");
            Check.Equal(a.Id, Api.Str(flat.GetProperty("replyToUser"), "id"), "replyToUser");
            JsonElement flat2 = await ContentTestKit.CommentAsync(f, a.AccessToken, post, "reply to flattened", Api.Str(flat, "id"), ct).ConfigureAwait(false);
            Check.Equal(d1, Api.Str(flat2, "parentCommentId"), "Still a sibling");
            Comment stored = (await f.Database.Comments.ReadAsync(Api.Str(flat2, "id"), ct).ConfigureAwait(false))!;
            Check.Equal(d0 + "/" + d1 + "/" + stored.Id + "/", stored.Path, "Path stops at max depth");
            Check.Equal(d0, stored.RootCommentId, "Root");
            List<Notification> notes = await NotificationTestKit.ListAsync(f, a.Id, ct).ConfigureAwait(false);
            Check.True(notes.Any(n => n.Type == NotificationTypeEnum.CommentReply && n.ParentCommentId == Api.Str(d2, "id")), "Replied-to author gets CommentReply");
            JsonElement tree = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/feed/" + post + "/comments?depth=3&childLimit=10&ordering=CreatedAscending", poster.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            JsonElement level2 = tree.GetProperty("objects")[0].GetProperty("children")[0].GetProperty("children");
            Check.Equal(3, level2.GetArrayLength(), "Three siblings at the max depth");
            await ContentTestKit.VerifyCountsAsync(f, post, ct).ConfigureAwait(false);
        }

        private static async Task PlaceholdersAsync(HttpFixture f, CancellationToken ct)
        {
            TestMember poster = await ContentTestKit.PosterAsync(f, ct).ConfigureAwait(false);
            TestMember x = await ContentTestKit.MemberAsync(f, ct, "Xena Author").ConfigureAwait(false);
            TestMember y = await ContentTestKit.MemberAsync(f, ct, "Yuri Replier").ConfigureAwait(false);
            TestMember z = await ContentTestKit.MemberAsync(f, ct, "Zed Viewer").ConfigureAwait(false);
            TestMember admin = await ContentTestKit.AdminAsync(f, ct).ConfigureAwait(false);
            string post = await ContentTestKit.PostIdAsync(f, poster.AccessToken, "Placeholders", ct).ConfigureAwait(false);
            string deleted = await ContentTestKit.CommentIdAsync(f, x.AccessToken, post, "will be deleted", null, ct).ConfigureAwait(false);
            await ContentTestKit.CommentIdAsync(f, poster.AccessToken, post, "reply keeps it", deleted, ct).ConfigureAwait(false);
            string deletedLeaf = await ContentTestKit.CommentIdAsync(f, x.AccessToken, post, "deleted leaf", null, ct).ConfigureAwait(false);
            string hidden = await ContentTestKit.CommentIdAsync(f, x.AccessToken, post, "under review", null, ct).ConfigureAwait(false);
            await ContentTestKit.CommentIdAsync(f, y.AccessToken, post, "reply to hidden", hidden, ct).ConfigureAwait(false);
            string hiddenLeaf = await ContentTestKit.CommentIdAsync(f, x.AccessToken, post, "hidden leaf", null, ct).ConfigureAwait(false);
            string removed = await ContentTestKit.CommentIdAsync(f, x.AccessToken, post, "removed", null, ct).ConfigureAwait(false);
            await ContentTestKit.CommentIdAsync(f, y.AccessToken, post, "reply to removed", removed, ct).ConfigureAwait(false);
            string blocked = await ContentTestKit.CommentIdAsync(f, y.AccessToken, post, "by a blocked member", null, ct).ConfigureAwait(false);
            await ContentTestKit.CommentIdAsync(f, x.AccessToken, post, "reply to blocked", blocked, ct).ConfigureAwait(false);
            string blockedLeaf = await ContentTestKit.CommentIdAsync(f, y.AccessToken, post, "blocked leaf", null, ct).ConfigureAwait(false);
            await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/comments/" + deleted + "/reactions/like", z.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            await Api.ExpectAsync(f, HttpMethod.Delete, "/api/v1/comments/" + deleted, x.AccessToken, null, HttpStatusCode.NoContent, ct).ConfigureAwait(false);
            await Api.ExpectAsync(f, HttpMethod.Delete, "/api/v1/comments/" + deletedLeaf, x.AccessToken, null, HttpStatusCode.NoContent, ct).ConfigureAwait(false);
            await f.Services.Comments.SetModerationStateAsync(hidden, ModerationStateEnum.Hidden, null, "Reported", ct).ConfigureAwait(false);
            await f.Services.Comments.SetModerationStateAsync(hiddenLeaf, ModerationStateEnum.Hidden, null, "Reported", ct).ConfigureAwait(false);
            await f.Services.Comments.SetModerationStateAsync(removed, ModerationStateEnum.Removed, admin.Id, "Spam", ct).ConfigureAwait(false);
            await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/users/" + y.Id + "/block", z.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);

            Dictionary<string, JsonElement> forZ = Roots(await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/feed/" + post + "/comments", z.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false));
            Check.True(!forZ.ContainsKey(deletedLeaf) && !forZ.ContainsKey(hiddenLeaf) && !forZ.ContainsKey(blockedLeaf), "Placeholders without visible replies are omitted");
            AssertPlaceholder(forZ[deleted], "Deleted");
            AssertPlaceholder(forZ[hidden], "Hidden");
            AssertPlaceholder(forZ[removed], "Removed");
            AssertPlaceholder(forZ[blocked], "BlockedAuthor");
            Check.Equal(1, forZ[deleted].GetProperty("children").GetArrayLength(), "Replies of a placeholder stay");
            JsonElement page = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/feed/" + post + "/comments", x.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal(6L, page.GetProperty("commentCount").GetInt64(), "commentCount excludes deleted, hidden, and removed comments");
            Dictionary<string, JsonElement> forX = Roots(page);
            Check.True(forX.ContainsKey(hiddenLeaf), "The author sees a Hidden comment without replies");
            Check.Equal("Hidden", Api.Str(forX[hidden], "state"), "Author sees state Hidden");
            Check.Equal("under review", Api.Str(forX[hidden], "body"), "Author sees the content");
            Check.Equal("Hidden", Api.Str(forX[hidden].GetProperty("moderation"), "state"), "Author sees the moderation object");
            Check.Equal("Removed", Api.Str(forX[removed].GetProperty("moderation"), "state"), "Author sees Removed with moderation");
            Check.Equal("Visible", Api.Str(forX[blocked], "state"), "Others see the blocked member's comment");
            Dictionary<string, JsonElement> forAdmin = Roots(await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/feed/" + post + "/comments", admin.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false));
            Check.Equal("removed", Api.Str(forAdmin[removed], "body"), "Moderators see removed content");
            Check.Equal("Spam", Api.Str(forAdmin[removed].GetProperty("moderation"), "reason"), "Moderation reason");
            Check.True(forAdmin.ContainsKey(hiddenLeaf), "Moderators see hidden leaves");
            await Api.ExpectErrorAsync(f, HttpMethod.Put, "/api/v1/comments/" + deleted + "/reactions/like", y.AccessToken, null, HttpStatusCode.NotFound, "NotFound", ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Post, "/api/v1/feed/" + post + "/comments", y.AccessToken, new { body = "reply", parentCommentId = removed }, (HttpStatusCode)409, "ParentUnavailable", ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Get, "/api/v1/comments/" + deleted + "/revisions", y.AccessToken, null, HttpStatusCode.NotFound, "NotFound", ct).ConfigureAwait(false);
            TrellisKit.Core.Responses.CommentView modView = await f.Services.Comments.ModeratorViewAsync(await ContentTestKit.ActorAsync(f, admin.Id, ct).ConfigureAwait(false), hidden, ct).ConfigureAwait(false);
            Check.True(modView.Moderation != null && modView.Moderation.Reason == "Reported" && modView.Body == "under review", "Moderator view carries content and the moderation object");
            await f.Services.Comments.SetModerationStateAsync(hidden, ModerationStateEnum.None, admin.Id, null, ct).ConfigureAwait(false);
            Dictionary<string, JsonElement> restored = Roots(await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/feed/" + post + "/comments", y.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false));
            Check.Equal("Visible", Api.Str(restored[hidden], "state"), "Restored");
            await ContentTestKit.VerifyCountsAsync(f, post, ct).ConfigureAwait(false);
        }

        private static async Task PermalinkAsync(HttpFixture f, CancellationToken ct)
        {
            TestMember poster = await ContentTestKit.PosterAsync(f, ct).ConfigureAwait(false);
            string post = await ContentTestKit.PostIdAsync(f, poster.AccessToken, "Permalink", ct).ConfigureAwait(false);
            string a = await ContentTestKit.CommentIdAsync(f, poster.AccessToken, post, "a", null, ct).ConfigureAwait(false);
            string b = await ContentTestKit.CommentIdAsync(f, poster.AccessToken, post, "b", a, ct).ConfigureAwait(false);
            string c = await ContentTestKit.CommentIdAsync(f, poster.AccessToken, post, "c", b, ct).ConfigureAwait(false);
            string d = await ContentTestKit.CommentIdAsync(f, poster.AccessToken, post, "d", c, ct).ConfigureAwait(false);
            await ContentTestKit.CommentIdAsync(f, poster.AccessToken, post, "e", d, ct).ConfigureAwait(false);
            JsonElement link = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/comments/" + c + "?depth=2", poster.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal("FeedPost", Api.Str(link.GetProperty("target"), "type"), "Target type");
            Check.Equal(post, Api.Str(link.GetProperty("target"), "id"), "Target id");
            Check.True(link.GetProperty("ancestors").EnumerateArray().Select(x => Api.Str(x, "id")).SequenceEqual(new[] { a, b }), "Ancestors root first");
            Check.Equal(0, link.GetProperty("ancestors")[0].GetProperty("children").GetArrayLength(), "Ancestors without children");
            Check.Equal(c, Api.Str(link.GetProperty("comment"), "id"), "The comment");
            Check.Equal(d, Api.Str(link.GetProperty("comment").GetProperty("children")[0], "id"), "With children");
            Check.Equal(1, link.GetProperty("comment").GetProperty("children")[0].GetProperty("moreChildren").GetProperty("remainingCount").GetInt32(), "Depth limit applies");
            await Api.ExpectErrorAsync(f, HttpMethod.Get, "/api/v1/comments/cmt_missing", poster.AccessToken, null, HttpStatusCode.NotFound, "NotFound", ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Get, "/api/v1/comments/" + c + "?depth=0", poster.AccessToken, null, HttpStatusCode.BadRequest, "ValidationFailed", ct).ConfigureAwait(false);
        }

        private static async Task ErrorsAsync(HttpFixture f, CancellationToken ct)
        {
            TestMember poster = await ContentTestKit.PosterAsync(f, ct).ConfigureAwait(false);
            TestMember m = await ContentTestKit.MemberAsync(f, ct).ConfigureAwait(false);
            TestMember other = await ContentTestKit.MemberAsync(f, ct).ConfigureAwait(false);
            TestMember admin = await ContentTestKit.AdminAsync(f, ct).ConfigureAwait(false);
            f.Settings.Comments.NewAccountPerMinute = 1000;
            string closed = Api.Str(await ContentTestKit.PostAsync(f, poster.AccessToken, new { body = "No comments", commentsEnabled = false }, ct).ConfigureAwait(false), "id");
            await Api.ExpectErrorAsync(f, HttpMethod.Post, "/api/v1/feed/" + closed + "/comments", m.AccessToken, new { body = "hi" }, (HttpStatusCode)409, "CommentsDisabled", ct).ConfigureAwait(false);
            string scheduled = Api.Str(await ContentTestKit.PostAsync(f, poster.AccessToken, new { body = "Later", publishAtUtc = DateTime.UtcNow.AddHours(1) }, ct).ConfigureAwait(false), "id");
            await Api.ExpectErrorAsync(f, HttpMethod.Post, "/api/v1/feed/" + scheduled + "/comments", poster.AccessToken, new { body = "hi" }, HttpStatusCode.NotFound, "NotFound", ct).ConfigureAwait(false);
            string post = await ContentTestKit.PostIdAsync(f, poster.AccessToken, "Open", ct).ConfigureAwait(false);
            string elsewhere = await ContentTestKit.PostIdAsync(f, poster.AccessToken, "Elsewhere", ct).ConfigureAwait(false);
            string foreign = await ContentTestKit.CommentIdAsync(f, m.AccessToken, elsewhere, "foreign", null, ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Post, "/api/v1/feed/" + post + "/comments", m.AccessToken, new { body = "x", parentCommentId = foreign }, HttpStatusCode.NotFound, "NotFound", ct).ConfigureAwait(false);
            string gone = await ContentTestKit.CommentIdAsync(f, m.AccessToken, post, "gone", null, ct).ConfigureAwait(false);
            await Api.ExpectAsync(f, HttpMethod.Delete, "/api/v1/comments/" + gone, m.AccessToken, null, HttpStatusCode.NoContent, ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Post, "/api/v1/feed/" + post + "/comments", other.AccessToken, new { body = "x", parentCommentId = gone }, (HttpStatusCode)409, "ParentUnavailable", ct).ConfigureAwait(false);
            string mine = await ContentTestKit.CommentIdAsync(f, m.AccessToken, post, "mine", null, ct).ConfigureAwait(false);
            await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/users/" + other.Id + "/block", m.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Post, "/api/v1/feed/" + post + "/comments", other.AccessToken, new { body = "x", parentCommentId = mine }, HttpStatusCode.Forbidden, "InteractionBlocked", ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Post, "/api/v1/feed/" + post + "/comments", m.AccessToken, new { body = "   " }, HttpStatusCode.BadRequest, "ValidationFailed", ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Post, "/api/v1/feed/" + post + "/comments", m.AccessToken, new { body = new string('c', f.Settings.Comments.MaxBodyLength + 1) }, HttpStatusCode.BadRequest, "ValidationFailed", ct).ConfigureAwait(false);
            string image = await ContentTestKit.MediaAsync(f, m.Id, MediaKindEnum.Image, ct).ConfigureAwait(false);
            string image2 = await ContentTestKit.MediaAsync(f, m.Id, MediaKindEnum.Image, ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Post, "/api/v1/feed/" + post + "/comments", m.AccessToken, new { body = "two", mediaIds = new[] { image, image2 } }, HttpStatusCode.BadRequest, "MediaLimitExceeded", ct).ConfigureAwait(false);
            JsonElement withMedia = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/feed/" + post + "/comments", m.AccessToken, new { body = "", mediaIds = new[] { image } }, HttpStatusCode.Created, ct).ConfigureAwait(false);
            Check.Equal(1, withMedia.GetProperty("attachments").GetArrayLength(), "One attachment");
            Check.Equal(1, (await f.Database.Comments.ReadAsync(Api.Str(withMedia, "id"), ct).ConfigureAwait(false))!.AttachmentCount, "attachmentcount");
            await Api.ExpectErrorAsync(f, HttpMethod.Put, "/api/v1/comments/" + mine, other.AccessToken, new { body = "edit" }, HttpStatusCode.Forbidden, "Forbidden", ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Put, "/api/v1/comments/" + mine, admin.AccessToken, new { body = "edit" }, HttpStatusCode.Forbidden, "Forbidden", ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Delete, "/api/v1/comments/" + mine, other.AccessToken, null, HttpStatusCode.Forbidden, "Forbidden", ct).ConfigureAwait(false);
            JsonElement edited = await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/comments/" + mine, m.AccessToken, new { body = "mine, edited" }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal("mine, edited", Api.Str(edited, "body"), "Author edits");
            await Api.ExpectAsync(f, HttpMethod.Delete, "/api/v1/comments/" + mine, admin.AccessToken, null, HttpStatusCode.NoContent, ct).ConfigureAwait(false);
            Check.Equal(CommentDeletedReasonEnum.Admin, (await f.Database.Comments.ReadAsync(mine, ct).ConfigureAwait(false))!.DeletedReason, "Deleted by an admin");
            await Api.ExpectErrorAsync(f, HttpMethod.Delete, "/api/v1/comments/" + mine, m.AccessToken, null, HttpStatusCode.NotFound, "NotFound", ct).ConfigureAwait(false);
            await SafetyHelpers.UpdateUserAsync(f, m.Id, u => { u.IsMuted = true; u.MutedUntilUtc = DateTime.UtcNow.AddHours(1); }, ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Post, "/api/v1/feed/" + post + "/comments", m.AccessToken, new { body = "muted" }, HttpStatusCode.Forbidden, "Muted", ct).ConfigureAwait(false);
            await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/feed/" + post + "/comments", m.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            await ContentTestKit.VerifyCountsAsync(f, post, ct).ConfigureAwait(false);
        }

        private static async Task NotificationsAsync(HttpFixture f, CancellationToken ct)
        {
            TestMember poster = await ContentTestKit.PosterAsync(f, ct, "Pat Poster", "patposter").ConfigureAwait(false);
            TestMember a = await ContentTestKit.MemberAsync(f, ct, "Amy Commenter", "amycomm").ConfigureAwait(false);
            TestMember b = await ContentTestKit.MemberAsync(f, ct, "Bob Replier", "bobreply").ConfigureAwait(false);
            string post = await ContentTestKit.PostIdAsync(f, poster.AccessToken, "Notify", ct).ConfigureAwait(false);
            string top = await ContentTestKit.CommentIdAsync(f, a.AccessToken, post, "Top level", null, ct).ConfigureAwait(false);
            List<Notification> forPoster = await NotificationTestKit.ListAsync(f, poster.Id, ct).ConfigureAwait(false);
            Check.True(forPoster.Count == 1 && forPoster[0].Type == NotificationTypeEnum.PostComment && forPoster[0].CommentId == top, "PostComment to the post author");
            await ContentTestKit.CommentIdAsync(f, b.AccessToken, post, "Reply to @amycomm and @patposter", top, ct).ConfigureAwait(false);
            List<Notification> forA = await NotificationTestKit.ListAsync(f, a.Id, ct).ConfigureAwait(false);
            Check.True(forA.Count == 1 && forA[0].Type == NotificationTypeEnum.CommentReply, "Only CommentReply when the reply also mentions the parent author");
            forPoster = await NotificationTestKit.ListAsync(f, poster.Id, ct).ConfigureAwait(false);
            Check.Equal(1, forPoster.Count(n => n.Type == NotificationTypeEnum.Mention), "Mentioned post author gets a Mention");
            await ContentTestKit.CommentIdAsync(f, a.AccessToken, post, "Replying to myself", top, ct).ConfigureAwait(false);
            Check.Equal(1, (await NotificationTestKit.ListAsync(f, a.Id, ct).ConfigureAwait(false)).Count, "No notification for your own reply");
            f.Settings.Content.EditGraceSeconds = 0;
            string plain = await ContentTestKit.CommentIdAsync(f, b.AccessToken, post, "Plain", null, ct).ConfigureAwait(false);
            await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/comments/" + plain, b.AccessToken, new { body = "Plain, now with @amycomm" }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal(1, (await NotificationTestKit.ListAsync(f, a.Id, ct).ConfigureAwait(false)).Count(n => n.Type == NotificationTypeEnum.Mention), "A mention added by an edit notifies");
        }

        private static async Task RateLimitsAsync(HttpFixture f, CancellationToken ct)
        {
            TestMember poster = await ContentTestKit.PosterAsync(f, ct).ConfigureAwait(false);
            TestMember fresh = await ContentTestKit.MemberAsync(f, ct).ConfigureAwait(false);
            string post = await ContentTestKit.PostIdAsync(f, poster.AccessToken, "Limits", ct).ConfigureAwait(false);
            f.Settings.Comments.NewAccountPerMinute = 2;
            f.Settings.Comments.PerMinute = 5;
            await ContentTestKit.CommentIdAsync(f, fresh.AccessToken, post, "1", null, ct).ConfigureAwait(false);
            await ContentTestKit.CommentIdAsync(f, fresh.AccessToken, post, "2", null, ct).ConfigureAwait(false);
            ApiResult limited = await Api.SendAsync(f, HttpMethod.Post, "/api/v1/feed/" + post + "/comments", fresh.AccessToken, new { body = "3" }, ct).ConfigureAwait(false);
            Check.Equal((HttpStatusCode)429, limited.Status, "New account per minute");
            Check.True(!String.IsNullOrEmpty(limited.RetryAfter), "Retry-After");
            f.Settings.Comments.NewAccountHours = 0;
            for (int i = 0; i < 3; i++) await ContentTestKit.CommentIdAsync(f, fresh.AccessToken, post, "older " + i, null, ct).ConfigureAwait(false);
            ApiResult regular = await Api.SendAsync(f, HttpMethod.Post, "/api/v1/feed/" + post + "/comments", fresh.AccessToken, new { body = "6" }, ct).ConfigureAwait(false);
            Check.Equal((HttpStatusCode)429, regular.Status, "Regular per minute");
            f.Settings.RateLimits.EditsPerHour = 1;
            string mine = (await f.Database.Comments.ListByAuthorAsync(fresh.Id, null, 1, false, ct).ConfigureAwait(false))[0].Id;
            await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/comments/" + mine, fresh.AccessToken, new { body = "edit 1" }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            ApiResult edits = await Api.SendAsync(f, HttpMethod.Put, "/api/v1/comments/" + mine, fresh.AccessToken, new { body = "edit 2" }, ct).ConfigureAwait(false);
            Check.Equal((HttpStatusCode)429, edits.Status, "Edits per hour");
        }

        private static async Task IntegrityAsync(HttpFixture f, CancellationToken ct)
        {
            TestMember poster = await ContentTestKit.PosterAsync(f, ct).ConfigureAwait(false);
            f.Settings.Comments.NewAccountPerMinute = 10000;
            f.Settings.Comments.NewAccountPerDay = 10000;
            List<TestMember> members = new List<TestMember>();
            for (int i = 0; i < 6; i++) members.Add(await ContentTestKit.MemberAsync(f, ct, "Racer " + i).ConfigureAwait(false));
            string post = await ContentTestKit.PostIdAsync(f, poster.AccessToken, "Race", ct).ConfigureAwait(false);
            List<string> roots = new List<string>();
            for (int i = 0; i < 3; i++) roots.Add(await ContentTestKit.CommentIdAsync(f, members[i].AccessToken, post, "root " + i, null, ct).ConfigureAwait(false));
            string mid = await ContentTestKit.CommentIdAsync(f, members[0].AccessToken, post, "middle", roots[0], ct).ConfigureAwait(false);
            string deep = await ContentTestKit.CommentIdAsync(f, members[1].AccessToken, post, "deep", mid, ct).ConfigureAwait(false);
            List<string> doomed = new List<string>();
            for (int i = 0; i < 3; i++) doomed.Add(await ContentTestKit.CommentIdAsync(f, members[i].AccessToken, post, "doomed " + i, mid, ct).ConfigureAwait(false));
            List<Task> work = new List<Task>();
            for (int i = 0; i < 30; i++)
            {
                TestMember who = members[i % members.Count];
                string parent = i % 4 == 0 ? roots[i % 3] : i % 4 == 1 ? mid : i % 4 == 2 ? deep : roots[1];
                work.Add(ContentTestKit.CommentIdAsync(f, who.AccessToken, post, "reply " + i, parent, ct));
            }
            for (int i = 0; i < 3; i++) work.Add(Api.ExpectAsync(f, HttpMethod.Delete, "/api/v1/comments/" + doomed[i], members[i].AccessToken, null, HttpStatusCode.NoContent, ct));
            foreach (TestMember who in members)
            {
                work.Add(Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/comments/" + mid + "/reactions/like", who.AccessToken, null, HttpStatusCode.OK, ct));
                work.Add(Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/feed/" + post + "/reactions/idea", who.AccessToken, null, HttpStatusCode.OK, ct));
            }
            work.Add(f.Services.Comments.SetModerationStateAsync(roots[2], ModerationStateEnum.Hidden, null, "race", ct));
            await Task.WhenAll(work).ConfigureAwait(false);
            await ContentTestKit.VerifyCountsAsync(f, post, ct).ConfigureAwait(false);
            Comment midRow = (await f.Database.Comments.ReadAsync(mid, ct).ConfigureAwait(false))!;
            Check.Equal(1 + 8, midRow.ChildCount, "deep plus eight direct replies (the doomed ones are gone)");
            Check.Equal(6 + 2 * midRow.ChildCount, midRow.Score, "score = reactions + 2 * children");
        }

        private static Dictionary<string, JsonElement> Roots(JsonElement page)
        {
            return page.GetProperty("objects").EnumerateArray().ToDictionary(o => Api.Str(o, "id"), o => o);
        }

        private static void AssertPlaceholder(JsonElement comment, string state)
        {
            Check.Equal(state, Api.Str(comment, "state"), "Placeholder state " + state);
            Check.Equal(JsonValueKind.Null, comment.GetProperty("author").ValueKind, state + ": no author");
            Check.Equal(JsonValueKind.Null, comment.GetProperty("body").ValueKind, state + ": no body");
            Check.Equal(0, comment.GetProperty("mentions").GetArrayLength(), state + ": no mentions");
            Check.Equal(0, comment.GetProperty("attachments").GetArrayLength(), state + ": no attachments");
            Check.Equal(JsonValueKind.Null, comment.GetProperty("linkPreview").ValueKind, state + ": no preview");
            Check.Equal(0L, comment.GetProperty("reactionTotal").GetInt64(), state + ": reactions zeroed");
            Check.Equal(0, comment.GetProperty("reactionCounts").EnumerateObject().Count(), state + ": no counts");
            Check.Equal(JsonValueKind.Null, comment.GetProperty("moderation").ValueKind, state + ": no moderation object");
            Check.True(comment.GetProperty("viewer").EnumerateObject().All(p => p.Value.ValueKind == JsonValueKind.False), state + ": every viewer flag false");
            ContentTestKit.CheckMetadata(comment, state + " placeholder");
        }

        #endregion
    }
}
