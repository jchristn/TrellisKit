namespace Test.Shared.Suites
{
    using System;
    using System.Collections.Generic;
    using System.Net;
    using System.Net.Http;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Touchstone.Core;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Models;

    /// <summary>
    /// Phase 2 posting permissions (contract section 1.1): canPostToFeed, canPost, canPostMedia, canUploadVideo, mute,
    /// and SystemAdmin ignoring the flags, on posts, comments, reactions, and edits.
    /// </summary>
    public static class PermissionSuite
    {
        #region Public-Members

        /// <summary>
        /// Suite identifier.
        /// </summary>
        public const string Id = "Permissions";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the suite.
        /// </summary>
        /// <returns>Suite.</returns>
        public static TestSuiteDescriptor Build()
        {
            return new TestSuiteDescriptor(Id, "Posting permissions (" + TestEnvironment.Provider + ")", new[]
            {
                Case("Permissions.CanPostToFeed", "Members without canPostToFeed get 403 Forbidden on create; the admin token cannot author on the member route", CanPostToFeedAsync),
                Case("Permissions.CanPost", "canPost false blocks posts, comments, and edits (403 PostingNotAllowed) but not reacting or deleting your own content", CanPostAsync),
                Case("Permissions.CanPostMedia", "canPostMedia false allows text only (403 MediaNotAllowed) and turns the attach flags off", CanPostMediaAsync),
                Case("Permissions.CanUploadVideo", "canUploadVideo false allows images only (403 VideoUploadNotAllowed)", CanUploadVideoAsync),
                Case("Permissions.Mute", "Muted members get 403 Muted on create, edit, and react, and can still read and delete their own content", MuteAsync),
                Case("Regression.Permissions.MutedUntilField", "403 Muted carries mutedUntilUtc as a body field (null for an indefinite mute) and a plain message without a timestamp", async (f, ct) =>
                {
                    TestMember poster = await ContentTestKit.PosterAsync(f, ct).ConfigureAwait(false);
                    DateTime until = DateTime.UtcNow.AddDays(3);
                    await SafetyHelpers.UpdateUserAsync(f, poster.Id, u => { u.IsMuted = true; u.MutedUntilUtc = until; }, ct).ConfigureAwait(false);
                    ApiResult timed = await Api.SendAsync(f, HttpMethod.Post, "/api/v1/feed", poster.AccessToken, new { body = "muted" }, ct).ConfigureAwait(false);
                    Check.Equal("Muted", timed.Error, "Muted");
                    Check.True(Math.Abs((timed.Body.GetProperty("mutedUntilUtc").GetDateTime().ToUniversalTime() - until).TotalSeconds) < 2, "mutedUntilUtc field");
                    Check.True(!System.Text.RegularExpressions.Regex.IsMatch(Api.Str(timed.Body, "message"), "\\d{4}-\\d{2}-\\d{2}"), "Plain message: " + Api.Str(timed.Body, "message"));
                    Check.True(timed.Body.TryGetProperty("fieldErrors", out JsonElement _), "Standard error fields kept");
                    await SafetyHelpers.UpdateUserAsync(f, poster.Id, u => { u.IsMuted = true; u.MutedUntilUtc = null; }, ct).ConfigureAwait(false);
                    ApiResult indefinite = await Api.SendAsync(f, HttpMethod.Post, "/api/v1/feed", poster.AccessToken, new { body = "muted" }, ct).ConfigureAwait(false);
                    Check.True(indefinite.Body.GetProperty("mutedUntilUtc").ValueKind == JsonValueKind.Null, "Indefinite mute: null");
                }),
                Case("Permissions.SystemAdmin", "SystemAdmin ignores the permission flags and mute", SystemAdminAsync)
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

        private static async Task CanPostToFeedAsync(HttpFixture f, CancellationToken ct)
        {
            TestMember m = await ContentTestKit.MemberAsync(f, ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Post, "/api/v1/feed", m.AccessToken, new { body = "hello" }, HttpStatusCode.Forbidden, "Forbidden", ct).ConfigureAwait(false);
            await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/admin/members/" + m.Id + "/flags", HttpFixture.AdminToken, new { canPostToFeed = true }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            await ContentTestKit.PostIdAsync(f, m.AccessToken, "now allowed", ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Post, "/api/v1/feed", HttpFixture.AdminToken, new { body = "token" }, HttpStatusCode.Forbidden, "Forbidden", ct).ConfigureAwait(false);
        }

        private static async Task CanPostAsync(HttpFixture f, CancellationToken ct)
        {
            TestMember poster = await ContentTestKit.PosterAsync(f, ct).ConfigureAwait(false);
            string post = await ContentTestKit.PostIdAsync(f, poster.AccessToken, "mine", ct).ConfigureAwait(false);
            string comment = await ContentTestKit.CommentIdAsync(f, poster.AccessToken, post, "my comment", null, ct).ConfigureAwait(false);
            await f.Database.UserPermissions.UpsertAsync(new UserPermission { UserId = poster.Id, CanPost = false, CanPostMedia = true }, ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Post, "/api/v1/feed", poster.AccessToken, new { body = "blocked" }, HttpStatusCode.Forbidden, "PostingNotAllowed", ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Post, "/api/v1/feed/" + post + "/comments", poster.AccessToken, new { body = "blocked" }, HttpStatusCode.Forbidden, "PostingNotAllowed", ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Put, "/api/v1/feed/" + post, poster.AccessToken, new { body = "edit" }, HttpStatusCode.Forbidden, "PostingNotAllowed", ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Put, "/api/v1/comments/" + comment, poster.AccessToken, new { body = "edit" }, HttpStatusCode.Forbidden, "PostingNotAllowed", ct).ConfigureAwait(false);
            JsonElement read = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/feed/" + post, poster.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.True(!read.GetProperty("viewer").GetProperty("canEdit").GetBoolean() && !read.GetProperty("viewer").GetProperty("canComment").GetBoolean(), "Viewer flags follow canPost");
            await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/feed/" + post + "/reactions/like", poster.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            await Api.ExpectAsync(f, HttpMethod.Delete, "/api/v1/comments/" + comment, poster.AccessToken, null, HttpStatusCode.NoContent, ct).ConfigureAwait(false);
            await Api.ExpectAsync(f, HttpMethod.Delete, "/api/v1/feed/" + post, poster.AccessToken, null, HttpStatusCode.NoContent, ct).ConfigureAwait(false);
        }

        private static async Task CanPostMediaAsync(HttpFixture f, CancellationToken ct)
        {
            TestMember poster = await ContentTestKit.PosterAsync(f, ct).ConfigureAwait(false);
            TestMember m = await ContentTestKit.MemberAsync(f, ct).ConfigureAwait(false);
            string post = await ContentTestKit.PostIdAsync(f, poster.AccessToken, "photos?", ct).ConfigureAwait(false);
            string image = await ContentTestKit.MediaAsync(f, m.Id, MediaKindEnum.Image, ct).ConfigureAwait(false);
            await f.Database.UserPermissions.UpsertAsync(new UserPermission { UserId = m.Id, CanPost = true, CanPostMedia = false }, ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Post, "/api/v1/feed/" + post + "/comments", m.AccessToken, new { body = "pic", mediaIds = new[] { image } }, HttpStatusCode.Forbidden, "MediaNotAllowed", ct).ConfigureAwait(false);
            await ContentTestKit.CommentIdAsync(f, m.AccessToken, post, "text only is fine", null, ct).ConfigureAwait(false);
            JsonElement read = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/feed/" + post, m.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.True(!read.GetProperty("viewer").GetProperty("canAttachMedia").GetBoolean() && !read.GetProperty("viewer").GetProperty("canAttachVideo").GetBoolean(), "Attach flags off");
            string posterImage = await ContentTestKit.MediaAsync(f, poster.Id, MediaKindEnum.Image, ct).ConfigureAwait(false);
            await f.Database.UserPermissions.UpsertAsync(new UserPermission { UserId = poster.Id, CanPost = true, CanPostMedia = false }, ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Post, "/api/v1/feed", poster.AccessToken, new { body = "pic", mediaIds = new[] { posterImage } }, HttpStatusCode.Forbidden, "MediaNotAllowed", ct).ConfigureAwait(false);
        }

        private static async Task CanUploadVideoAsync(HttpFixture f, CancellationToken ct)
        {
            TestMember poster = await ContentTestKit.PosterAsync(f, ct).ConfigureAwait(false);
            string video = await ContentTestKit.MediaAsync(f, poster.Id, MediaKindEnum.Video, ct).ConfigureAwait(false);
            string image = await ContentTestKit.MediaAsync(f, poster.Id, MediaKindEnum.Image, ct).ConfigureAwait(false);
            await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/admin/members/" + poster.Id + "/flags", HttpFixture.AdminToken, new { canUploadVideo = false }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Post, "/api/v1/feed", poster.AccessToken, new { body = "clip", mediaIds = new[] { video } }, HttpStatusCode.Forbidden, "VideoUploadNotAllowed", ct).ConfigureAwait(false);
            JsonElement ok = await ContentTestKit.PostAsync(f, poster.AccessToken, new { body = "photo", mediaIds = new[] { image } }, ct).ConfigureAwait(false);
            Check.True(ok.GetProperty("viewer").GetProperty("canAttachMedia").GetBoolean() && !ok.GetProperty("viewer").GetProperty("canAttachVideo").GetBoolean(), "Images yes, video no");
        }

        private static async Task MuteAsync(HttpFixture f, CancellationToken ct)
        {
            TestMember poster = await ContentTestKit.PosterAsync(f, ct).ConfigureAwait(false);
            string post = await ContentTestKit.PostIdAsync(f, poster.AccessToken, "before mute", ct).ConfigureAwait(false);
            string comment = await ContentTestKit.CommentIdAsync(f, poster.AccessToken, post, "comment", null, ct).ConfigureAwait(false);
            DateTime until = DateTime.UtcNow.AddHours(2);
            await SafetyHelpers.UpdateUserAsync(f, poster.Id, u => { u.IsMuted = true; u.MutedUntilUtc = until; }, ct).ConfigureAwait(false);
            ApiResult create = await Api.SendAsync(f, HttpMethod.Post, "/api/v1/feed", poster.AccessToken, new { body = "muted" }, ct).ConfigureAwait(false);
            Check.Equal(HttpStatusCode.Forbidden, create.Status, "Create");
            Check.Equal("Muted", create.Error, "Muted code");
            Check.True(!Api.Str(create.Body, "message").Contains(until.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal), "Regression.Muted: the message carries no raw timestamp");
            Check.True(Math.Abs((create.Body.GetProperty("mutedUntilUtc").GetDateTime().ToUniversalTime() - until).TotalSeconds) < 2, "Regression.Muted: mutedUntilUtc is a field of the error body");
            await Api.ExpectErrorAsync(f, HttpMethod.Put, "/api/v1/feed/" + post, poster.AccessToken, new { body = "edit" }, HttpStatusCode.Forbidden, "Muted", ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Post, "/api/v1/feed/" + post + "/comments", poster.AccessToken, new { body = "c" }, HttpStatusCode.Forbidden, "Muted", ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Put, "/api/v1/comments/" + comment, poster.AccessToken, new { body = "e" }, HttpStatusCode.Forbidden, "Muted", ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Put, "/api/v1/feed/" + post + "/reactions/like", poster.AccessToken, null, HttpStatusCode.Forbidden, "Muted", ct).ConfigureAwait(false);
            JsonElement read = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/feed/" + post, poster.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.True(!read.GetProperty("viewer").GetProperty("canReact").GetBoolean() && !read.GetProperty("viewer").GetProperty("canEdit").GetBoolean(), "Viewer flags follow mute");
            await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/feed/" + post + "/comments", poster.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            await Api.ExpectAsync(f, HttpMethod.Delete, "/api/v1/comments/" + comment, poster.AccessToken, null, HttpStatusCode.NoContent, ct).ConfigureAwait(false);
            await Api.ExpectAsync(f, HttpMethod.Delete, "/api/v1/feed/" + post, poster.AccessToken, null, HttpStatusCode.NoContent, ct).ConfigureAwait(false);
        }

        private static async Task SystemAdminAsync(HttpFixture f, CancellationToken ct)
        {
            TestMember admin = await ContentTestKit.AdminAsync(f, ct).ConfigureAwait(false);
            await Api.GrantCompAsync(f, admin.Id, ct).ConfigureAwait(false);
            await f.Database.UserPermissions.UpsertAsync(new UserPermission { UserId = admin.Id, CanPost = false, CanPostMedia = false }, ct).ConfigureAwait(false);
            await SafetyHelpers.UpdateUserAsync(f, admin.Id, u => { u.IsMuted = true; u.CanUploadVideo = false; u.CanPostToFeed = false; }, ct).ConfigureAwait(false);
            string video = await ContentTestKit.MediaAsync(f, admin.Id, MediaKindEnum.Video, ct).ConfigureAwait(false);
            JsonElement post = await ContentTestKit.PostAsync(f, admin.AccessToken, new { body = "admin", mediaIds = new[] { video } }, ct).ConfigureAwait(false);
            string id = Api.Str(post, "id");
            await ContentTestKit.CommentIdAsync(f, admin.AccessToken, id, "admin comment", null, ct).ConfigureAwait(false);
            await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/feed/" + id + "/reactions/like", admin.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/feed/" + id, admin.AccessToken, new { body = "admin edit" }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.True(post.GetProperty("viewer").GetProperty("canAttachVideo").GetBoolean() && post.GetProperty("viewer").GetProperty("canModerate").GetBoolean(), "SystemAdmin flags");
        }

        #endregion
    }
}
