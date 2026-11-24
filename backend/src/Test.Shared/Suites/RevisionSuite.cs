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
    /// Phase 2 edit history (contract sections 5.5 and 7.6): silent edits in the grace window, unlimited edits with
    /// revisions and attachment summaries, and history visibility following the content.
    /// </summary>
    public static class RevisionSuite
    {
        #region Public-Members

        /// <summary>
        /// Suite identifier.
        /// </summary>
        public const string Id = "Revisions";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the suite.
        /// </summary>
        /// <returns>Suite.</returns>
        public static TestSuiteDescriptor Build()
        {
            return new TestSuiteDescriptor(Id, "Edit history (" + TestEnvironment.Provider + ")", new[]
            {
                Case("Revisions.Grace", "Edits within content.editGraceSeconds are silent: no revision and no editedUtc", GraceAsync),
                Case("Revisions.Unlimited", "Every later edit of title, body, or attachments stores a numbered revision with editedBy and an attachment summary; metadata-only edits store none", UnlimitedAsync),
                Case("Revisions.Visibility", "History is visible to everyone who can see the content and follows Hidden posts and deleted comments", VisibilityAsync),
                Case("Revisions.Comments", "Comment edits after the grace window store revisions with the previous body", CommentsAsync),
                Case("Revisions.Metadata", "Revisions keep the labels and tags of their version, filter on them, strip staff: entries for members, are read-only through the metadata routes, and lose them on purge", MetadataAsync),
                SuiteCases.Unit(Id, "Revisions.SummaryText", "attachmentSummary describes removed and added images and videos, or a reorder", SummaryAsync)
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

        private static async Task GraceAsync(HttpFixture f, CancellationToken ct)
        {
            TestMember poster = await ContentTestKit.PosterAsync(f, ct).ConfigureAwait(false);
            string id = await ContentTestKit.PostIdAsync(f, poster.AccessToken, "Typo hree", ct).ConfigureAwait(false);
            JsonElement edited = await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/feed/" + id, poster.AccessToken, new { body = "Typo here" }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal("Typo here", Api.Str(edited, "body"), "Edited");
            Check.Equal(JsonValueKind.Null, edited.GetProperty("editedUtc").ValueKind, "No editedUtc in the grace window");
            Check.Equal(0, edited.GetProperty("revisionCount").GetInt32(), "No revision");
            JsonElement none = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/feed/" + id + "/revisions", poster.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal(0, none.GetProperty("objects").GetArrayLength(), "Empty history");
            string scheduled = Api.Str(await ContentTestKit.PostAsync(f, poster.AccessToken, new { body = "Draft", publishAtUtc = DateTime.UtcNow.AddHours(1) }, ct).ConfigureAwait(false), "id");
            f.Settings.Content.EditGraceSeconds = 0;
            JsonElement draft = await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/feed/" + scheduled, poster.AccessToken, new { body = "Draft two" }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal(0, draft.GetProperty("revisionCount").GetInt32(), "Edits before publishing are silent");
        }

        private static async Task UnlimitedAsync(HttpFixture f, CancellationToken ct)
        {
            TestMember poster = await ContentTestKit.PosterAsync(f, ct, "Rita Reviser").ConfigureAwait(false);
            TestMember admin = await ContentTestKit.AdminAsync(f, ct).ConfigureAwait(false);
            f.Settings.Content.EditGraceSeconds = 0;
            string image = await ContentTestKit.MediaAsync(f, poster.Id, MediaKindEnum.Image, ct).ConfigureAwait(false);
            string video = await ContentTestKit.MediaAsync(f, poster.Id, MediaKindEnum.Video, ct).ConfigureAwait(false);
            string id = Api.Str(await ContentTestKit.PostAsync(f, poster.AccessToken, new { title = "v1", body = "Version one", mediaIds = new[] { image } }, ct).ConfigureAwait(false), "id");
            await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/feed/" + id, poster.AccessToken, new { body = "Version two" }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/feed/" + id, poster.AccessToken, new { mediaIds = new[] { video } }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/feed/" + id, poster.AccessToken, new { labels = new[] { "only-metadata" } }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            for (int i = 3; i <= 6; i++) await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/feed/" + id, poster.AccessToken, new { title = "v" + i }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            JsonElement last = await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/feed/" + id, admin.AccessToken, new { body = "Admin fix" }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal(7, last.GetProperty("revisionCount").GetInt32(), "Seven revisions (the metadata-only edit stored none)");
            Check.True(last.GetProperty("editedUtc").ValueKind == JsonValueKind.String, "editedUtc set");
            JsonElement history = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/feed/" + id + "/revisions", poster.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            List<JsonElement> items = history.GetProperty("objects").EnumerateArray().ToList();
            Check.True(items.Select(r => r.GetProperty("revision").GetInt32()).SequenceEqual(new[] { 7, 6, 5, 4, 3, 2, 1 }), "Newest first, numbered from 1");
            JsonElement first = items[items.Count - 1];
            Check.Equal("v1", Api.Str(first, "title"), "Revision 1 keeps the first title");
            Check.Equal("Version one", Api.Str(first, "body"), "Revision 1 keeps the first body");
            Check.Equal(JsonValueKind.Null, first.GetProperty("attachmentSummary").ValueKind, "No attachment change in revision 1");
            Check.Equal("Removed 1 image, added 1 video", Api.Str(items[items.Count - 2], "attachmentSummary"), "Attachment summary");
            Check.Equal(poster.Id, Api.Str(first.GetProperty("editedBy"), "id"), "editedBy");
            Check.Equal(admin.Id, Api.Str(items[0].GetProperty("editedBy"), "id"), "Admin edit recorded");
            ContentTestKit.CheckMetadata(first, "revision");
            Check.Equal(0, first.GetProperty("labels").GetArrayLength(), "Revisions carry []");
            JsonElement paged = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/feed/" + id + "/revisions/enumerate", poster.AccessToken, new { maxResults = 3 }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal(3, paged.GetProperty("objects").GetArrayLength(), "Paged");
            JsonElement labeled = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/feed/" + id + "/revisions?labels=only-metadata", poster.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.True(labeled.GetProperty("objects").EnumerateArray().Select(r => r.GetProperty("revision").GetInt32()).SequenceEqual(new[] { 7, 6, 5, 4, 3 }),
                "Revisions stored after the label was added carry it and filter on it");
            JsonElement none = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/feed/" + id + "/revisions?labels=x", poster.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal(0, none.GetProperty("objects").GetArrayLength(), "An unknown label matches no revision");
            FeedPost row = (await f.Database.FeedPosts.ReadAsync(id, ct).ConfigureAwait(false))!;
            Check.True(row.AttachmentCount == 1 && row.HasVideo, "attachmentcount and hasvideo follow the edit");
            Check.True(!(await f.Database.Media.ReadAsync(image, ct).ConfigureAwait(false))!.IsAttached, "Removed media detached");
        }

        private static async Task VisibilityAsync(HttpFixture f, CancellationToken ct)
        {
            TestMember poster = await ContentTestKit.PosterAsync(f, ct).ConfigureAwait(false);
            TestMember reader = await ContentTestKit.MemberAsync(f, ct).ConfigureAwait(false);
            f.Settings.Content.EditGraceSeconds = 0;
            string id = await ContentTestKit.PostIdAsync(f, poster.AccessToken, "Before", ct).ConfigureAwait(false);
            await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/feed/" + id, poster.AccessToken, new { body = "After" }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            JsonElement seen = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/feed/" + id + "/revisions", reader.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal("Before", Api.Str(seen.GetProperty("objects")[0], "body"), "Readers see the history");
            await f.Services.Feed.SetModerationStateAsync(id, ModerationStateEnum.Hidden, null, "Review", ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Get, "/api/v1/feed/" + id + "/revisions", reader.AccessToken, null, HttpStatusCode.NotFound, "NotFound", ct).ConfigureAwait(false);
            await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/feed/" + id + "/revisions", poster.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            JsonElement asAuthor = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/feed/" + id, poster.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal("Hidden", Api.Str(asAuthor, "state"), "Author sees Hidden");
            Check.Equal("Review", Api.Str(asAuthor.GetProperty("moderation"), "reason"), "Author sees the moderation object");
            await f.Services.Feed.SetModerationStateAsync(id, ModerationStateEnum.None, null, null, ct).ConfigureAwait(false);
            string comment = await ContentTestKit.CommentIdAsync(f, poster.AccessToken, id, "c1", null, ct).ConfigureAwait(false);
            await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/comments/" + comment, poster.AccessToken, new { body = "c2" }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/comments/" + comment + "/revisions", reader.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            await Api.ExpectAsync(f, HttpMethod.Delete, "/api/v1/comments/" + comment, poster.AccessToken, null, HttpStatusCode.NoContent, ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Get, "/api/v1/comments/" + comment + "/revisions", reader.AccessToken, null, HttpStatusCode.NotFound, "NotFound", ct).ConfigureAwait(false);
            Check.Equal(0L, await ContentTestKit.ScalarAsync(f, "SELECT COUNT(*) AS cnt FROM commentrevisions WHERE commentid = '" + comment + "';", ct).ConfigureAwait(false), "Revisions deleted with the comment");
        }

        private static async Task CommentsAsync(HttpFixture f, CancellationToken ct)
        {
            TestMember poster = await ContentTestKit.PosterAsync(f, ct).ConfigureAwait(false);
            string post = await ContentTestKit.PostIdAsync(f, poster.AccessToken, "Post", ct).ConfigureAwait(false);
            string comment = await ContentTestKit.CommentIdAsync(f, poster.AccessToken, post, "first words", null, ct).ConfigureAwait(false);
            JsonElement silent = await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/comments/" + comment, poster.AccessToken, new { body = "first word" }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal(0, silent.GetProperty("revisionCount").GetInt32(), "Silent in the grace window");
            f.Settings.Content.EditGraceSeconds = 0;
            for (int i = 0; i < 3; i++) await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/comments/" + comment, poster.AccessToken, new { body = "edit " + i }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            string image = await ContentTestKit.MediaAsync(f, poster.Id, MediaKindEnum.Image, ct).ConfigureAwait(false);
            JsonElement withMedia = await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/comments/" + comment, poster.AccessToken, new { mediaIds = new[] { image } }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal(4, withMedia.GetProperty("revisionCount").GetInt32(), "Four revisions");
            Check.True(withMedia.GetProperty("editedUtc").ValueKind == JsonValueKind.String, "editedUtc");
            JsonElement history = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/comments/" + comment + "/revisions", poster.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            List<JsonElement> items = history.GetProperty("objects").EnumerateArray().ToList();
            Check.Equal("Added 1 image", Api.Str(items[0], "attachmentSummary"), "Attachment summary");
            Check.Equal("first word", Api.Str(items[items.Count - 1], "body"), "Oldest revision keeps the body before the first stored edit");
            ContentTestKit.CheckMetadata(items[0], "comment revision");
        }

        private static async Task MetadataAsync(HttpFixture f, CancellationToken ct)
        {
            TestMember poster = await ContentTestKit.PosterAsync(f, ct).ConfigureAwait(false);
            TestMember reader = await ContentTestKit.MemberAsync(f, ct).ConfigureAwait(false);
            TestMember admin = await ContentTestKit.AdminAsync(f, ct).ConfigureAwait(false);
            f.Settings.Content.EditGraceSeconds = 0;
            string id = Api.Str(await ContentTestKit.PostAsync(f, poster.AccessToken, new { body = "Version one", labels = new[] { "draft" }, tags = new Dictionary<string, string> { { "level", "beginner" } } }, ct).ConfigureAwait(false), "id");
            await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/metadata/FeedPost/" + id, admin.AccessToken, new { labels = new[] { "draft", "staff:review" }, tags = new Dictionary<string, string> { { "level", "beginner" } } }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/feed/" + id, poster.AccessToken, new { body = "Version two", labels = new[] { "final" }, tags = new Dictionary<string, string> { { "level", "advanced" } } }, HttpStatusCode.OK, ct).ConfigureAwait(false);

            JsonElement asReader = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/feed/" + id + "/revisions", reader.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            JsonElement revision = asReader.GetProperty("objects")[0];
            List<string> readerLabels = revision.GetProperty("labels").EnumerateArray().Select(l => l.GetString() ?? String.Empty).ToList();
            Check.True(readerLabels.SequenceEqual(new[] { "draft" }), "The revision keeps the labels of its version; members do not see staff: labels");
            Check.Equal("beginner", Api.Str(revision.GetProperty("tags"), "level"), "The revision keeps the tags of its version");
            JsonElement asAdmin = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/feed/" + id + "/revisions", admin.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.True(asAdmin.GetProperty("objects")[0].GetProperty("labels").EnumerateArray().Any(l => l.GetString() == "staff:review"), "Staff see staff: labels on revisions");
            JsonElement byTag = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/feed/" + id + "/revisions/enumerate", reader.AccessToken,
                new { tags = new Dictionary<string, string> { { "level", "beginner" } } }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal(1, byTag.GetProperty("objects").GetArrayLength(), "Tag filter matches the revision");
            JsonElement byNewTag = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/feed/" + id + "/revisions/enumerate", reader.AccessToken,
                new { tags = new Dictionary<string, string> { { "level", "advanced" } } }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal(0, byNewTag.GetProperty("objects").GetArrayLength(), "The current metadata is not the revision's");
            await Api.ExpectErrorAsync(f, HttpMethod.Get, "/api/v1/feed/" + id + "/revisions?labels=staff:review", reader.AccessToken, null, HttpStatusCode.BadRequest, "ValidationFailed", ct).ConfigureAwait(false);
            string revisionId = Api.Str(revision, "id");
            await Api.ExpectErrorAsync(f, HttpMethod.Put, "/api/v1/metadata/FeedPostRevision/" + revisionId, admin.AccessToken, new { labels = new[] { "changed" } }, HttpStatusCode.NotFound, "NotFound", ct).ConfigureAwait(false);

            string comment = Api.Str(await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/feed/" + id + "/comments", poster.AccessToken, new { body = "c1", labels = new[] { "question" } }, HttpStatusCode.Created, ct).ConfigureAwait(false), "id");
            await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/comments/" + comment, poster.AccessToken, new { body = "c2", labels = new[] { "answered" } }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            JsonElement commentRevisions = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/comments/" + comment + "/revisions?labels=question", reader.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal(1, commentRevisions.GetProperty("objects").GetArrayLength(), "Comment revisions filter on their own labels");
            Check.Equal("question", commentRevisions.GetProperty("objects")[0].GetProperty("labels")[0].GetString(), "The comment revision keeps the label of its version");

            await f.Database.Comments.PurgeAsync(comment, DateTime.UtcNow, ct).ConfigureAwait(false);
            await f.Database.FeedPosts.PurgeAsync(id, DateTime.UtcNow, ct).ConfigureAwait(false);
            Check.Equal(0L, await ContentTestKit.ScalarAsync(f, "SELECT COUNT(*) AS cnt FROM labels WHERE targettype IN ('FeedPostRevision', 'CommentRevision') AND targetid IN ('" + revisionId + "', '" +
                Api.Str(commentRevisions.GetProperty("objects")[0], "id") + "');", ct).ConfigureAwait(false), "Purge deletes revision labels");
        }

        private static Task SummaryAsync(CancellationToken ct)
        {
            MediaAttachment i1 = new MediaAttachment { Id = "med_1", Kind = MediaKindEnum.Image };
            MediaAttachment i2 = new MediaAttachment { Id = "med_2", Kind = MediaKindEnum.Image };
            MediaAttachment v1 = new MediaAttachment { Id = "med_3", Kind = MediaKindEnum.Video };
            Check.Equal("Removed 1 image, added 1 video", AttachmentSummaryText.Describe(new[] { i1 }, new[] { v1 }), "Image for video");
            Check.Equal("Removed 2 images", AttachmentSummaryText.Describe(new[] { i1, i2 }, Array.Empty<MediaAttachment>()), "Plural");
            Check.Equal("Added 1 image and 1 video", AttachmentSummaryText.Describe(Array.Empty<MediaAttachment>(), new[] { i1, v1 }), "Both kinds");
            Check.Equal("Reordered attachments", AttachmentSummaryText.Describe(new[] { i1, i2 }, new[] { i2, i1 }), "Reorder");
            Check.Equal<string?>(null, AttachmentSummaryText.Describe(new[] { i1 }, new[] { i1 }), "No change");
            return Task.CompletedTask;
        }

        #endregion
    }
}
