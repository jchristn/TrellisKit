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
    using TrellisKit.Core.Models;

    /// <summary>
    /// Phase 2 public sample posts (contract section 5.10): anonymous list and read, the PublicFeedPost shape, the cache
    /// header, the per-IP rate limit, features.publicSamples, and public media copies following the post.
    /// </summary>
    public static class PublicFeedSuite
    {
        #region Public-Members

        /// <summary>
        /// Suite identifier.
        /// </summary>
        public const string Id = "PublicFeed";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the suite.
        /// </summary>
        /// <returns>Suite.</returns>
        public static TestSuiteDescriptor Build()
        {
            return new TestSuiteDescriptor(Id, "Public sample posts (" + TestEnvironment.Provider + ")", new[]
            {
                SuiteCases.Http(Id, "PublicFeed.Anonymous", "Anonymous visitors list and read Public posts as PublicFeedPost with Cache-Control public, max-age=60", AnonymousAsync),
                SuiteCases.Http(Id, "PublicFeed.RateLimit", "rateLimits.publicFeedPerMinute limits anonymous reads per IP with Retry-After", RateLimitAsync),
                SuiteCases.Http(Id, "PublicFeed.Disabled", "features.publicSamples false makes the public routes 404", p => p.Features.PublicSamples = false, DisabledAsync),
                SuiteCases.Http(Id, "PublicFeed.MediaCopies", "Media of a Public post is copied to the public paths and removed when the post stops being Public, is hidden, or is deleted", MediaCopiesAsync)
            });
        }

        #endregion

        #region Private-Methods

        private static async Task AnonymousAsync(HttpFixture f, CancellationToken ct)
        {
            TestMember admin = await ContentTestKit.AdminAsync(f, ct).ConfigureAwait(false);
            TestMember poster = await ContentTestKit.PosterAsync(f, ct, "Plain Poster", "plainposter").ConfigureAwait(false);
            JsonElement sample = await ContentTestKit.PostAsync(f, admin.AccessToken, new { title = "Sample", body = "Hello @plainposter", visibility = "Public", labels = new[] { "teaser", "staff:pick" } }, ct).ConfigureAwait(false);
            Check.Equal("Public", Api.Str(sample, "audience"), "Audience follows visibility");
            string members = await ContentTestKit.PostIdAsync(f, poster.AccessToken, "Members only", ct).ConfigureAwait(false);
            string audience = Api.Str(await ContentTestKit.PostAsync(f, admin.AccessToken, new { body = "By audience", audience = "Public" }, ct).ConfigureAwait(false), "id");
            using (HttpResponseMessage response = await f.Client.GetAsync("/api/v1/public/feed", ct).ConfigureAwait(false))
            {
                Check.Equal(HttpStatusCode.OK, response.StatusCode, "Anonymous list");
                Check.Equal("public, max-age=60", String.Join(",", response.Headers.GetValues("Cache-Control")), "Cache-Control");
                using (JsonDocument doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false)))
                {
                    List<string> ids = ContentTestKit.Ids(doc.RootElement);
                    Check.True(ids.SequenceEqual(new[] { audience, Api.Str(sample, "id") }), "Public posts newest first, members-only omitted");
                    JsonElement item = doc.RootElement.GetProperty("objects")[1];
                    foreach (string missing in new[] { "myReactions", "viewer", "moderation", "mentions" }) Check.True(!item.TryGetProperty(missing, out JsonElement _), "No " + missing);
                    Check.True(item.GetProperty("labels").EnumerateArray().Select(l => l.GetString()).SequenceEqual(new[] { "teaser" }), "Public labels only");
                    Check.True(item.TryGetProperty("commentCount", out JsonElement _) && item.TryGetProperty("reactionCounts", out JsonElement _), "Counts present");
                    Check.Equal(10, doc.RootElement.GetProperty("maxResults").GetInt32(), "Default page size 10");
                }
            }
            using (HttpResponseMessage one = await f.Client.GetAsync("/api/v1/public/feed/" + Api.Str(sample, "id"), ct).ConfigureAwait(false))
            {
                Check.Equal(HttpStatusCode.OK, one.StatusCode, "Anonymous read");
                Check.Equal("public, max-age=60", String.Join(",", one.Headers.GetValues("Cache-Control")), "Cache-Control on read");
            }
            await Api.ExpectErrorAsync(f, HttpMethod.Get, "/api/v1/public/feed/" + members, null, null, HttpStatusCode.NotFound, "NotFound", ct).ConfigureAwait(false);
            JsonElement clamped = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/public/feed/enumerate", null, new { maxResults = 500 }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal(20, clamped.GetProperty("maxResults").GetInt32(), "Clamped to 20");
            await Api.ExpectErrorAsync(f, HttpMethod.Get, "/api/v1/public/feed?labels=staff:pick", null, null, HttpStatusCode.BadRequest, "ValidationFailed", ct).ConfigureAwait(false);
            await f.Services.Feed.SetModerationStateAsync(Api.Str(sample, "id"), TrellisKit.Core.Enums.ModerationStateEnum.Hidden, null, "check", ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Get, "/api/v1/public/feed/" + Api.Str(sample, "id"), null, null, HttpStatusCode.NotFound, "NotFound", ct).ConfigureAwait(false);
        }

        private static async Task RateLimitAsync(HttpFixture f, CancellationToken ct)
        {
            f.Settings.RateLimits.PublicFeedPerMinute = 2;
            await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/public/feed", null, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/public/feed", null, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            ApiResult limited = await Api.SendAsync(f, HttpMethod.Get, "/api/v1/public/feed/fpo_any", null, null, ct).ConfigureAwait(false);
            Check.Equal((HttpStatusCode)429, limited.Status, "Per IP limit shared by list and read");
            Check.True(!String.IsNullOrEmpty(limited.RetryAfter), "Retry-After");
        }

        private static async Task DisabledAsync(HttpFixture f, CancellationToken ct)
        {
            TestMember admin = await ContentTestKit.AdminAsync(f, ct).ConfigureAwait(false);
            string id = Api.Str(await ContentTestKit.PostAsync(f, admin.AccessToken, new { body = "Sample", visibility = "Public" }, ct).ConfigureAwait(false), "id");
            await Api.ExpectErrorAsync(f, HttpMethod.Get, "/api/v1/public/feed", null, null, HttpStatusCode.NotFound, "NotFound", ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Get, "/api/v1/public/feed/" + id, null, null, HttpStatusCode.NotFound, "NotFound", ct).ConfigureAwait(false);
            await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/feed/" + id, admin.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
        }

        private static async Task MediaCopiesAsync(HttpFixture f, CancellationToken ct)
        {
            TestMember admin = await ContentTestKit.AdminAsync(f, ct).ConfigureAwait(false);
            string media = await MediaTestKit.UploadSingleAsync(f, admin.AccessToken, "Image", "image/png", MediaTestKit.Png(40, 30), ct).ConfigureAwait(false);
            await MediaTestKit.ProcessAsync(f, ct).ConfigureAwait(false);
            JsonElement post = await ContentTestKit.PostAsync(f, admin.AccessToken, new { body = "Public photo", visibility = "Public", mediaIds = new[] { media } }, ct).ConfigureAwait(false);
            string id = Api.Str(post, "id");
            Check.True(await IsPublicAsync(f, media, ct).ConfigureAwait(false), "Copied to the public paths on publish");
            JsonElement anonymous = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/public/feed/" + id, null, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            string url = anonymous.GetProperty("attachments")[0].GetProperty("image").GetProperty("variants").GetProperty("thumb").GetProperty("jpegUrl").GetString()!;
            Check.True(url.Contains("/public/", StringComparison.Ordinal), "Public media URL: " + url);
            await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/feed/" + id, admin.AccessToken, new { visibility = "Members" }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.True(!await IsPublicAsync(f, media, ct).ConfigureAwait(false), "Removed when the post becomes Members");
            await Api.ExpectErrorAsync(f, HttpMethod.Get, "/api/v1/public/feed/" + id, null, null, HttpStatusCode.NotFound, "NotFound", ct).ConfigureAwait(false);
            await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/feed/" + id, admin.AccessToken, new { visibility = "Public" }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.True(await IsPublicAsync(f, media, ct).ConfigureAwait(false), "Copied again");
            await f.Services.Feed.SetModerationStateAsync(id, TrellisKit.Core.Enums.ModerationStateEnum.Hidden, admin.Id, "check", ct).ConfigureAwait(false);
            Check.True(!await IsPublicAsync(f, media, ct).ConfigureAwait(false), "Removed when hidden");
            await f.Services.Feed.SetModerationStateAsync(id, TrellisKit.Core.Enums.ModerationStateEnum.None, admin.Id, null, ct).ConfigureAwait(false);
            Check.True(await IsPublicAsync(f, media, ct).ConfigureAwait(false), "Copied again when restored");
            await Api.ExpectAsync(f, HttpMethod.Delete, "/api/v1/feed/" + id, admin.AccessToken, null, HttpStatusCode.NoContent, ct).ConfigureAwait(false);
            Check.True(!await IsPublicAsync(f, media, ct).ConfigureAwait(false), "Removed when deleted");
        }

        private static async Task<bool> IsPublicAsync(HttpFixture f, string mediaId, CancellationToken ct)
        {
            MediaAttachment media = (await f.Database.Media.ReadAsync(mediaId, ct).ConfigureAwait(false))!;
            return media.IsPublic;
        }

        #endregion
    }
}
