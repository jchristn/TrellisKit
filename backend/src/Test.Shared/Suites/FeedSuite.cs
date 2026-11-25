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
    using TrellisKit.Core.Exceptions;
    using TrellisKit.Core.Models;
    using TrellisKit.Server.Services;

    /// <summary>
    /// Phase 2 feed posts (contract section 5, docs/ENUMERATION_AND_METADATA.md): payload shape, the pinned prefix and
    /// continuation, label and tag filters, blocks, scheduling and the publish worker, processing media, delete,
    /// mentions, link previews, and rate limits.
    /// </summary>
    public static class FeedSuite
    {
        #region Public-Members

        /// <summary>
        /// Suite identifier.
        /// </summary>
        public const string Id = "Feed";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the suite.
        /// </summary>
        /// <returns>Suite.</returns>
        public static TestSuiteDescriptor Build()
        {
            return new TestSuiteDescriptor(Id, "Feed posts (" + TestEnvironment.Provider + ")", new[]
            {
                Case("Feed.Shape", "A created post has the contract shape plus audience, labels, and tags; GET returns it; viewer flags follow the caller", ShapeAsync),
                Case("Feed.PinnedFirst", "The Feed ordering starts with pinned posts, continues unpinned posts without repeating pins, and PinLimitReached applies", PinnedAsync),
                Case("Feed.Continuation", "Paging through the feed under concurrent inserts has no duplicates and no gaps", ContinuationAsync),
                Case("Feed.Metadata", "Label (All, Any) and tag (value, *) filters, staff stripping, the staff filter 400, and staff entries kept on member edits", MetadataAsync),
                Case("Feed.Audience", "Audience is enforced per object (feed, detail, comments, reactions, revisions, media, notifications) and filterable on the feed, my posts, and the public feed", AudienceAsync),
                Case("Feed.Blocks", "Posts by members the caller blocked are omitted from the caller's feed only", BlocksAsync),
                Case("Feed.Scheduling", "Scheduled posts stay out of the feed, publish through the worker once, start the fan-out, audit, and notify mentions on publish", SchedulingAsync),
                Case("Feed.ProcessingMedia", "A post with Processing media is held in Processing and published when the media is Ready", ProcessingAsync),
                Case("Feed.Delete", "Author and SystemAdmin delete (soft); others get 403 or 404; deleted posts and their comments are unreachable", DeleteAsync),
                Case("Feed.Mentions", "Mentions resolve case-insensitively, keep the handle as written, notify once, skip self and blocks, honor the cap, and notify only new members on edit", MentionsAsync),
                Case("Feed.LinkPreview", "The first URL gets a preview card unless showLinkPreview is false", LinkPreviewAsync),
                Case("Feed.WordFilter", "Block terms reject title, body, alt text, and comments with ContentFiltered; Flag terms save, file one system report, and alert SystemAdmins with a link", WordFilterAsync),
                Case("Feed.WordFilterStarter", "The starter list loads as Flag terms once; a second load skips every term", WordFilterStarterAsync),
                Case("Feed.Validation", "Title, body, publishAtUtc, and Public visibility are validated", ValidationAsync),
                Case("Regression.API06.PinnedTotals", "ordering=Feed counts pinned posts in totalRecords on every page", async (f, ct) =>
                {
                    TestMember admin = await ContentTestKit.AdminAsync(f, ct).ConfigureAwait(false);
                    TestMember reader = await ContentTestKit.MemberAsync(f, ct).ConfigureAwait(false);
                    List<string> ids = new List<string>();
                    for (int i = 0; i < 5; i++) ids.Add(await ContentTestKit.PostIdAsync(f, admin.AccessToken, "Total " + i, ct).ConfigureAwait(false));
                    await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/feed/" + ids[0] + "/pin", admin.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/feed/" + ids[1] + "/pin", admin.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    JsonElement first = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/feed?maxResults=2", reader.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.Equal(2, ContentTestKit.Ids(first).Count, "Pins count toward the page cap");
                    Check.Equal(5L, first.GetProperty("totalRecords").GetInt64(), "totalRecords counts pinned posts");
                    JsonElement second = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/feed?maxResults=2&continuationToken=" + Uri.EscapeDataString(Api.Str(first, "continuationToken")), reader.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.Equal(5L, second.GetProperty("totalRecords").GetInt64(), "Same total on later pages");
                    JsonElement plain = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/feed/enumerate", reader.AccessToken, new { ordering = "PublishedDescending" }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.Equal(5L, plain.GetProperty("totalRecords").GetInt64(), "Other orderings agree");
                }),
                Case("Regression.Feed.InvertedPublishedWindow", "publishedAfterUtc later than publishedBeforeUtc is 400 ValidationFailed on the member, public, and admin feeds", async (f, ct) =>
                {
                    TestMember reader = await ContentTestKit.MemberAsync(f, ct).ConfigureAwait(false);
                    object body = new { filters = new Dictionary<string, string> { { "publishedAfterUtc", "2026-06-01T00:00:00Z" }, { "publishedBeforeUtc", "2026-01-01T00:00:00Z" } } };
                    ApiResult member = await Api.SendAsync(f, HttpMethod.Post, "/api/v1/feed/enumerate", reader.AccessToken, body, ct).ConfigureAwait(false);
                    Check.Equal(HttpStatusCode.BadRequest, member.Status, "Member feed status (body: " + member.Text + ")");
                    Check.Equal("ValidationFailed", member.Error, "Member feed error");
                    Check.True(member.Body.GetProperty("fieldErrors").TryGetProperty("filters.publishedBeforeUtc", out JsonElement _), "Field named");
                    ApiResult admin = await Api.SendAsync(f, HttpMethod.Post, "/api/v1/admin/feed/enumerate", HttpFixture.AdminToken, body, ct).ConfigureAwait(false);
                    Check.Equal(HttpStatusCode.BadRequest, admin.Status, "Admin feed status (body: " + admin.Text + ")");
                    await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/feed/enumerate", reader.AccessToken,
                        new { filters = new Dictionary<string, string> { { "publishedAfterUtc", "2026-01-01T00:00:00Z" }, { "publishedBeforeUtc", "2026-06-01T00:00:00Z" } } }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                }),
                Case("Feed.RateLimits", "feed.postsPerHour and rateLimits.memberReadsPerMinute return 429 with Retry-After", RateLimitsAsync)
            });
        }

        #endregion

        #region Private-Methods

        private static TestCaseDescriptor Case(string caseId, string name, Func<HttpFixture, CancellationToken, Task> body)
        {
            return SuiteCases.Http(Id, caseId, name, body);
        }

        private static async Task ShapeAsync(HttpFixture f, CancellationToken ct)
        {
            TestMember poster = await ContentTestKit.PosterAsync(f, ct).ConfigureAwait(false);
            TestMember reader = await ContentTestKit.MemberAsync(f, ct).ConfigureAwait(false);
            JsonElement post = await ContentTestKit.PostAsync(f, poster.AccessToken, new { title = "  Welcome  ", body = "Hello\r\n\r\n\r\n\r\nworld \u200B", labels = new[] { "intro" }, tags = new Dictionary<string, string> { { "level", "all" } } }, ct).ConfigureAwait(false);
            string[] fields = { "id", "kind", "author", "title", "body", "excerpt", "mentions", "attachments", "linkPreview", "visibility", "audience", "state", "commentsEnabled", "isPinned", "pinnedUtc",
                "publishAtUtc", "publishedUtc", "createdUtc", "updatedUtc", "editedUtc", "revisionCount", "commentCount", "reactionCounts", "reactionTotal", "myReactions", "commentTarget", "source",
                "moderation", "viewer", "labels", "tags" };
            foreach (string field in fields) Check.True(post.TryGetProperty(field, out JsonElement _), "Field " + field);
            Check.Equal("Original", Api.Str(post, "kind"), "kind");
            Check.Equal("Welcome", Api.Str(post, "title"), "Title trimmed");
            Check.Equal("Hello\n\nworld", Api.Str(post, "body"), "Body normalized");
            Check.Equal("Published", Api.Str(post, "state"), "Published now");
            Check.Equal("Members", Api.Str(post, "visibility"), "Members by default");
            Check.Equal("Members", Api.Str(post, "audience"), "Audience follows");
            Check.Equal(JsonValueKind.Null, post.GetProperty("moderation").ValueKind, "No moderation");
            Check.Equal("FeedPost", Api.Str(post.GetProperty("commentTarget"), "type"), "commentTarget type");
            Check.Equal("intro", post.GetProperty("labels")[0].GetString(), "Labels applied");
            Check.Equal("all", post.GetProperty("tags").GetProperty("level").GetString(), "Tags applied");
            Check.True(post.GetProperty("viewer").GetProperty("canEdit").GetBoolean() && post.GetProperty("viewer").GetProperty("canDelete").GetBoolean(), "Author can edit and delete");
            Check.True(!post.GetProperty("viewer").GetProperty("canReport").GetBoolean() && !post.GetProperty("viewer").GetProperty("canPin").GetBoolean(), "Author cannot report or pin");
            JsonElement read = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/feed/" + Api.Str(post, "id"), reader.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            JsonElement viewer = read.GetProperty("viewer");
            Check.True(!viewer.GetProperty("canEdit").GetBoolean() && !viewer.GetProperty("canDelete").GetBoolean(), "Reader cannot edit or delete");
            Check.True(viewer.GetProperty("canComment").GetBoolean() && viewer.GetProperty("canReact").GetBoolean() && viewer.GetProperty("canReport").GetBoolean(), "Reader can comment, react, report");
            Check.True(viewer.GetProperty("canAttachMedia").GetBoolean() && viewer.GetProperty("canAttachVideo").GetBoolean() && !viewer.GetProperty("canModerate").GetBoolean(), "Attach flags, not a moderator");
            Check.Equal(poster.Id, Api.Str(read.GetProperty("author"), "id"), "Author summary");
            JsonElement bare = await ContentTestKit.PostAsync(f, poster.AccessToken, new { body = "No metadata" }, ct).ConfigureAwait(false);
            Check.Equal(0, bare.GetProperty("labels").GetArrayLength(), "labels []");
            Check.Equal(0, bare.GetProperty("tags").EnumerateObject().Count(), "tags {}");
            JsonElement page = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/feed", reader.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            foreach (JsonElement item in page.GetProperty("objects").EnumerateArray()) ContentTestKit.CheckMetadata(item, "feed item");
            await Api.ExpectErrorAsync(f, HttpMethod.Get, "/api/v1/feed/fpo_unknown", reader.AccessToken, null, HttpStatusCode.NotFound, "NotFound", ct).ConfigureAwait(false);
        }

        private static async Task PinnedAsync(HttpFixture f, CancellationToken ct)
        {
            TestMember admin = await ContentTestKit.AdminAsync(f, ct).ConfigureAwait(false);
            TestMember reader = await ContentTestKit.MemberAsync(f, ct).ConfigureAwait(false);
            List<string> ids = new List<string>();
            for (int i = 0; i < 4; i++) ids.Add(await ContentTestKit.PostIdAsync(f, admin.AccessToken, "Post " + i, ct).ConfigureAwait(false));
            await Api.ExpectErrorAsync(f, HttpMethod.Post, "/api/v1/feed/" + ids[0] + "/pin", reader.AccessToken, null, HttpStatusCode.Forbidden, "Forbidden", ct).ConfigureAwait(false);
            JsonElement pinned = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/feed/" + ids[0] + "/pin", admin.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.True(pinned.GetProperty("isPinned").GetBoolean(), "Pinned");
            await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/feed/" + ids[0] + "/pin", admin.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            JsonElement first = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/feed?maxResults=2", reader.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            List<string> firstIds = ContentTestKit.Ids(first);
            Check.True(firstIds.SequenceEqual(new[] { ids[0], ids[3] }), "Pinned first within the two-object cap: " + String.Join(",", firstIds));
            JsonElement second = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/feed?maxResults=2&continuationToken=" + Uri.EscapeDataString(Api.Str(first, "continuationToken")), reader.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.True(ContentTestKit.Ids(second).SequenceEqual(new[] { ids[2], ids[1] }), "Second page continues the same keyset");
            Check.True(second.GetProperty("endOfResults").GetBoolean(), "End");
            JsonElement plain = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/feed/enumerate", reader.AccessToken, new { ordering = "PublishedDescending" }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.True(ContentTestKit.Ids(plain).SequenceEqual(new[] { ids[3], ids[2], ids[1], ids[0] }), "PublishedDescending has no pinned prefix");
            f.Settings.Feed.MaxPinned = 1;
            await Api.ExpectErrorAsync(f, HttpMethod.Post, "/api/v1/feed/" + ids[1] + "/pin", admin.AccessToken, null, (HttpStatusCode)409, "PinLimitReached", ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Post, "/api/v1/feed", admin.AccessToken, new { body = "pin me", pin = true }, (HttpStatusCode)409, "PinLimitReached", ct).ConfigureAwait(false);
            for (int i = 0; i < 2; i++)
            {
                JsonElement unpinned = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/feed/" + ids[0] + "/unpin", admin.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                Check.True(!unpinned.GetProperty("isPinned").GetBoolean(), "Unpinned (pass " + i + ")");
            }
            JsonElement created = await ContentTestKit.PostAsync(f, admin.AccessToken, new { body = "pinned on create", pin = true }, ct).ConfigureAwait(false);
            Check.True(created.GetProperty("isPinned").GetBoolean(), "Pin on create");
            await Api.ExpectErrorAsync(f, HttpMethod.Post, "/api/v1/feed/" + ids[1] + "/pin", admin.AccessToken, null, (HttpStatusCode)409, "PinLimitReached", ct).ConfigureAwait(false);
            Check.True(await ContentTestKit.ScalarAsync(f, "SELECT COUNT(*) AS cnt FROM auditrecords WHERE action = 'feedPost.unpin';", ct).ConfigureAwait(false) >= 1, "Unpin audited");
            Check.True(await ContentTestKit.ScalarAsync(f, "SELECT COUNT(*) AS cnt FROM auditrecords WHERE action = 'feedPost.pin';", ct).ConfigureAwait(false) >= 1, "Pin audited");
        }

        private static async Task ContinuationAsync(HttpFixture f, CancellationToken ct)
        {
            TestMember poster = await ContentTestKit.PosterAsync(f, ct).ConfigureAwait(false);
            HashSet<string> original = new HashSet<string>();
            for (int i = 0; i < 7; i++) original.Add(await ContentTestKit.PostIdAsync(f, poster.AccessToken, "Original " + i, ct).ConfigureAwait(false));
            List<string> seen = new List<string>();
            string? next = null;
            int pages = 0;
            do
            {
                string path = "/api/v1/feed?maxResults=2" + (next == null ? String.Empty : "&continuationToken=" + Uri.EscapeDataString(next));
                JsonElement page = await Api.ExpectAsync(f, HttpMethod.Get, path, poster.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                seen.AddRange(ContentTestKit.Ids(page));
                next = page.GetProperty("continuationToken").ValueKind == JsonValueKind.String ? page.GetProperty("continuationToken").GetString() : null;
                await Task.WhenAll(ContentTestKit.PostIdAsync(f, poster.AccessToken, "Concurrent A" + pages, ct), ContentTestKit.PostIdAsync(f, poster.AccessToken, "Concurrent B" + pages, ct)).ConfigureAwait(false);
                pages++;
            }
            while (next != null && pages < 20);
            Check.Equal(seen.Count, seen.Distinct().Count(), "No duplicates");
            Check.True(original.All(seen.Contains), "No gaps");
            await Api.ExpectErrorAsync(f, HttpMethod.Get, "/api/v1/feed?continuationToken=garbage", poster.AccessToken, null, HttpStatusCode.BadRequest, "InvalidContinuationToken", ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Get, "/api/v1/feed?ordering=Sideways", poster.AccessToken, null, HttpStatusCode.BadRequest, "ValidationFailed", ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Get, "/api/v1/feed?color=red", poster.AccessToken, null, HttpStatusCode.BadRequest, "ValidationFailed", ct).ConfigureAwait(false);
            JsonElement clamped = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/feed?maxResults=5000", poster.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal(50, clamped.GetProperty("maxResults").GetInt32(), "maxResults clamped to 50");
            JsonElement window = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/feed?publishedAfterUtc=" + Uri.EscapeDataString(DateTime.UtcNow.AddMinutes(5).ToString("o")), poster.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal(0, window.GetProperty("objects").GetArrayLength(), "publishedAfterUtc in the future selects nothing");
        }

        private static async Task MetadataAsync(HttpFixture f, CancellationToken ct)
        {
            TestMember admin = await ContentTestKit.AdminAsync(f, ct).ConfigureAwait(false);
            TestMember poster = await ContentTestKit.PosterAsync(f, ct).ConfigureAwait(false);
            string a = Api.Str(await ContentTestKit.PostAsync(f, poster.AccessToken, new { body = "A", labels = new[] { "topics", "basics" }, tags = new Dictionary<string, string> { { "level", "beginner" } } }, ct).ConfigureAwait(false), "id");
            string b = Api.Str(await ContentTestKit.PostAsync(f, poster.AccessToken, new { body = "B", labels = new[] { "topics" }, tags = new Dictionary<string, string> { { "level", "advanced" } } }, ct).ConfigureAwait(false), "id");
            string c = Api.Str(await ContentTestKit.PostAsync(f, poster.AccessToken, new { body = "C", labels = new[] { "basics" } }, ct).ConfigureAwait(false), "id");
            Check.True(Set(await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/feed?labels=topics&labels=basics", poster.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false)).SetEquals(new[] { a }), "labels All");
            Check.True(Set(await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/feed?labels=topics,basics&labelMatch=Any", poster.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false)).SetEquals(new[] { a, b, c }), "labels Any");
            Check.True(Set(await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/feed?tag.level=advanced", poster.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false)).SetEquals(new[] { b }), "tag value");
            Check.True(Set(await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/feed/enumerate", poster.AccessToken, new { tags = new Dictionary<string, string> { { "LEVEL", "*" } } }, HttpStatusCode.OK, ct).ConfigureAwait(false)).SetEquals(new[] { a, b }), "tag key present (*), key case-insensitive");
            Check.True(Set(await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/feed?search=b", poster.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false)).SetEquals(new[] { b }), "search over body");
            await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/metadata/FeedPost/" + a, admin.AccessToken, new { labels = new[] { "topics", "basics", "staff:featured" } }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            JsonElement asMember = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/feed/" + a, poster.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.True(!asMember.GetProperty("labels").EnumerateArray().Any(l => l.GetString()!.StartsWith("staff:", StringComparison.Ordinal)), "Staff label stripped for members");
            JsonElement asAdmin = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/feed/" + a, admin.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.True(asAdmin.GetProperty("labels").EnumerateArray().Any(l => l.GetString() == "staff:featured"), "Staff sees the staff label");
            await Api.ExpectErrorAsync(f, HttpMethod.Get, "/api/v1/feed?labels=staff:featured", poster.AccessToken, null, HttpStatusCode.BadRequest, "ValidationFailed", ct).ConfigureAwait(false);
            Check.True(Set(await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/feed?labels=staff:featured", admin.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false)).SetEquals(new[] { a }), "Staff may filter on staff labels");
            await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/feed/" + a, poster.AccessToken, new { labels = new[] { "renamed" } }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            ObjectMetadata stored = await f.Database.Metadata.ReadAsync(TargetTypeEnum.FeedPost, a, ct).ConfigureAwait(false);
            Check.True(stored.Labels.Contains("renamed") && stored.Labels.Contains("staff:featured") && !stored.Labels.Contains("topics"), "Member replace keeps staff entries: " + String.Join(",", stored.Labels));
            await Api.ExpectErrorAsync(f, HttpMethod.Post, "/api/v1/feed", poster.AccessToken, new { body = "bad", labels = new[] { "not<valid>" } }, HttpStatusCode.BadRequest, "ValidationFailed", ct).ConfigureAwait(false);
        }

        private static async Task BlocksAsync(HttpFixture f, CancellationToken ct)
        {
            TestMember poster = await ContentTestKit.PosterAsync(f, ct).ConfigureAwait(false);
            TestMember blocker = await ContentTestKit.MemberAsync(f, ct).ConfigureAwait(false);
            TestMember other = await ContentTestKit.MemberAsync(f, ct).ConfigureAwait(false);
            string id = await ContentTestKit.PostIdAsync(f, poster.AccessToken, "Visible to most", ct).ConfigureAwait(false);
            await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/users/" + poster.Id + "/block", blocker.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.True(!Set(await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/feed", blocker.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false)).Contains(id), "Omitted for the blocker");
            Check.True(Set(await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/feed", other.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false)).Contains(id), "Shown to others");
            JsonElement reverse = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/users/" + blocker.Id + "/block", poster.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.True(reverse.GetProperty("blocked").GetBoolean(), "Poster blocks back");
            JsonElement seen = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/feed/" + id, blocker.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.True(!seen.GetProperty("viewer").GetProperty("canComment").GetBoolean() && !seen.GetProperty("viewer").GetProperty("canReact").GetBoolean(), "Blocked by the author: no comment or react");
        }

        private static async Task SchedulingAsync(HttpFixture f, CancellationToken ct)
        {
            TestMember poster = await ContentTestKit.PosterAsync(f, ct).ConfigureAwait(false);
            TestMember mentioned = await ContentTestKit.MemberAsync(f, ct, "Mia Mentioned", "miamention").ConfigureAwait(false);
            JsonElement post = await ContentTestKit.PostAsync(f, poster.AccessToken, new { body = "Later @MiaMention", publishAtUtc = DateTime.UtcNow.AddMinutes(5) }, ct).ConfigureAwait(false);
            string id = Api.Str(post, "id");
            Check.Equal("Scheduled", Api.Str(post, "state"), "Scheduled");
            Check.Equal(JsonValueKind.Null, post.GetProperty("publishedUtc").ValueKind, "Not published");
            Check.True(!Set(await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/feed", mentioned.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false)).Contains(id), "Not in the feed");
            await Api.ExpectErrorAsync(f, HttpMethod.Get, "/api/v1/feed/" + id, mentioned.AccessToken, null, HttpStatusCode.NotFound, "NotFound", ct).ConfigureAwait(false);
            Check.True(Set(await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/me/feed-posts?state=Scheduled", poster.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false)).SetEquals(new[] { id }), "My scheduled posts");
            Check.Equal(0, Set(await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/me/feed-posts?state=Published", poster.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false)).Count, "Nothing published yet");
            Check.Equal(0, (await NotificationTestKit.ListAsync(f, mentioned.Id, ct).ConfigureAwait(false)).Count, "Mention not notified before publishing");
            Check.Equal(0, await ContentTestKit.PublishAsync(f, ct).ConfigureAwait(false), "Nothing due");
            await f.Raw.ExecuteQueryAsync("UPDATE feedposts SET publishatutc = " + NotificationTestKit.Ts(DateTime.UtcNow.AddSeconds(-5)) + " WHERE id = '" + id + "';", false, ct).ConfigureAwait(false);
            int[] passes = await Task.WhenAll(ContentTestKit.PublishAsync(f, ct), f.Services.Feed.PublishDueAsync(10, ct)).ConfigureAwait(false);
            Check.Equal(1, passes.Sum(), "Published exactly once");
            JsonElement published = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/feed/" + id, mentioned.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal("Published", Api.Str(published, "state"), "Published");
            Check.True(published.GetProperty("publishedUtc").ValueKind == JsonValueKind.String, "publishedUtc set");
            Check.True(await f.Database.NotificationFanouts.ReadAsync(NotificationTypeEnum.NewFeedPost, id, ct).ConfigureAwait(false) != null, "NewFeedPost fan-out started");
            Check.Equal(1L, await ContentTestKit.ScalarAsync(f, "SELECT COUNT(*) AS cnt FROM auditrecords WHERE action = 'feedPost.publish' AND targetid = '" + id + "';", ct).ConfigureAwait(false), "Publish audited once");
            List<Notification> notes = await NotificationTestKit.ListAsync(f, mentioned.Id, ct).ConfigureAwait(false);
            Check.Equal(1, notes.Count(n => n.Type == NotificationTypeEnum.Mention), "Mention notified on publish");
            Check.Equal(0, await ContentTestKit.PublishAsync(f, ct).ConfigureAwait(false), "Nothing left");
            Check.Equal(1, (await NotificationTestKit.ListAsync(f, mentioned.Id, ct).ConfigureAwait(false)).Count, "Still one notification");
            await Api.ExpectErrorAsync(f, HttpMethod.Put, "/api/v1/feed/" + id, poster.AccessToken, new { publishAtUtc = DateTime.UtcNow.AddDays(1) }, HttpStatusCode.BadRequest, "ValidationFailed", ct).ConfigureAwait(false);
            JsonElement later = await ContentTestKit.PostAsync(f, poster.AccessToken, new { body = "Another", publishAtUtc = DateTime.UtcNow.AddHours(1) }, ct).ConfigureAwait(false);
            JsonElement now = await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/feed/" + Api.Str(later, "id"), poster.AccessToken, "{\"publishAtUtc\":null}", HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal("Published", Api.Str(now, "state"), "publishAtUtc null publishes now");
        }

        private static async Task ProcessingAsync(HttpFixture f, CancellationToken ct)
        {
            TestMember poster = await ContentTestKit.PosterAsync(f, ct).ConfigureAwait(false);
            string media = await MediaTestKit.UploadSingleAsync(f, poster.AccessToken, "Image", "image/png", MediaTestKit.Png(32, 24), ct).ConfigureAwait(false);
            JsonElement post = await ContentTestKit.PostAsync(f, poster.AccessToken, new { body = "With a photo", mediaIds = new[] { media } }, ct).ConfigureAwait(false);
            string id = Api.Str(post, "id");
            Check.Equal("Processing", Api.Str(post, "state"), "Held while the media processes");
            Check.Equal(1, post.GetProperty("attachments").GetArrayLength(), "Attachment shown to the author");
            Check.True(!Set(await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/feed", poster.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false)).Contains(id), "Not in the feed yet");
            Check.Equal(0, await ContentTestKit.PublishAsync(f, ct).ConfigureAwait(false), "Not publishable while Processing");
            await MediaTestKit.ProcessAsync(f, ct).ConfigureAwait(false);
            MediaAttachment processed = (await f.Database.Media.ReadAsync(media, ct).ConfigureAwait(false))!;
            Check.Equal(MediaStatusEnum.Ready, processed.Status, "Media processed");
            Check.Equal(1, await ContentTestKit.PublishAsync(f, ct).ConfigureAwait(false), "Published after the media is Ready");
            JsonElement done = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/feed/" + id, poster.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal("Published", Api.Str(done, "state"), "Published");
            Check.Equal("Ready", Api.Str(done.GetProperty("attachments")[0], "status"), "Media Ready");
            FeedPost row = (await f.Database.FeedPosts.ReadAsync(id, ct).ConfigureAwait(false))!;
            Check.True(row.AttachmentCount == 1 && !row.HasVideo, "attachmentcount and hasvideo");
            MediaAttachment stored = (await f.Database.Media.ReadAsync(media, ct).ConfigureAwait(false))!;
            Check.True(row.PublishedUtc >= stored.ReadyUtc!.Value.AddMilliseconds(-1), "publishedUtc is not before the media became Ready");
            await Api.ExpectAsync(f, HttpMethod.Delete, "/api/v1/admin/media/" + media, HttpFixture.AdminToken, null, HttpStatusCode.NoContent, ct).ConfigureAwait(false);
            Check.Equal(0, (await f.Database.FeedPosts.ReadAsync(id, ct).ConfigureAwait(false))!.AttachmentCount, "An admin media delete recomputes attachmentcount");
        }

        private static async Task DeleteAsync(HttpFixture f, CancellationToken ct)
        {
            TestMember poster = await ContentTestKit.PosterAsync(f, ct).ConfigureAwait(false);
            TestMember other = await ContentTestKit.MemberAsync(f, ct).ConfigureAwait(false);
            TestMember admin = await ContentTestKit.AdminAsync(f, ct).ConfigureAwait(false);
            string id = Api.Str(await ContentTestKit.PostAsync(f, poster.AccessToken, new { body = "To delete", labels = new[] { "gone" } }, ct).ConfigureAwait(false), "id");
            await ContentTestKit.CommentAsync(f, other.AccessToken, id, "A comment", null, ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Delete, "/api/v1/feed/" + id, other.AccessToken, null, HttpStatusCode.Forbidden, "Forbidden", ct).ConfigureAwait(false);
            string scheduled = Api.Str(await ContentTestKit.PostAsync(f, poster.AccessToken, new { body = "Later", publishAtUtc = DateTime.UtcNow.AddHours(2) }, ct).ConfigureAwait(false), "id");
            await Api.ExpectErrorAsync(f, HttpMethod.Delete, "/api/v1/feed/" + scheduled, other.AccessToken, null, HttpStatusCode.NotFound, "NotFound", ct).ConfigureAwait(false);
            await Api.ExpectAsync(f, HttpMethod.Delete, "/api/v1/feed/" + id, poster.AccessToken, null, HttpStatusCode.NoContent, ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Get, "/api/v1/feed/" + id, poster.AccessToken, null, HttpStatusCode.NotFound, "NotFound", ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Get, "/api/v1/feed/" + id + "/comments", other.AccessToken, null, HttpStatusCode.NotFound, "NotFound", ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Delete, "/api/v1/feed/" + id, poster.AccessToken, null, HttpStatusCode.NotFound, "NotFound", ct).ConfigureAwait(false);
            Check.True(!Set(await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/feed", other.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false)).Contains(id), "Gone from the feed");
            Check.Equal(0, (await f.Database.Metadata.ReadAsync(TargetTypeEnum.FeedPost, id, ct).ConfigureAwait(false)).Labels.Count, "Labels deleted with the post");
            FeedPost row = (await f.Database.FeedPosts.ReadAsync(id, ct).ConfigureAwait(false))!;
            Check.True(row.DeletedUtc != null && row.DeletedByUserId == poster.Id, "Soft deleted");
            await Api.ExpectAsync(f, HttpMethod.Delete, "/api/v1/feed/" + scheduled, admin.AccessToken, null, HttpStatusCode.NoContent, ct).ConfigureAwait(false);
            Check.True(await ContentTestKit.ScalarAsync(f, "SELECT COUNT(*) AS cnt FROM auditrecords WHERE action = 'feedPost.delete';", ct).ConfigureAwait(false) == 2, "Deletes audited");
        }

        private static async Task AudienceAsync(HttpFixture f, CancellationToken ct)
        {
            TestMember poster = await ContentTestKit.PosterAsync(f, ct).ConfigureAwait(false);
            TestMember admin = await ContentTestKit.AdminAsync(f, ct).ConfigureAwait(false);
            TestMember reader = await ContentTestKit.MemberAsync(f, ct, "Audience Reader").ConfigureAwait(false);
            TestMember lapsed = await ContentTestKit.MemberAsync(f, ct, "Lapsed Member").ConfigureAwait(false);
            string image = await ContentTestKit.MediaAsync(f, poster.Id, MediaKindEnum.Image, ct).ConfigureAwait(false);
            string members = Api.Str(await ContentTestKit.PostAsync(f, poster.AccessToken, new { body = "members only", mediaIds = new[] { image } }, ct).ConfigureAwait(false), "id");
            string signedIn = Api.Str(await ContentTestKit.PostAsync(f, poster.AccessToken, new { body = "signed in", audience = "SignedIn" }, ct).ConfigureAwait(false), "id");
            string publicPost = Api.Str(await ContentTestKit.PostAsync(f, admin.AccessToken, new { body = "public sample", audience = "Public" }, ct).ConfigureAwait(false), "id");

            Check.True(ContentTestKit.Ids(await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/feed?audience=SignedIn", reader.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false)).SequenceEqual(new[] { signedIn }), "Feed audience=SignedIn");
            Check.True(ContentTestKit.Ids(await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/feed/enumerate", reader.AccessToken, new { filters = new Dictionary<string, string> { { "audience", "Members" } } }, HttpStatusCode.OK, ct).ConfigureAwait(false)).SequenceEqual(new[] { members }), "Feed audience=Members (POST)");
            Check.True(ContentTestKit.Ids(await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/feed?audience=Public", reader.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false)).SequenceEqual(new[] { publicPost }), "Feed audience=Public");
            await Api.ExpectErrorAsync(f, HttpMethod.Get, "/api/v1/feed?audience=Everyone", reader.AccessToken, null, HttpStatusCode.BadRequest, "ValidationFailed", ct).ConfigureAwait(false);
            Check.True(ContentTestKit.Ids(await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/me/feed-posts?audience=SignedIn", poster.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false)).SequenceEqual(new[] { signedIn }), "My posts audience filter");
            Check.True(ContentTestKit.Ids(await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/public/feed?audience=Public", null, null, HttpStatusCode.OK, ct).ConfigureAwait(false)).SequenceEqual(new[] { publicPost }), "Public feed audience=Public");
            Check.Equal(0, ContentTestKit.Ids(await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/public/feed?audience=Members", null, null, HttpStatusCode.OK, ct).ConfigureAwait(false)).Count, "Public feed never lists other audiences");
            await Api.ExpectErrorAsync(f, HttpMethod.Get, "/api/v1/public/feed/" + signedIn, null, null, HttpStatusCode.NotFound, "NotFound", ct).ConfigureAwait(false);

            FeedPost membersRow = (await f.Database.FeedPosts.ReadAsync(members, ct).ConfigureAwait(false))!;
            FeedPost signedInRow = (await f.Database.FeedPosts.ReadAsync(signedIn, ct).ConfigureAwait(false))!;
            FeedPost publicRow = (await f.Database.FeedPosts.ReadAsync(publicPost, ct).ConfigureAwait(false))!;
            ContentActor nonMember = new ContentActor { UserId = "usr_signedin_only", AgeAttested = true, HasMembership = false };
            ContentActor unattested = new ContentActor { UserId = "usr_unattested", AgeAttested = false, HasMembership = false };
            DateTime now = DateTime.UtcNow;
            Check.True(!ContentAccessService.CanSeePost(membersRow, nonMember, now), "Members content needs a membership");
            Check.True(ContentAccessService.CanSeePost(signedInRow, nonMember, now), "SignedIn content needs only a signed-in, attested account");
            Check.True(!ContentAccessService.CanSeePost(signedInRow, unattested, now), "SignedIn content needs the age attestation");
            Check.True(ContentAccessService.CanSeePost(publicRow, unattested, now), "Public content is open");
            Check.True(ContentAccessService.CanSeePost(membersRow, new ContentActor { UserId = poster.Id }, now), "The author always sees their post");
            Check.True(ContentAccessService.CanSeePost(membersRow, new ContentActor { UserId = "usr_mod", IsFeedModerator = true }, now), "Feed moderators see what they moderate");
            Check.True(ContentAudienceRule.VisibleAudiences(nonMember).SequenceEqual(new[] { ContentAudienceEnum.Public, ContentAudienceEnum.SignedIn }), "Visible audiences of a signed-in non-member");
            Check.True(ContentAudienceRule.VisibleAudiences(new ContentActor { UserId = "usr_m", AgeAttested = true, HasMembership = true }).Count == 3, "Members meet every audience");
            Check.True(!ContentAudienceRule.RecipientMayOpen(ContentAudienceEnum.SignedIn, false, true, false), "Recipients need the member route gate (FullyGated) even for SignedIn content");
            Check.True(ContentAudienceRule.RecipientMayOpen(ContentAudienceEnum.Members, true, false, false), "SystemAdmin recipients may open anything");
            await Check.ThrowsAsync<ApiErrorException>(() => f.Services.ContentAccess.RequireVisiblePostAsync(nonMember, members, ct), "Detail, comments, reactions, reports, and revisions 404 below the audience");
            Check.True((await f.Services.ContentAccess.RequireVisiblePostAsync(nonMember, signedIn, ct).ConfigureAwait(false)).Id == signedIn, "The same lookup admits a met audience");
            MediaAttachment media = (await f.Database.Media.ReadAsync(image, ct).ConfigureAwait(false))!;
            Check.True(!await f.Services.ContentAccess.CanSeeAttachedMediaAsync(media, nonMember, ct).ConfigureAwait(false), "Attached media follows the post's audience");
            Check.True(await f.Services.ContentAccess.CanSeeAttachedMediaAsync(media, await f.Services.Actors.ForUserAsync(reader.Id, ct).ConfigureAwait(false), ct).ConfigureAwait(false), "A member sees the attached media");
            ContentActor readerActor = await f.Services.Actors.ForUserAsync(reader.Id, ct).ConfigureAwait(false);
            Check.True(readerActor.HasMembership && readerActor.AgeAttested, "Actors carry membership and attestation");

            string lapsedComment = await ContentTestKit.CommentIdAsync(f, lapsed.AccessToken, members, "before lapsing", null, ct).ConfigureAwait(false);
            await f.Raw.ExecuteQueryAsync("UPDATE entitlements SET revokedutc = " + NotificationTestKit.Ts(DateTime.UtcNow.AddMinutes(-1)) + " WHERE userid = '" + lapsed.Id + "';", false, ct).ConfigureAwait(false);
            Check.True(!(await f.Services.Actors.ForUserAsync(lapsed.Id, ct).ConfigureAwait(false)).HasMembership, "Membership revoked");
            await ContentTestKit.CommentIdAsync(f, reader.AccessToken, members, "a reply", lapsedComment, ct).ConfigureAwait(false);
            Check.Equal(0, (await NotificationTestKit.ListAsync(f, lapsed.Id, ct).ConfigureAwait(false)).Count(n => n.Type == NotificationTypeEnum.CommentReply), "No reply notification to a recipient who cannot open the post");
            await f.Raw.ExecuteQueryAsync("UPDATE entitlements SET revokedutc = " + NotificationTestKit.Ts(DateTime.UtcNow.AddMinutes(-1)) + " WHERE userid = '" + poster.Id + "';", false, ct).ConfigureAwait(false);
            await ContentTestKit.CommentIdAsync(f, reader.AccessToken, members, "to the author", null, ct).ConfigureAwait(false);
            Check.True((await NotificationTestKit.ListAsync(f, poster.Id, ct).ConfigureAwait(false)).Any(n => n.Type == NotificationTypeEnum.PostComment), "The post author is still notified (authors always see their posts)");
        }

        private static async Task MentionsAsync(HttpFixture f, CancellationToken ct)
        {
            TestMember poster = await ContentTestKit.PosterAsync(f, ct, "Paula Poster", "paulap").ConfigureAwait(false);
            TestMember alex = await ContentTestKit.MemberAsync(f, ct, "Alex Chen", "alexchen").ConfigureAwait(false);
            TestMember bea = await ContentTestKit.MemberAsync(f, ct, "Bea Blocker", "beablock").ConfigureAwait(false);
            TestMember cal = await ContentTestKit.MemberAsync(f, ct, "Cal Later", "callater").ConfigureAwait(false);
            await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/users/" + poster.Id + "/block", bea.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            JsonElement post = await ContentTestKit.PostAsync(f, poster.AccessToken, new { body = "Thanks @AlexChen and @beablock and @paulap and @nobody_here, mail me at me@alexchen.test" }, ct).ConfigureAwait(false);
            string id = Api.Str(post, "id");
            List<JsonElement> mentions = post.GetProperty("mentions").EnumerateArray().ToList();
            Check.Equal(3, mentions.Count, "Three resolved mentions (self included, unknown and email skipped)");
            Check.Equal("AlexChen", Api.Str(mentions[0], "handle"), "Handle as written");
            Check.Equal("Alex Chen", Api.Str(mentions[0], "displayName"), "Display name");
            Check.Equal(alex.Id, Api.Str(mentions[0], "userId"), "User id");
            Check.Equal(1, (await NotificationTestKit.ListAsync(f, alex.Id, ct).ConfigureAwait(false)).Count(n => n.Type == NotificationTypeEnum.Mention), "Alex notified");
            Check.Equal(0, (await NotificationTestKit.ListAsync(f, bea.Id, ct).ConfigureAwait(false)).Count, "Blocked member not notified");
            Check.Equal(0, (await NotificationTestKit.ListAsync(f, poster.Id, ct).ConfigureAwait(false)).Count(n => n.Type == NotificationTypeEnum.Mention), "No self notification");
            f.Settings.Content.EditGraceSeconds = 0;
            await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/feed/" + id, poster.AccessToken, new { body = "Thanks @AlexChen and @callater" }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal(1, (await NotificationTestKit.ListAsync(f, alex.Id, ct).ConfigureAwait(false)).Count, "Alex not notified again");
            Check.Equal(1, (await NotificationTestKit.ListAsync(f, cal.Id, ct).ConfigureAwait(false)).Count, "Newly added member notified");
            f.Settings.Mentions.MaxNotifiedPerItem = 1;
            JsonElement capped = await ContentTestKit.PostAsync(f, poster.AccessToken, new { body = "@callater @alexchen" }, ct).ConfigureAwait(false);
            Check.Equal(1, capped.GetProperty("mentions").GetArrayLength(), "Capped at mentions.maxNotifiedPerItem");
            Check.Equal(cal.Id, Api.Str(capped.GetProperty("mentions")[0], "userId"), "First mention kept");
        }

        private static async Task LinkPreviewAsync(HttpFixture f, CancellationToken ct)
        {
            TestMember poster = await ContentTestKit.PosterAsync(f, ct).ConfigureAwait(false);
            f.Fakes.LinkPreviews.Page("https://example.test/about", "<html><head><meta property=\"og:title\" content=\"About us\"><meta property=\"og:description\" content=\"Who we are.\"></head></html>");
            JsonElement shown = await ContentTestKit.PostAsync(f, poster.AccessToken, new { body = "Read https://example.test/about please" }, ct).ConfigureAwait(false);
            Check.True(shown.GetProperty("linkPreview").ValueKind == JsonValueKind.Object, "Preview card (" + shown.GetProperty("linkPreview") + ")");
            Check.Equal("About us", Api.Str(shown.GetProperty("linkPreview"), "title"), "Preview title");
            JsonElement hidden = await ContentTestKit.PostAsync(f, poster.AccessToken, new { body = "Read https://example.test/about please", showLinkPreview = false }, ct).ConfigureAwait(false);
            Check.Equal(JsonValueKind.Null, hidden.GetProperty("linkPreview").ValueKind, "showLinkPreview false");
            JsonElement off = await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/feed/" + Api.Str(shown, "id"), poster.AccessToken, new { showLinkPreview = false }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal(JsonValueKind.Null, off.GetProperty("linkPreview").ValueKind, "Turned off by an edit");
        }

        private static async Task WordFilterAsync(HttpFixture f, CancellationToken ct)
        {
            TestMember poster = await ContentTestKit.PosterAsync(f, ct).ConfigureAwait(false);
            await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/admin/moderation/word-filter", HttpFixture.AdminToken, new { term = "blockedword", matchType = "Word", action = "Block" }, HttpStatusCode.Created, ct).ConfigureAwait(false);
            await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/admin/moderation/word-filter", HttpFixture.AdminToken, new { term = "flaggedword", matchType = "Word", action = "Flag" }, HttpStatusCode.Created, ct).ConfigureAwait(false);
            await ExpectFiltered(f, poster, "/api/v1/feed", new { body = "this has blockedword in it" }, "body", ct).ConfigureAwait(false);
            await ExpectFiltered(f, poster, "/api/v1/feed", new { title = "blockedword", body = "fine" }, "title", ct).ConfigureAwait(false);
            string media = await ContentTestKit.MediaAsync(f, poster.Id, MediaKindEnum.Image, ct, MediaStatusEnum.Ready, "a blockedword photo").ConfigureAwait(false);
            await ExpectFiltered(f, poster, "/api/v1/feed", new { body = "fine", mediaIds = new[] { media } }, "mediaIds[0].altText", ct).ConfigureAwait(false);
            string id = await ContentTestKit.PostIdAsync(f, poster.AccessToken, "a flaggedword post", ct).ConfigureAwait(false);
            Check.Equal(1L, await ContentTestKit.ScalarAsync(f, "SELECT COUNT(*) AS cnt FROM reports WHERE reporteruserid IS NULL AND targetid = '" + id + "';", ct).ConfigureAwait(false), "One system report for a flagged post");
            TestMember admin = await Api.AdminAsync(f, ct).ConfigureAwait(false);
            string flaggedAgain = await ContentTestKit.PostIdAsync(f, poster.AccessToken, "another flaggedword post", ct).ConfigureAwait(false);
            string expectedPath = "/admin/moderation?target=FeedPost%3A" + flaggedAgain;
            Check.True(await ContentTestKit.ScalarAsync(f, "SELECT COUNT(*) AS cnt FROM notifications WHERE type = 'ContentFlagged' AND targettype = 'FeedPost' AND targetid = '" + flaggedAgain + "' AND path = '" + expectedPath + "';", ct).ConfigureAwait(false) >= 1L,
                "A flagged post alerts SystemAdmins with a link to the post in the moderation queue");
            List<Notification> alerts = (await NotificationTestKit.ListAsync(f, admin.Id, ct).ConfigureAwait(false)).Where(n => n.Type == NotificationTypeEnum.ContentFlagged && n.TargetId == flaggedAgain).ToList();
            if (alerts.Count > 0)
            {
                Check.Equal(expectedPath, alerts[0].Path, "Alert path");
                Check.True((alerts[0].Body ?? String.Empty).Contains("flaggedword", StringComparison.Ordinal), "The alert shows the flagged text");
            }
            await ContentTestKit.CommentIdAsync(f, poster.AccessToken, flaggedAgain, "flaggedword reply", null, ct).ConfigureAwait(false);
            Check.True(await ContentTestKit.ScalarAsync(f, "SELECT COUNT(*) AS cnt FROM notifications WHERE type = 'ContentFlagged' AND targettype = 'Comment' AND path LIKE '/admin/moderation?target=Comment%%';", ct).ConfigureAwait(false) >= 1L,
                "A flagged comment alerts SystemAdmins too");
            await ExpectFiltered(f, poster, "/api/v1/feed/" + id + "/comments", new { body = "blockedword" }, "body", ct).ConfigureAwait(false);
            await ExpectFiltered(f, poster, "/api/v1/feed/" + id + "/comments", new { body = "ok", labels = new[] { "blockedword" } }, "labels[0]", ct).ConfigureAwait(false);
            ApiResult edit = await Api.SendAsync(f, HttpMethod.Put, "/api/v1/feed/" + id, poster.AccessToken, new { body = "now blockedword" }, ct).ConfigureAwait(false);
            Check.Equal("ContentFiltered", edit.Error, "Edits are filtered too");
        }

        private static async Task WordFilterStarterAsync(HttpFixture f, CancellationToken ct)
        {
            int expected = WordFilterStarterList.All().Count;
            ApiResult first = await Api.SendAsync(f, HttpMethod.Post, "/api/v1/admin/moderation/word-filter/starter", HttpFixture.AdminToken, new { }, ct).ConfigureAwait(false);
            Check.Equal(HttpStatusCode.OK, first.Status, "Starter load (" + first.Text + ")");
            int added = first.Body.GetProperty("added").GetInt32();
            int skipped = first.Body.GetProperty("skipped").GetInt32();
            Check.Equal(expected, added + skipped, "Every starter term is added or skipped");
            ApiResult second = await Api.SendAsync(f, HttpMethod.Post, "/api/v1/admin/moderation/word-filter/starter", HttpFixture.AdminToken, new { }, ct).ConfigureAwait(false);
            Check.Equal(0, second.Body.GetProperty("added").GetInt32(), "A second load adds nothing");
            Check.Equal(0L, await ContentTestKit.ScalarAsync(f, "SELECT COUNT(*) AS cnt FROM wordfilterterms WHERE normalizedterm IN ('fuck', 'bitch') AND action <> 'Flag';", ct).ConfigureAwait(false), "Starter terms are Flag terms");
            TestMember member = await ContentTestKit.MemberAsync(f, ct, "Plain").ConfigureAwait(false);
            ApiResult plain = await Api.SendAsync(f, HttpMethod.Post, "/api/v1/admin/moderation/word-filter/starter", member.AccessToken, new { }, ct).ConfigureAwait(false);
            Check.Equal(HttpStatusCode.Forbidden, plain.Status, "Members cannot load the starter list");

            // Leave the shared list as other cases expect it: remove the starter terms, then add and delete a term so
            // the server drops its compiled cache.
            string list = String.Join(", ", WordFilterStarterList.All().Select(t => "'" + t + "'"));
            await ContentTestKit.ScalarAsync(f, "WITH gone AS (DELETE FROM wordfilterterms WHERE term IN (" + list + ") RETURNING 1) SELECT COUNT(*) AS cnt FROM gone;", ct).ConfigureAwait(false);
            ApiResult marker = await Api.SendAsync(f, HttpMethod.Post, "/api/v1/admin/moderation/word-filter", HttpFixture.AdminToken, new { term = "starterresetmarker", action = "Flag" }, ct).ConfigureAwait(false);
            await Api.SendAsync(f, HttpMethod.Delete, "/api/v1/admin/moderation/word-filter/" + Api.Str(marker.Body, "id"), HttpFixture.AdminToken, null, ct).ConfigureAwait(false);
        }

        private static async Task ExpectFiltered(HttpFixture f, TestMember member, string path, object body, string field, CancellationToken ct)
        {
            ApiResult result = await Api.SendAsync(f, HttpMethod.Post, path, member.AccessToken, body, ct).ConfigureAwait(false);
            Check.Equal(HttpStatusCode.BadRequest, result.Status, path + " filtered (" + result.Text + ")");
            Check.Equal("ContentFiltered", result.Error, "ContentFiltered");
            Check.True(result.Body.GetProperty("fieldErrors").TryGetProperty(field, out JsonElement _), "fieldErrors." + field + " (" + result.Text + ")");
        }

        private static async Task ValidationAsync(HttpFixture f, CancellationToken ct)
        {
            TestMember poster = await ContentTestKit.PosterAsync(f, ct).ConfigureAwait(false);
            await ExpectField(f, poster, new { body = "x", title = new string('t', 141) }, "title", ct).ConfigureAwait(false);
            await ExpectField(f, poster, new { body = "" }, "body", ct).ConfigureAwait(false);
            await ExpectField(f, poster, new { body = new string('b', f.Settings.Feed.MaxBodyLength + 1) }, "body", ct).ConfigureAwait(false);
            await ExpectField(f, poster, new { body = "x", publishAtUtc = DateTime.UtcNow.AddSeconds(20) }, "publishAtUtc", ct).ConfigureAwait(false);
            await ExpectField(f, poster, new { body = "x", publishAtUtc = DateTime.UtcNow.AddDays(400) }, "publishAtUtc", ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Post, "/api/v1/feed", poster.AccessToken, new { body = "x", visibility = "Public" }, HttpStatusCode.Forbidden, "Forbidden", ct).ConfigureAwait(false);
            ApiResult lone = await Api.SendAsync(f, HttpMethod.Post, "/api/v1/feed", poster.AccessToken, "{\"body\":\"\\ud800\"}", ct).ConfigureAwait(false);
            Check.Equal(HttpStatusCode.BadRequest, lone.Status, "Text that is not valid UTF-8 is rejected");
            string media = await ContentTestKit.MediaAsync(f, poster.Id, MediaKindEnum.Image, ct).ConfigureAwait(false);
            JsonElement onlyMedia = await ContentTestKit.PostAsync(f, poster.AccessToken, new { body = "", mediaIds = new[] { media } }, ct).ConfigureAwait(false);
            Check.Equal(String.Empty, Api.Str(onlyMedia, "body"), "Empty body allowed with an attachment");
            string v1 = await ContentTestKit.MediaAsync(f, poster.Id, MediaKindEnum.Video, ct).ConfigureAwait(false);
            string v2 = await ContentTestKit.MediaAsync(f, poster.Id, MediaKindEnum.Video, ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Post, "/api/v1/feed", poster.AccessToken, new { body = "two videos", mediaIds = new[] { v1, v2 } }, HttpStatusCode.BadRequest, "MediaLimitExceeded", ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Post, "/api/v1/feed", poster.AccessToken, new { body = "reuse", mediaIds = new[] { media } }, (HttpStatusCode)409, "MediaNotAttachable", ct).ConfigureAwait(false);
        }

        private static async Task RateLimitsAsync(HttpFixture f, CancellationToken ct)
        {
            TestMember poster = await ContentTestKit.PosterAsync(f, ct).ConfigureAwait(false);
            f.Settings.Feed.PostsPerHour = 2;
            await ContentTestKit.PostIdAsync(f, poster.AccessToken, "one", ct).ConfigureAwait(false);
            await ContentTestKit.PostIdAsync(f, poster.AccessToken, "two", ct).ConfigureAwait(false);
            ApiResult limited = await Api.SendAsync(f, HttpMethod.Post, "/api/v1/feed", poster.AccessToken, new { body = "three" }, ct).ConfigureAwait(false);
            Check.Equal((HttpStatusCode)429, limited.Status, "Posts per hour");
            Check.True(!String.IsNullOrEmpty(limited.RetryAfter), "Retry-After");
            TestMember reader = await ContentTestKit.MemberAsync(f, ct).ConfigureAwait(false);
            f.Settings.RateLimits.MemberReadsPerMinute = 10;
            for (int i = 0; i < 10; i++) await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/feed", reader.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            ApiResult reads = await Api.SendAsync(f, HttpMethod.Get, "/api/v1/feed", reader.AccessToken, null, ct).ConfigureAwait(false);
            Check.Equal((HttpStatusCode)429, reads.Status, "Member reads per minute");
            Check.True(!String.IsNullOrEmpty(reads.RetryAfter), "Retry-After on reads");
        }

        private static async Task ExpectField(HttpFixture f, TestMember poster, object body, string field, CancellationToken ct)
        {
            ApiResult result = await Api.SendAsync(f, HttpMethod.Post, "/api/v1/feed", poster.AccessToken, body, ct).ConfigureAwait(false);
            Check.Equal(HttpStatusCode.BadRequest, result.Status, "400 for " + field + " (" + result.Text + ")");
            Check.True(result.Body.GetProperty("fieldErrors").TryGetProperty(field, out JsonElement _), "fieldErrors." + field + " (" + result.Text + ")");
        }

        private static HashSet<string> Set(JsonElement page)
        {
            return new HashSet<string>(ContentTestKit.Ids(page));
        }

        #endregion
    }
}
