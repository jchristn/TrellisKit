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
    using TrellisKit.Core.PropertyConfig;

    /// <summary>
    /// Phase 2 reactions (contract section 6): the property's set, idempotent react and unreact with several keys per
    /// member, counts under concurrent PUTs and DELETEs, UnknownReaction, InteractionBlocked, Muted, removed keys, who
    /// reacted, notifications, and the rate limit.
    /// </summary>
    public static class ReactionSuite
    {
        #region Public-Members

        /// <summary>
        /// Suite identifier.
        /// </summary>
        public const string Id = "Reactions";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the suite.
        /// </summary>
        /// <returns>Suite.</returns>
        public static TestSuiteDescriptor Build()
        {
            return new TestSuiteDescriptor(Id, "Reactions (" + TestEnvironment.Provider + ")", new[]
            {
                Case("Reactions.Set", "GET /api/v1/reactions/set returns the property's reactions in order and likeKey", SetAsync),
                Case("Reactions.Idempotent", "PUT and DELETE are idempotent, a member may hold several keys, payloads carry the reaction fields, and only the first add notifies someone else", IdempotentAsync),
                Case("Reactions.Concurrent", "Counts, reactionTotal, and comment score match the rows after concurrent PUTs and DELETEs from many members", ConcurrentAsync),
                Case("Reactions.Errors", "UnknownReaction, InteractionBlocked, Muted, NotFound for unpublished targets, and the rate limit", ErrorsAsync),
                Case("Reactions.RemovedKey", "A key removed from the property hides its counts but keeps the rows; restoring it restores the counts", RemovedKeyAsync),
                Case("Reactions.WhoReacted", "Who reacted is newest first, filters by key, omits blocked members, and carries labels and tags", WhoReactedAsync)
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

        private static async Task SetAsync(HttpFixture f, CancellationToken ct)
        {
            TestMember m = await ContentTestKit.MemberAsync(f, ct).ConfigureAwait(false);
            JsonElement set = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/reactions/set", m.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            List<string> keys = set.GetProperty("objects").EnumerateArray().Select(i => Api.Str(i, "key")).ToList();
            Check.True(keys.SequenceEqual(f.Property.Reactions.Select(r => r.Key)), "Keys in property order");
            Check.Equal(f.Property.Reactions[0].Key, Api.Str(set, "likeKey"), "likeKey is the first key");
            JsonElement first = set.GetProperty("objects")[0];
            Check.True(Api.Str(first, "emoji").Length > 0 && Api.Str(first, "label").Length > 0, "Emoji and label");
            await Api.ExpectErrorAsync(f, HttpMethod.Get, "/api/v1/reactions/set", null, null, HttpStatusCode.Unauthorized, "Unauthorized", ct).ConfigureAwait(false);
        }

        private static async Task IdempotentAsync(HttpFixture f, CancellationToken ct)
        {
            TestMember poster = await ContentTestKit.PosterAsync(f, ct).ConfigureAwait(false);
            TestMember m = await ContentTestKit.MemberAsync(f, ct).ConfigureAwait(false);
            string post = await ContentTestKit.PostIdAsync(f, poster.AccessToken, "React", ct).ConfigureAwait(false);
            for (int i = 0; i < 2; i++)
            {
                JsonElement state = await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/feed/" + post + "/reactions/like", m.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                Check.Equal(1L, state.GetProperty("reactionCounts").GetProperty("like").GetInt64(), "One like (pass " + i + ")");
                Check.Equal(1L, state.GetProperty("reactionTotal").GetInt64(), "Total");
                Check.True(state.GetProperty("myReactions").EnumerateArray().Select(e => e.GetString()).SequenceEqual(new[] { "like" }), "Mine");
            }
            JsonElement two = await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/feed/" + post + "/reactions/idea", m.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal(2L, two.GetProperty("reactionTotal").GetInt64(), "Two keys");
            Check.Equal(2, two.GetProperty("myReactions").GetArrayLength(), "Both mine");
            await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/feed/" + post + "/reactions/like", poster.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            JsonElement read = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/feed/" + post, m.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal(2L, read.GetProperty("reactionCounts").GetProperty("like").GetInt64(), "Post payload counts");
            Check.Equal(3L, read.GetProperty("reactionTotal").GetInt64(), "Post payload total");
            Check.True(!read.GetProperty("reactionCounts").TryGetProperty("heart", out JsonElement _), "Only keys above zero");
            List<Notification> notes = await NotificationTestKit.ListAsync(f, poster.Id, ct).ConfigureAwait(false);
            Check.Equal(1, notes.Count(n => n.Type == NotificationTypeEnum.Reaction), "One aggregated Reaction notification; self reaction does not notify");
            for (int i = 0; i < 2; i++)
            {
                JsonElement removed = await Api.ExpectAsync(f, HttpMethod.Delete, "/api/v1/feed/" + post + "/reactions/like", m.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                Check.Equal(1L, removed.GetProperty("reactionCounts").GetProperty("like").GetInt64(), "The poster's like remains (pass " + i + ")");
                Check.True(removed.GetProperty("myReactions").EnumerateArray().Select(e => e.GetString()).SequenceEqual(new[] { "idea" }), "Idea remains mine");
            }
            Check.Equal(2L, await f.Database.Reactions.CountRowsAsync(TargetTypeEnum.FeedPost, post, ct).ConfigureAwait(false), "Rows");
            await ContentTestKit.VerifyCountsAsync(f, post, ct).ConfigureAwait(false);
        }

        private static async Task ConcurrentAsync(HttpFixture f, CancellationToken ct)
        {
            TestMember poster = await ContentTestKit.PosterAsync(f, ct).ConfigureAwait(false);
            List<TestMember> members = new List<TestMember>();
            for (int i = 0; i < 12; i++) members.Add(await ContentTestKit.MemberAsync(f, ct, "Reactor " + i).ConfigureAwait(false));
            string post = await ContentTestKit.PostIdAsync(f, poster.AccessToken, "Popular", ct).ConfigureAwait(false);
            string comment = await ContentTestKit.CommentIdAsync(f, poster.AccessToken, post, "Popular comment", null, ct).ConfigureAwait(false);
            await ContentTestKit.CommentIdAsync(f, members[0].AccessToken, post, "reply", comment, ct).ConfigureAwait(false);
            List<Task> puts = new List<Task>();
            foreach (TestMember m in members)
            {
                foreach (string key in new[] { "like", "heart" })
                {
                    puts.Add(Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/feed/" + post + "/reactions/" + key, m.AccessToken, null, HttpStatusCode.OK, ct));
                    puts.Add(Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/feed/" + post + "/reactions/" + key, m.AccessToken, null, HttpStatusCode.OK, ct));
                    puts.Add(Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/comments/" + comment + "/reactions/" + key, m.AccessToken, null, HttpStatusCode.OK, ct));
                }
            }
            await Task.WhenAll(puts).ConfigureAwait(false);
            List<Task> mixed = new List<Task>();
            for (int i = 0; i < members.Count; i++)
            {
                if (i % 2 == 0)
                {
                    mixed.Add(Api.ExpectAsync(f, HttpMethod.Delete, "/api/v1/feed/" + post + "/reactions/like", members[i].AccessToken, null, HttpStatusCode.OK, ct));
                    mixed.Add(Api.ExpectAsync(f, HttpMethod.Delete, "/api/v1/comments/" + comment + "/reactions/heart", members[i].AccessToken, null, HttpStatusCode.OK, ct));
                }
                else mixed.Add(Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/feed/" + post + "/reactions/idea", members[i].AccessToken, null, HttpStatusCode.OK, ct));
            }
            await Task.WhenAll(mixed).ConfigureAwait(false);
            JsonElement read = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/feed/" + post, poster.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            JsonElement counts = read.GetProperty("reactionCounts");
            Check.Equal(6L, counts.GetProperty("like").GetInt64(), "like");
            Check.Equal(12L, counts.GetProperty("heart").GetInt64(), "heart");
            Check.Equal(6L, counts.GetProperty("idea").GetInt64(), "idea");
            Check.Equal(24L, read.GetProperty("reactionTotal").GetInt64(), "total");
            Comment row = (await f.Database.Comments.ReadAsync(comment, ct).ConfigureAwait(false))!;
            Check.Equal(18, row.ReactionTotal, "Comment reactions");
            Check.Equal(18 + 2, row.Score, "Comment score");
            Dictionary<string, Dictionary<string, long>> stored = await f.Database.Reactions.CountsAsync(TargetTypeEnum.FeedPost, new[] { post }, ct).ConfigureAwait(false);
            Check.Equal(6L, stored[post]["like"], "reactioncounts row");
            await ContentTestKit.VerifyCountsAsync(f, post, ct).ConfigureAwait(false);
        }

        private static async Task ErrorsAsync(HttpFixture f, CancellationToken ct)
        {
            TestMember poster = await ContentTestKit.PosterAsync(f, ct).ConfigureAwait(false);
            TestMember m = await ContentTestKit.MemberAsync(f, ct).ConfigureAwait(false);
            string post = await ContentTestKit.PostIdAsync(f, poster.AccessToken, "Errors", ct).ConfigureAwait(false);
            string comment = await ContentTestKit.CommentIdAsync(f, poster.AccessToken, post, "comment", null, ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Put, "/api/v1/feed/" + post + "/reactions/thumbsup", m.AccessToken, null, HttpStatusCode.BadRequest, "UnknownReaction", ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Delete, "/api/v1/comments/" + comment + "/reactions/LIKE", m.AccessToken, null, HttpStatusCode.BadRequest, "UnknownReaction", ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Put, "/api/v1/feed/fpo_missing/reactions/like", m.AccessToken, null, HttpStatusCode.NotFound, "NotFound", ct).ConfigureAwait(false);
            string scheduled = Api.Str(await ContentTestKit.PostAsync(f, poster.AccessToken, new { body = "later", publishAtUtc = DateTime.UtcNow.AddHours(1) }, ct).ConfigureAwait(false), "id");
            await Api.ExpectErrorAsync(f, HttpMethod.Put, "/api/v1/feed/" + scheduled + "/reactions/like", poster.AccessToken, null, HttpStatusCode.NotFound, "NotFound", ct).ConfigureAwait(false);
            string closed = Api.Str(await ContentTestKit.PostAsync(f, poster.AccessToken, new { body = "closed", commentsEnabled = false }, ct).ConfigureAwait(false), "id");
            await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/feed/" + closed + "/reactions/like", m.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/users/" + m.Id + "/block", poster.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Put, "/api/v1/feed/" + post + "/reactions/like", m.AccessToken, null, HttpStatusCode.Forbidden, "InteractionBlocked", ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Put, "/api/v1/comments/" + comment + "/reactions/like", m.AccessToken, null, HttpStatusCode.Forbidden, "InteractionBlocked", ct).ConfigureAwait(false);
            TestMember muted = await ContentTestKit.MemberAsync(f, ct).ConfigureAwait(false);
            await SafetyHelpers.UpdateUserAsync(f, muted.Id, u => u.IsMuted = true, ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Put, "/api/v1/feed/" + post + "/reactions/like", muted.AccessToken, null, HttpStatusCode.Forbidden, "Muted", ct).ConfigureAwait(false);
            TestMember busy = await ContentTestKit.MemberAsync(f, ct).ConfigureAwait(false);
            f.Settings.Reactions.PerMinute = 3;
            await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/feed/" + post + "/reactions/like", busy.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            await Api.ExpectAsync(f, HttpMethod.Delete, "/api/v1/feed/" + post + "/reactions/like", busy.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/feed/" + post + "/reactions/heart", busy.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            ApiResult limited = await Api.SendAsync(f, HttpMethod.Put, "/api/v1/feed/" + post + "/reactions/idea", busy.AccessToken, null, ct).ConfigureAwait(false);
            Check.Equal((HttpStatusCode)429, limited.Status, "reactions.perMinute");
            Check.True(!String.IsNullOrEmpty(limited.RetryAfter), "Retry-After");
        }

        private static async Task RemovedKeyAsync(HttpFixture f, CancellationToken ct)
        {
            TestMember poster = await ContentTestKit.PosterAsync(f, ct).ConfigureAwait(false);
            TestMember m = await ContentTestKit.MemberAsync(f, ct).ConfigureAwait(false);
            string post = await ContentTestKit.PostIdAsync(f, poster.AccessToken, "Keys", ct).ConfigureAwait(false);
            await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/feed/" + post + "/reactions/like", m.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/feed/" + post + "/reactions/heart", m.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            PropertyReaction heart = f.Property.Reactions.First(r => r.Key == "heart");
            int index = f.Property.Reactions.IndexOf(heart);
            f.Property.Reactions.Remove(heart);
            JsonElement hidden = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/feed/" + post, m.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.True(!hidden.GetProperty("reactionCounts").TryGetProperty("heart", out JsonElement _), "Removed key hidden");
            Check.Equal(1L, hidden.GetProperty("reactionTotal").GetInt64(), "Total of current keys");
            Check.True(hidden.GetProperty("myReactions").EnumerateArray().Select(e => e.GetString()).SequenceEqual(new[] { "like" }), "myReactions of current keys");
            Check.Equal(2L, await f.Database.Reactions.CountRowsAsync(TargetTypeEnum.FeedPost, post, ct).ConfigureAwait(false), "Rows kept");
            await Api.ExpectErrorAsync(f, HttpMethod.Put, "/api/v1/feed/" + post + "/reactions/heart", m.AccessToken, null, HttpStatusCode.BadRequest, "UnknownReaction", ct).ConfigureAwait(false);
            JsonElement who = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/feed/" + post + "/reactions", m.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal(1, who.GetProperty("objects").GetArrayLength(), "Who reacted hides removed keys");
            f.Property.Reactions.Insert(index, heart);
            JsonElement restored = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/feed/" + post, m.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal(1L, restored.GetProperty("reactionCounts").GetProperty("heart").GetInt64(), "Restored");
            Check.Equal(2L, restored.GetProperty("reactionTotal").GetInt64(), "Restored total");
        }

        private static async Task WhoReactedAsync(HttpFixture f, CancellationToken ct)
        {
            TestMember poster = await ContentTestKit.PosterAsync(f, ct).ConfigureAwait(false);
            TestMember a = await ContentTestKit.MemberAsync(f, ct, "Ann").ConfigureAwait(false);
            TestMember b = await ContentTestKit.MemberAsync(f, ct, "Ben").ConfigureAwait(false);
            TestMember c = await ContentTestKit.MemberAsync(f, ct, "Cat").ConfigureAwait(false);
            string post = await ContentTestKit.PostIdAsync(f, poster.AccessToken, "Who", ct).ConfigureAwait(false);
            string comment = await ContentTestKit.CommentIdAsync(f, poster.AccessToken, post, "comment", null, ct).ConfigureAwait(false);
            await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/feed/" + post + "/reactions/like", a.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/feed/" + post + "/reactions/heart", b.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/feed/" + post + "/reactions/like", c.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/comments/" + comment + "/reactions/idea", a.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            JsonElement all = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/feed/" + post + "/reactions", poster.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            List<string> users = all.GetProperty("objects").EnumerateArray().Select(o => Api.Str(o.GetProperty("user"), "id")).ToList();
            Check.True(users.SequenceEqual(new[] { c.Id, b.Id, a.Id }), "Newest first");
            Check.Equal(50, all.GetProperty("maxResults").GetInt32(), "Default 50");
            foreach (JsonElement o in all.GetProperty("objects").EnumerateArray())
            {
                ContentTestKit.CheckMetadata(o, "reaction row");
                Check.True(o.TryGetProperty("key", out JsonElement _) && o.TryGetProperty("createdUtc", out JsonElement _) && Api.Str(o, "id").StartsWith("rxn_", StringComparison.Ordinal), "Row fields");
            }
            JsonElement likes = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/feed/" + post + "/reactions/enumerate", poster.AccessToken, new { filters = new Dictionary<string, string> { { "key", "like" } } }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal(2, likes.GetProperty("objects").GetArrayLength(), "Key filter");
            await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/users/" + c.Id + "/block", poster.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            JsonElement afterBlock = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/feed/" + post + "/reactions?key=like", poster.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.True(afterBlock.GetProperty("objects").EnumerateArray().Select(o => Api.Str(o.GetProperty("user"), "id")).SequenceEqual(new[] { a.Id }), "Blocked members omitted");
            JsonElement page = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/feed/" + post + "/reactions?maxResults=1", b.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            JsonElement next = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/feed/" + post + "/reactions?maxResults=1&continuationToken=" + Uri.EscapeDataString(Api.Str(page, "continuationToken")), b.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal(b.Id, Api.Str(next.GetProperty("objects")[0].GetProperty("user"), "id"), "Continuation");
            JsonElement onComment = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/comments/" + comment + "/reactions", b.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal(1, onComment.GetProperty("objects").GetArrayLength(), "Comment reactions");
            await Api.ExpectErrorAsync(f, HttpMethod.Get, "/api/v1/feed/" + post + "/reactions?key=nope", b.AccessToken, null, HttpStatusCode.BadRequest, "UnknownReaction", ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Get, "/api/v1/feed/" + post + "/reactions?mood=happy", b.AccessToken, null, HttpStatusCode.BadRequest, "ValidationFailed", ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Get, "/api/v1/feed/fpo_missing/reactions", b.AccessToken, null, HttpStatusCode.NotFound, "NotFound", ct).ConfigureAwait(false);
        }

        #endregion
    }
}
