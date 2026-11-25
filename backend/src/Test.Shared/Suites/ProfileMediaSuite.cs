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
    using TrellisKit.Core.Helpers;
    using TrellisKit.Core.Models;

    /// <summary>Gallery ownership, bounded reads, reporting, and public access revocation.</summary>
    public static class ProfileMediaSuite
    {
        /// <summary>Suite identifier.</summary>
        public const string Id = "ProfileMedia";
        /// <summary>Build integration cases.</summary>
        public static TestSuiteDescriptor Build() => new TestSuiteDescriptor(Id, "Profile and public multimedia", new[]
        {
            SuiteCases.Http(Id, "ProfileMedia.Lifecycle", "Owners attach and delete, readers filter and report, moderation and blocks apply", LifecycleAsync),
            SuiteCases.Http(Id, "ProfileMedia.PublicVisibility", "Public pages render photos/video and per-item reporting; file URLs revoke with privacy, moderation, deletion and venue closure", PublicAsync),
            SuiteCases.Http(Id, "ProfileMedia.AtomicAttach", "Concurrent attachment consumes each upload only once and leaves no orphan item", ConcurrentAsync)
        });

        private static string PathOf(string userId) => "/api/v1/users/" + userId + "/media";
        private static async Task<string> AddAsync(HttpFixture f, TestMember member, string media, CancellationToken ct)
        {
            JsonElement result = await Api.ExpectAsync(f, HttpMethod.Post, PathOf(member.Id), member.AccessToken, new { mediaIds = new[] { media }, labels = new[] { "gallery" }, tags = new { scene = "practice" } }, HttpStatusCode.Created, ct).ConfigureAwait(false);
            return Api.Str(result.GetProperty("objects")[0], "id");
        }
        private static async Task LifecycleAsync(HttpFixture f, CancellationToken ct)
        {
            DirectoryTestKit.Relax(f);
            TestMember owner = await Api.MemberAsync(f, ct).ConfigureAwait(false);
            TestMember reader = await Api.MemberAsync(f, ct).ConfigureAwait(false);
            string image = await ContentTestKit.MediaAsync(f, owner.Id, MediaKindEnum.Image, ct).ConfigureAwait(false);
            string video = await ContentTestKit.MediaAsync(f, owner.Id, MediaKindEnum.Video, ct).ConfigureAwait(false);
            string item = await AddAsync(f, owner, image, ct).ConfigureAwait(false);
            await AddAsync(f, owner, video, ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Post, PathOf(owner.Id), reader.AccessToken, new { mediaIds = new[] { image } }, HttpStatusCode.Forbidden, "Forbidden", ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Post, PathOf(owner.Id), owner.AccessToken, new { mediaIds = new[] { image } }, HttpStatusCode.Conflict, "MediaNotAttachable", ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Delete, PathOf(owner.Id) + "/" + item, reader.AccessToken, null, HttpStatusCode.Forbidden, "Forbidden", ct).ConfigureAwait(false);
            JsonElement page = await Api.ExpectAsync(f, HttpMethod.Post, PathOf(owner.Id) + "/enumerate", reader.AccessToken, new { maxResults = 1 }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal(1, page.GetProperty("objects").GetArrayLength(), "Page bounded");
            string first = Api.Str(page.GetProperty("objects")[0], "id");
            JsonElement next = await Api.ExpectAsync(f, HttpMethod.Post, PathOf(owner.Id) + "/enumerate", reader.AccessToken, new { maxResults = 1, continuationToken = Api.Str(page, "continuationToken") }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.True(first != Api.Str(next.GetProperty("objects")[0], "id"), "No repeated page item");
            JsonElement filtered = await Api.ExpectAsync(f, HttpMethod.Post, PathOf(owner.Id) + "/enumerate", reader.AccessToken, new { labels = new[] { "gallery" }, tags = new { scene = "practice" }, filters = new { kind = "Image" } }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal(1, filtered.GetProperty("objects").GetArrayLength(), "Kind, label and tag filters");
            Check.True(filtered.GetProperty("objects")[0].GetProperty("viewer").GetProperty("canReport").GetBoolean(), "Reader can report");
            await ModerationTestKit.ReportOkAsync(f, reader.AccessToken, "ProfileMedia", item, ct).ConfigureAwait(false);
            TestMember moderator = await ModerationTestKit.ModeratorAsync(f, ct).ConfigureAwait(false);
            await ModerationTestKit.ActOkAsync(f, moderator.AccessToken, "ProfileMedia", item, "Hide", ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Get, "/api/v1/media/" + image, reader.AccessToken, null, HttpStatusCode.NotFound, "NotFound", ct).ConfigureAwait(false);
            await ModerationTestKit.ActOkAsync(f, moderator.AccessToken, "ProfileMedia", item, "Unhide", ct).ConfigureAwait(false);
            await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/users/" + owner.Id + "/block", reader.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Post, PathOf(owner.Id) + "/enumerate", reader.AccessToken, new { }, HttpStatusCode.NotFound, "NotFound", ct).ConfigureAwait(false);
            await Api.ExpectAsync(f, HttpMethod.Delete, PathOf(owner.Id) + "/" + item, owner.AccessToken, null, HttpStatusCode.NoContent, ct).ConfigureAwait(false);
            Check.True((await f.Database.Media.ReadAsync(image, ct).ConfigureAwait(false))!.IsDeleted, "Owner deletion purges media");
        }

        private static async Task<string> StoredPhotoAsync(HttpFixture f, TestMember owner, CancellationToken ct)
        {
            string id = await ContentTestKit.MediaAsync(f, owner.Id, MediaKindEnum.Image, ct, MediaStatusEnum.Processing, "Photo <script>unsafe</script>").ConfigureAwait(false);
            MediaAttachment media = (await f.Database.Media.ReadAsync(id, ct).ConfigureAwait(false))!;
            media.Status = MediaStatusEnum.Ready;
            media.ReadyUtc = DateTime.UtcNow;
            await f.Database.Media.CompleteProcessingAsync(media, new List<MediaVariant> { new MediaVariant { MediaId = id, Name = "medium", Format = "jpeg", FileName = "medium.jpg", Width = 40, Height = 40, SizeBytes = 4 } }, ct).ConfigureAwait(false);
            await f.Fakes.ObjectStore.PutAsync(MediaLocationEnum.Images, MediaKeys.ImagePrefix(f.Services.Media.PropertyId, id) + "medium.jpg", new byte[] { 1, 2, 3, 4 }, "image/jpeg", ct).ConfigureAwait(false);
            return id;
        }
        private static async Task PublicAsync(HttpFixture f, CancellationToken ct)
        {
            DirectoryTestKit.Relax(f);
            TestMember owner = await Api.MemberAsync(f, ct).ConfigureAwait(false);
            string photo = await StoredPhotoAsync(f, owner, ct).ConfigureAwait(false);
            string item = await AddAsync(f, owner, photo, ct).ConfigureAwait(false);
            string videoId = await ContentTestKit.MediaAsync(f, owner.Id, MediaKindEnum.Video, ct, MediaStatusEnum.Processing).ConfigureAwait(false);
            MediaAttachment video = (await f.Database.Media.ReadAsync(videoId, ct).ConfigureAwait(false))!;
            video.Status = MediaStatusEnum.Ready;
            video.ReadyUtc = DateTime.UtcNow;
            video.HlsManifestFile = "index.m3u8";
            await f.Database.Media.CompleteProcessingAsync(video, new List<MediaVariant>(), ct).ConfigureAwait(false);
            await f.Fakes.ObjectStore.PutAsync(MediaLocationEnum.Transcoded, MediaKeys.VideoPrefix(f.Services.Media.PropertyId, videoId) + "index.m3u8", System.Text.Encoding.UTF8.GetBytes("#EXTM3U\n"), "application/vnd.apple.mpegurl", ct).ConfigureAwait(false);
            await AddAsync(f, owner, videoId, ct).ConfigureAwait(false);
            string videoFile = "/api/v1/media/gallery/" + videoId + "/index.m3u8";
            string file = "/api/v1/media/gallery/" + photo + "/medium.jpg";
            await Api.ExpectErrorAsync(f, HttpMethod.Get, file, null, null, HttpStatusCode.NotFound, "NotFound", ct).ConfigureAwait(false);
            await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/me/public-page", owner.AccessToken, new { enabled = true, slug = "gallery-owner" }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            ApiResult html = await Api.SendAsync(f, HttpMethod.Get, "/api/v1/public/pages/resolve/profiles/gallery-owner", null, null, ct).ConfigureAwait(false);
            Check.Equal(HttpStatusCode.OK, html.Status, "Public profile renders");
            Check.True(html.Text.Contains(file, StringComparison.Ordinal) && html.Text.Contains("?reportMedia=" + item, StringComparison.Ordinal), "Public image and item report link");
            Check.True(html.Text.Contains("&lt;script&gt;unsafe&lt;/script&gt;", StringComparison.Ordinal), "Image alt text is encoded");
            Check.True(html.Text.Contains("data-hls=\"" + videoFile, StringComparison.Ordinal), "Public video playlist uses the checked gallery route");
            Check.Equal(HttpStatusCode.OK, (await Api.SendAsync(f, HttpMethod.Get, videoFile, null, null, ct).ConfigureAwait(false)).Status, "Public HLS playlist");
            Check.True(html.Text.Contains("theme-toggle", StringComparison.Ordinal), "Public theme control");
            ApiResult bytes = await Api.SendAsync(f, HttpMethod.Get, file, null, null, ct).ConfigureAwait(false);
            Check.Equal(HttpStatusCode.OK, bytes.Status, "Anonymous file works");
            Check.Equal("no-store", bytes.Headers["Cache-Control"], "No stale public file cache");
            using (HttpRequestMessage range = new HttpRequestMessage(HttpMethod.Get, file))
            {
                f.Decorate(range);
                range.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(1, 2);
                using (HttpResponseMessage response = await f.Client.SendAsync(range, ct).ConfigureAwait(false))
                {
                    Check.Equal(HttpStatusCode.PartialContent, response.StatusCode, "Anonymous byte range");
                    Check.Equal(2, (await response.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false)).Length, "Requested bytes only");
                }
            }

            await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/me/public-page", owner.AccessToken, new { enabled = false }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Get, videoFile, null, null, HttpStatusCode.NotFound, "NotFound", ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Get, file, null, null, HttpStatusCode.NotFound, "NotFound", ct).ConfigureAwait(false);
            await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/me/public-page", owner.AccessToken, new { enabled = true }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            TestMember moderator = await ModerationTestKit.ModeratorAsync(f, ct).ConfigureAwait(false);
            await ModerationTestKit.ActOkAsync(f, moderator.AccessToken, "ProfileMedia", item, "Hide", ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Get, file, null, null, HttpStatusCode.NotFound, "NotFound", ct).ConfigureAwait(false);
            await ModerationTestKit.ActOkAsync(f, moderator.AccessToken, "ProfileMedia", item, "Unhide", ct).ConfigureAwait(false);
            await Api.ExpectAsync(f, HttpMethod.Delete, PathOf(owner.Id) + "/" + item, owner.AccessToken, null, HttpStatusCode.NoContent, ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Get, file, null, null, HttpStatusCode.NotFound, "NotFound", ct).ConfigureAwait(false);

            string venue = await DirectoryTestKit.VenueIdAsync(f, "Gallery Club", ct).ConfigureAwait(false);
            string venuePhoto = await StoredPhotoAsync(f, owner, ct).ConfigureAwait(false);
            await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/venues/" + venue + "/media", owner.AccessToken, new { mediaIds = new[] { venuePhoto } }, HttpStatusCode.Created, ct).ConfigureAwait(false);
            string venueFile = "/api/v1/media/gallery/" + venuePhoto + "/medium.jpg";
            Check.Equal(HttpStatusCode.OK, (await Api.SendAsync(f, HttpMethod.Get, venueFile, null, null, ct).ConfigureAwait(false)).Status, "Public venue media");
            await Api.ExpectAsync(f, HttpMethod.Post, DirectoryTestKit.AdminVenues + "/" + venue + "/close", HttpFixture.AdminToken, new { reason = "Closed" }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Get, venueFile, null, null, HttpStatusCode.NotFound, "NotFound", ct).ConfigureAwait(false);
        }

        private static async Task ConcurrentAsync(HttpFixture f, CancellationToken ct)
        {
            DirectoryTestKit.Relax(f);
            TestMember owner = await Api.MemberAsync(f, ct).ConfigureAwait(false);
            string photo = await ContentTestKit.MediaAsync(f, owner.Id, MediaKindEnum.Image, ct).ConfigureAwait(false);
            ApiResult[] results = await Task.WhenAll(Enumerable.Range(0, 2).Select(_ => Api.SendAsync(f, HttpMethod.Post, PathOf(owner.Id), owner.AccessToken, new { mediaIds = new[] { photo } }, ct))).ConfigureAwait(false);
            Check.Equal(1, results.Count(r => r.Status == HttpStatusCode.Created), "One winner");
            Check.Equal(1, results.Count(r => r.Status == HttpStatusCode.Conflict), "Duplicate rejected");
            Check.Equal(1L, await ContentTestKit.ScalarAsync(f, "SELECT COUNT(*) AS cnt FROM profilemedia WHERE mediaid = '" + photo + "';", ct).ConfigureAwait(false), "One item only");
        }
    }
}
