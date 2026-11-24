namespace Test.Shared.Suites
{
    using SkiaSharp;
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Net;
    using System.Net.Http;
    using System.Text;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Touchstone.Core;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Helpers;
    using TrellisKit.Core.Integrations.Implementations;
    using TrellisKit.Core.Integrations.Models;
    using TrellisKit.Core.Models;
    using TrellisKit.Core.Responses;
    using TrellisKit.Server.Services;
    using TrellisKit.Server.Workers;

    /// <summary>
    /// Phase 2 media pipeline over HTTP in Local storage mode with the Passthrough video processor: upload sessions and
    /// their rules, signed local PUTs, single and multipart uploads with resume, complete checks (size, sniffing, mvhd
    /// duration, expiry), SkiaSharp processing (variant sizes, orientation, metadata removal, blurhash), alt text,
    /// labels and tags, delete rules, quota and rate limits, permissions, the media grant and the file route, the
    /// admin library, the content and moderation API, the cleanup worker, link preview images, and the MediaConvert
    /// webhook.
    /// </summary>
    public static class MediaSuite
    {
        #region Public-Members

        /// <summary>
        /// Suite identifier.
        /// </summary>
        public const string Id = "Media";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the suite.
        /// </summary>
        /// <returns>Suite.</returns>
        public static TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>
            {
                Case("Media.SessionRules", "Upload sessions validate kind, content type, size, duration, and file name with the contract errors", SessionRulesAsync),
                Case("Media.SingleImage", "A single PUT image upload completes (202, then 200 idempotent), processes to Ready with variants, blurhash, labels, and tags", SingleImageAsync),
                Case("Media.LocalSignatures", "Local PUTs reject bad and expired signatures, a wrong Content-Type, and oversize bodies, and expose ETag through CORS", LocalSignaturesAsync),
                Case("Media.MultipartResume", "A multipart video uploads part by part, resumes through upload-urls, completes, and plays as passthrough MP4 with Range", MultipartResumeAsync),
                Case("Media.CompleteChecks", "Complete rejects missing bytes and size mismatch (409), sniff mismatch (415 Failed), long video (400 Failed), and expiry (410)", CompleteChecksAsync),
                Case("Media.ImageProcessing", "Variants keep the aspect ratio, are never upscaled, honour EXIF orientation, and carry no EXIF or GPS data", ImageProcessingAsync),
                Case("Media.AltTextAndMetadata", "Alt text changes in any status with length limits; labels and tags are accepted and staff entries survive member edits", AltTextAsync),
                Case("Media.DeleteRules", "Owners delete unattached media only; others get 404; SystemAdmin deletes attached media and the detached handler runs", DeleteRulesAsync),
                Case("Media.QuotaAndRateLimits", "The daily byte quota, uploads per hour, and member reads return 429 with Retry-After", QuotaAsync),
                Case("Media.Permissions", "canPostMedia, canUploadVideo, and mute block upload sessions with 403 MediaNotAllowed, VideoUploadNotAllowed, and Muted", PermissionsAsync),
                Case("Media.GrantAndFiles", "The Local grant sets tk_media; the file route accepts the cookie (browser or plain header) or a member token, serves Range, and refuses anonymous callers", GrantAndFilesAsync),
                Case("Media.CloudFrontGrant", "S3 delivery returns the three CloudFront cookies with Domain and Secure", CloudFrontGrantAsync),
                Case("Media.AdminLibrary", "The admin library enumerates with filters, orderings, and search; summary, detail, edit, reprocess, and delete work", AdminLibraryAsync),
                Case("Media.AttachApi", "ValidateAttachableAsync, AttachAsync, DetachAsync, views per target, readiness, and attached visibility follow the contract", AttachApiAsync),
                Case("Media.RetainAndPublic", "Retained media moves to the retained folder and back; public copies are served anonymously and removed again", RetainAndPublicAsync),
                Case("Media.Cleanup", "The cleanup worker expires stale uploads and deletes detached media after the retention, never attached originals", CleanupAsync),
                Case("Media.LinkPreviewImages", "Link preview images are re-encoded to at most 1200 pixels and served from the file route", LinkPreviewAsync),
                Case("Media.MediaConvertWebhook", "The webhook verifies SNS signatures, confirms subscriptions, completes jobs idempotently, and rejects bad signatures", WebhookAsync),
                Case("Media.ConcurrentComplete", "Concurrent complete calls move the media to Processing exactly once", ConcurrentCompleteAsync)
            };
            TestCaseDescriptor ffmpeg = Case("Media.FfmpegHls", "With ffmpeg, a two second test video becomes an HLS master playlist served by the file route", FfmpegAsync);
            string? missing = MissingTool();
            cases.Add(missing == null ? ffmpeg : ffmpeg.WithSkip(missing + " is not on PATH; install ffmpeg (with ffprobe) to run the HLS case."));
            return new TestSuiteDescriptor(Id, "Media pipeline (" + TestEnvironment.Provider + ")", cases.ToArray());
        }

        #endregion

        #region Private-Methods

        private static TestCaseDescriptor Case(string caseId, string name, Func<HttpFixture, CancellationToken, Task> body)
        {
            return SuiteCases.Http(Id, caseId, name, body);
        }

        private static string? MissingTool()
        {
            foreach (string tool in new[] { "ffmpeg", "ffprobe" })
            {
                string[] paths = (Environment.GetEnvironmentVariable("PATH") ?? String.Empty).Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);
                if (!paths.Any(p => File.Exists(Path.Combine(p, tool)) || File.Exists(Path.Combine(p, tool + ".exe")))) return tool;
            }
            return null;
        }

        private static async Task ExpectErrorAsync(HttpFixture f, HttpMethod method, string path, string? bearer, object? body, HttpStatusCode status, string error, CancellationToken ct)
        {
            MediaHttpResult result = await MediaTestKit.JsonAsync(f, method, path, bearer, body, ct).ConfigureAwait(false);
            Check.Equal(status, result.Status, method + " " + path + " status (" + result.Text + ")");
            Check.Equal(error, result.Error, method + " " + path + " error");
        }

        private static async Task SessionRulesAsync(HttpFixture f, CancellationToken ct)
        {
            TestMember member = await Api.MemberAsync(f, ct).ConfigureAwait(false);
            string u = "/api/v1/media/uploads";
            MediaHttpResult missing = await MediaTestKit.JsonAsync(f, HttpMethod.Post, u, member.AccessToken, new { }, ct).ConfigureAwait(false);
            Check.Equal("ValidationFailed", missing.Error, "Empty body");
            JsonElement fields = missing.Json.GetProperty("fieldErrors");
            Check.True(fields.TryGetProperty("kind", out _) && fields.TryGetProperty("contentType", out _) && fields.TryGetProperty("sizeBytes", out _), "kind, contentType, sizeBytes field errors");
            await ExpectErrorAsync(f, HttpMethod.Post, u, member.AccessToken, new { kind = "Image", contentType = "image/gif", sizeBytes = 10 }, HttpStatusCode.UnsupportedMediaType, "UnsupportedMediaType", ct).ConfigureAwait(false);
            MediaHttpResult mismatch = await MediaTestKit.JsonAsync(f, HttpMethod.Post, u, member.AccessToken, new { kind = "Video", contentType = "image/jpeg", sizeBytes = 10, durationSeconds = 3 }, ct).ConfigureAwait(false);
            Check.Equal("ValidationFailed", mismatch.Error, "Kind and type mismatch");
            Check.True(mismatch.Json.GetProperty("fieldErrors").TryGetProperty("contentType", out _), "contentType field error");
            MediaHttpResult noDuration = await MediaTestKit.JsonAsync(f, HttpMethod.Post, u, member.AccessToken, new { kind = "Video", contentType = "video/mp4", sizeBytes = 10 }, ct).ConfigureAwait(false);
            Check.True(noDuration.Json.GetProperty("fieldErrors").TryGetProperty("durationSeconds", out _), "durationSeconds required for video");
            MediaHttpResult longName = await MediaTestKit.JsonAsync(f, HttpMethod.Post, u, member.AccessToken, new { kind = "Image", contentType = "image/png", sizeBytes = 10, fileName = new string('n', 256) }, ct).ConfigureAwait(false);
            Check.True(longName.Json.GetProperty("fieldErrors").TryGetProperty("fileName", out _), "fileName at most 255");
            await ExpectErrorAsync(f, HttpMethod.Post, u, member.AccessToken, new { kind = "Image", contentType = "image/png", sizeBytes = f.Settings.Media.MaxImageBytes + 1L }, HttpStatusCode.RequestEntityTooLarge, "TooLarge", ct).ConfigureAwait(false);
            await ExpectErrorAsync(f, HttpMethod.Post, u, member.AccessToken, new { kind = "Video", contentType = "video/mp4", sizeBytes = f.Settings.Media.MaxMemberVideoMegabytes * 1048576L + 1, durationSeconds = 3 }, HttpStatusCode.RequestEntityTooLarge, "TooLarge", ct).ConfigureAwait(false);
            await ExpectErrorAsync(f, HttpMethod.Post, u, member.AccessToken, new { kind = "Video", contentType = "video/quicktime", sizeBytes = 1000, durationSeconds = f.Settings.Media.MaxMemberVideoSeconds + 1 }, HttpStatusCode.BadRequest, "VideoTooLong", ct).ConfigureAwait(false);

            TestMember admin = await Api.AdminAsync(f, ct).ConfigureAwait(false);
            JsonElement adminVideo = await MediaTestKit.CreateSessionAsync(f, admin.AccessToken, "Video", "video/mp4", f.Settings.Media.MaxMemberVideoMegabytes * 1048576L + 1, f.Settings.Media.MaxMemberVideoSeconds + 100, ct).ConfigureAwait(false);
            Check.Equal("Multipart", Api.Str(adminVideo.GetProperty("upload"), "mode"), "Admin video over the member caps is accepted (uncapped) and multipart");
            JsonElement upload = adminVideo.GetProperty("upload");
            long partSize = upload.GetProperty("multipart").GetProperty("partSizeBytes").GetInt64();
            Check.Equal(16L * 1048576, partSize, "Part size is media.multipartPartMegabytes for this size");
            Check.True(upload.GetProperty("multipart").GetProperty("partCount").GetInt32() <= 1000, "At most 1000 parts");
            Check.Equal(31L * 1048576, MediaService.PartSize(30000L * 1048576 + 1000, 16), "Large files use bigger parts, rounded up to a whole MiB");

            JsonElement image = await MediaTestKit.CreateSessionAsync(f, member.AccessToken, "Image", "image/png", 1234, null, ct).ConfigureAwait(false);
            JsonElement media = image.GetProperty("media");
            Check.Equal("Uploading", Api.Str(media, "status"), "Status Uploading");
            Check.Equal("Single", Api.Str(image.GetProperty("upload"), "mode"), "Images are single");
            Check.True(image.GetProperty("upload").GetProperty("multipart").ValueKind == JsonValueKind.Null, "multipart null for single");
            Check.Equal("image/png", Api.Str(image.GetProperty("upload").GetProperty("single").GetProperty("headers"), "Content-Type"), "Single PUT header");
            Check.True(Api.Str(image.GetProperty("upload").GetProperty("single"), "url").StartsWith(f.BaseUrl + "/api/v1/media/local-uploads/med_", StringComparison.Ordinal), "Absolute local upload URL");
            Check.Equal(0, media.GetProperty("labels").GetArrayLength(), "labels []");
            Check.Equal(0, media.GetProperty("tags").EnumerateObject().Count(), "tags {}");
            Check.True(media.GetProperty("width").ValueKind == JsonValueKind.Null && media.GetProperty("image").ValueKind == JsonValueKind.Null, "No dimensions or variants before Ready");
        }

        private static async Task SingleImageAsync(HttpFixture f, CancellationToken ct)
        {
            TestMember member = await Api.MemberAsync(f, ct).ConfigureAwait(false);
            byte[] png = MediaTestKit.Png(600, 400);
            MediaHttpResult created = await MediaTestKit.JsonAsync(f, HttpMethod.Post, "/api/v1/media/uploads", member.AccessToken,
                new { kind = "Image", contentType = "image/png", sizeBytes = png.Length, labels = new[] { "topics", "Topics" }, tags = new Dictionary<string, string> { { "level", "beginner" } } }, ct).ConfigureAwait(false);
            Check.Equal(HttpStatusCode.Created, created.Status, "Created (" + created.Text + ")");
            string id = created.Json.GetProperty("media").GetProperty("id").GetString()!;
            Check.Equal(1, created.Json.GetProperty("media").GetProperty("labels").GetArrayLength(), "Labels de-duplicated");
            string url = created.Json.GetProperty("upload").GetProperty("single").GetProperty("url").GetString()!;
            MediaHttpResult put = await MediaTestKit.PutBytesAsync(f, url, png, "image/png", ct).ConfigureAwait(false);
            Check.Equal(HttpStatusCode.OK, put.Status, "PUT (" + put.Text + ")");
            string md5 = Convert.ToHexString(System.Security.Cryptography.MD5.HashData(png)).ToLowerInvariant();
            Check.Equal("\"" + md5 + "\"", put.Header("ETag"), "ETag is the quoted MD5");
            Check.True(File.Exists(MediaTestKit.LocalPath(f, "source", "in/" + id + "/original")), "Original under source/in/{id}/original");

            MediaHttpResult first = await MediaTestKit.JsonAsync(f, HttpMethod.Post, "/api/v1/media/" + id + "/complete", member.AccessToken, null, ct).ConfigureAwait(false);
            Check.Equal(HttpStatusCode.Accepted, first.Status, "First complete 202 (" + first.Text + ")");
            Check.Equal("Processing", Api.Str(first.Json, "status"), "Processing");
            MediaHttpResult again = await MediaTestKit.JsonAsync(f, HttpMethod.Post, "/api/v1/media/" + id + "/complete", member.AccessToken, null, ct).ConfigureAwait(false);
            Check.Equal(HttpStatusCode.OK, again.Status, "Second complete 200");

            List<MediaAttachment> settled = new List<MediaAttachment>();
            f.Services.Media.AddSettledHandler((m, t) => { lock (settled) settled.Add(m); return Task.CompletedTask; });
            Check.Equal(1, await MediaTestKit.ProcessAsync(f, ct).ConfigureAwait(false), "Worker processed one item");
            Check.Equal(1, settled.Count, "Settled handler ran once");
            JsonElement ready = await MediaTestKit.GetMediaAsync(f, member.AccessToken, id, ct).ConfigureAwait(false);
            Check.Equal("Ready", Api.Str(ready, "status"), "Ready");
            Check.Equal(600, ready.GetProperty("width").GetInt32(), "Width");
            Check.Equal(400, ready.GetProperty("height").GetInt32(), "Height");
            string blurhash = Api.Str(ready, "blurhash");
            Check.Equal(28, blurhash.Length, "4x3 blurhash");
            JsonElement thumb = ready.GetProperty("image").GetProperty("variants").GetProperty("thumb");
            Check.Equal(320, thumb.GetProperty("width").GetInt32(), "Thumb 320");
            Check.Equal(213, thumb.GetProperty("height").GetInt32(), "Thumb keeps the aspect ratio");
            Check.Equal("/api/v1/media/files/" + id + "/thumb.webp", Api.Str(thumb, "webpUrl"), "Root-relative webp URL");
            Check.Equal("/api/v1/media/files/" + id + "/thumb.jpg", Api.Str(thumb, "jpegUrl"), "Root-relative jpeg URL");
            JsonElement large = ready.GetProperty("image").GetProperty("variants").GetProperty("large");
            Check.Equal(600, large.GetProperty("width").GetInt32(), "Large never upscaled");
            Check.True(ready.GetProperty("video").ValueKind == JsonValueKind.Null, "video null for images");
            Check.True(ready.GetProperty("readyUtc").ValueKind == JsonValueKind.String, "readyUtc");
            Check.Equal("beginner", Api.Str(ready.GetProperty("tags"), "level"), "Tags kept");

            f.Settings.Media.PublicBaseUrl = "https://media.example.test";
            JsonElement absolute = await MediaTestKit.GetMediaAsync(f, member.AccessToken, id, ct).ConfigureAwait(false);
            Check.Equal("https://media.example.test/" + f.Property.Id + "/media/" + id + "/medium.webp", Api.Str(absolute.GetProperty("image").GetProperty("variants").GetProperty("medium"), "webpUrl"), "Absolute URL with media.publicBaseUrl");
            f.Settings.Media.PublicBaseUrl = String.Empty;
        }

        private static async Task LocalSignaturesAsync(HttpFixture f, CancellationToken ct)
        {
            TestMember member = await Api.MemberAsync(f, ct).ConfigureAwait(false);
            byte[] png = MediaTestKit.Png(40, 40);
            JsonElement session = await MediaTestKit.CreateSessionAsync(f, member.AccessToken, "Image", "image/png", png.Length, null, ct).ConfigureAwait(false);
            string id = session.GetProperty("media").GetProperty("id").GetString()!;
            string url = session.GetProperty("upload").GetProperty("single").GetProperty("url").GetString()!;
            string tampered = url.Substring(0, url.Length - 4) + (url.EndsWith("0000", StringComparison.Ordinal) ? "1111" : "0000");
            MediaHttpResult bad = await MediaTestKit.PutBytesAsync(f, tampered, png, "image/png", ct).ConfigureAwait(false);
            Check.Equal(HttpStatusCode.Forbidden, bad.Status, "Tampered signature");
            Check.Equal("InvalidSignature", bad.Error, "InvalidSignature");
            string path = LocalUploadSigner.SinglePath(id);
            string expired = f.Fakes.ObjectStore.Signer.BuildUrl(f.BaseUrl, path, DateTime.UtcNow.AddMinutes(-1));
            Check.Equal("InvalidSignature", (await MediaTestKit.PutBytesAsync(f, expired, png, "image/png", ct).ConfigureAwait(false)).Error, "Expired signature");
            string otherPath = f.Fakes.ObjectStore.Signer.BuildUrl(f.BaseUrl, LocalUploadSigner.SinglePath("med_other"), DateTime.UtcNow.AddMinutes(5));
            Check.Equal("InvalidSignature", (await MediaTestKit.PutBytesAsync(f, otherPath.Replace("med_other", id, StringComparison.Ordinal), png, "image/png", ct).ConfigureAwait(false)).Error, "Signature bound to its path");
            MediaHttpResult wrongType = await MediaTestKit.PutBytesAsync(f, url, png, "image/jpeg", ct).ConfigureAwait(false);
            Check.Equal(HttpStatusCode.UnsupportedMediaType, wrongType.Status, "Declared Content-Type enforced");
            MediaHttpResult tooBig = await MediaTestKit.PutBytesAsync(f, url, png.Concat(new byte[10]).ToArray(), "image/png", ct).ConfigureAwait(false);
            Check.Equal(HttpStatusCode.RequestEntityTooLarge, tooBig.Status, "Body above the declared size");
            Check.True(!File.Exists(MediaTestKit.LocalPath(f, "source", "in/" + id + "/original")), "Nothing kept after a refused body");

            string origin = f.Settings.Cors.AllowedOrigins.First(o => o != "*");
            MediaHttpResult preflight = await MediaTestKit.SendAsync(f, HttpMethod.Options, url, null, null, ct, new Dictionary<string, string>
            {
                { "Origin", origin }, { "Access-Control-Request-Method", "PUT" }, { "Access-Control-Request-Headers", "content-type" }
            }).ConfigureAwait(false);
            Check.True((int)preflight.Status < 300, "Preflight succeeds (" + preflight.Status + ")");
            Check.True((preflight.Header("Access-Control-Allow-Methods") ?? String.Empty).Contains("PUT", StringComparison.Ordinal), "Preflight allows PUT");
            ByteArrayContent content = new ByteArrayContent(png);
            content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/png");
            MediaHttpResult cors = await MediaTestKit.SendAsync(f, HttpMethod.Put, url, null, content, ct, new Dictionary<string, string> { { "Origin", origin } }).ConfigureAwait(false);
            Check.Equal(HttpStatusCode.OK, cors.Status, "Upload with Origin (" + cors.Text + ")");
            Check.Equal(origin, cors.Header("Access-Control-Allow-Origin"), "CORS origin on the local PUT");
            Check.True((cors.Header("Access-Control-Expose-Headers") ?? String.Empty).Contains("ETag", StringComparison.Ordinal), "ETag exposed to browsers");
        }

        private static async Task MultipartResumeAsync(HttpFixture f, CancellationToken ct)
        {
            f.Settings.Media.MultipartThresholdMegabytes = 5;
            f.Settings.Media.MultipartPartMegabytes = 5;
            TestMember member = await Api.MemberAsync(f, ct).ConfigureAwait(false);
            byte[] video = MediaTestKit.Mp4(4.5, 1920, 1080, true, 11L * 1048576);
            JsonElement session = await MediaTestKit.CreateSessionAsync(f, member.AccessToken, "Video", "video/quicktime", video.Length, 4.5, ct).ConfigureAwait(false);
            string id = session.GetProperty("media").GetProperty("id").GetString()!;
            JsonElement multipart = session.GetProperty("upload").GetProperty("multipart");
            Check.Equal("Multipart", Api.Str(session.GetProperty("upload"), "mode"), "Multipart above the threshold");
            Check.True(session.GetProperty("upload").GetProperty("single").ValueKind == JsonValueKind.Null, "single null");
            long partSize = multipart.GetProperty("partSizeBytes").GetInt64();
            int count = multipart.GetProperty("partCount").GetInt32();
            Check.Equal(5L * 1048576, partSize, "Part size 5 MiB");
            Check.Equal(3, count, "Three parts");
            List<string> urls = multipart.GetProperty("parts").EnumerateArray().Select(p => p.GetProperty("url").GetString()!).ToList();

            MediaHttpResult part1 = await MediaTestKit.PutBytesAsync(f, urls[0], video.Take((int)partSize).ToArray(), null, ct).ConfigureAwait(false);
            Check.Equal(HttpStatusCode.OK, part1.Status, "Part 1 (" + part1.Text + ")");
            Check.True(Directory.Exists(MediaTestKit.LocalPath(f, "video-source", "in/" + id + "/original.parts")), "Parts under video-source/in/{id}/");

            MediaHttpResult incomplete = await MediaTestKit.JsonAsync(f, HttpMethod.Post, "/api/v1/media/" + id + "/complete", member.AccessToken,
                new { parts = new[] { new { partNumber = 1, etag = part1.Header("ETag") } } }, ct).ConfigureAwait(false);
            Check.Equal(HttpStatusCode.Conflict, incomplete.Status, "Missing parts");
            Check.Equal("UploadIncomplete", incomplete.Error, "UploadIncomplete");

            MediaHttpResult resume = await MediaTestKit.JsonAsync(f, HttpMethod.Post, "/api/v1/media/" + id + "/upload-urls", member.AccessToken, null, ct).ConfigureAwait(false);
            Check.Equal(HttpStatusCode.OK, resume.Status, "upload-urls (" + resume.Text + ")");
            JsonElement uploaded = resume.Json.GetProperty("upload").GetProperty("uploadedParts");
            Check.Equal(1, uploaded.GetArrayLength(), "uploadedParts lists part 1");
            Check.Equal(part1.Header("ETag"), Api.Str(uploaded[0], "etag"), "uploadedParts etag");
            Check.Equal(partSize, uploaded[0].GetProperty("sizeBytes").GetInt64(), "uploadedParts size");
            List<string> fresh = resume.Json.GetProperty("upload").GetProperty("multipart").GetProperty("parts").EnumerateArray().Select(p => p.GetProperty("url").GetString()!).ToList();

            MediaHttpResult part2 = await MediaTestKit.PutBytesAsync(f, fresh[1], video.Skip((int)partSize).Take((int)partSize).ToArray(), null, ct).ConfigureAwait(false);
            MediaHttpResult part3 = await MediaTestKit.PutBytesAsync(f, fresh[2], video.Skip((int)partSize * 2).ToArray(), null, ct).ConfigureAwait(false);
            Check.Equal(HttpStatusCode.OK, part3.Status, "Part 3");
            MediaHttpResult badEtag = await MediaTestKit.JsonAsync(f, HttpMethod.Post, "/api/v1/media/" + id + "/complete", member.AccessToken, new
            {
                parts = new[] { new { partNumber = 1, etag = part1.Header("ETag") }, new { partNumber = 2, etag = (string?)"\"0000\"" }, new { partNumber = 3, etag = part3.Header("ETag") } }
            }, ct).ConfigureAwait(false);
            Check.Equal("UploadIncomplete", badEtag.Error, "Wrong ETag");
            MediaHttpResult done = await MediaTestKit.JsonAsync(f, HttpMethod.Post, "/api/v1/media/" + id + "/complete", member.AccessToken, new
            {
                parts = new[] { new { partNumber = 1, etag = part1.Header("ETag") }, new { partNumber = 2, etag = part2.Header("ETag") }, new { partNumber = 3, etag = part3.Header("ETag") } }
            }, ct).ConfigureAwait(false);
            Check.Equal(HttpStatusCode.Accepted, done.Status, "Complete (" + done.Text + ")");
            Check.Equal(4.5, done.Json.GetProperty("durationSeconds").GetDouble(), "Duration from mvhd");
            byte[] assembled = await File.ReadAllBytesAsync(MediaTestKit.LocalPath(f, "video-source", "in/" + id + "/original"), ct).ConfigureAwait(false);
            Check.True(assembled.SequenceEqual(video), "Parts concatenated in order");

            await MediaTestKit.ProcessAsync(f, ct).ConfigureAwait(false);
            JsonElement ready = await MediaTestKit.GetMediaAsync(f, member.AccessToken, id, ct).ConfigureAwait(false);
            Check.Equal("Ready", Api.Str(ready, "status"), "Ready (" + ready + ")");
            Check.Equal(1080, ready.GetProperty("width").GetInt32(), "Rotated track reports portrait width");
            Check.Equal(1920, ready.GetProperty("height").GetInt32(), "Rotated track reports portrait height");
            JsonElement videoView = ready.GetProperty("video");
            Check.Equal("/api/v1/media/files/" + id + "/original.mp4", Api.Str(videoView, "mp4Url"), "Passthrough mp4Url");
            Check.True(videoView.GetProperty("hlsUrl").ValueKind == JsonValueKind.Null && videoView.GetProperty("posterUrl").ValueKind == JsonValueKind.Null, "No HLS or poster in passthrough");

            MediaHttpResult range = await MediaTestKit.SendAsync(f, HttpMethod.Get, Api.Str(videoView, "mp4Url"), member.AccessToken, null, ct, new Dictionary<string, string> { { "Range", "bytes=0-15" } }).ConfigureAwait(false);
            Check.Equal(HttpStatusCode.PartialContent, range.Status, "206 for Range");
            Check.Equal(16, range.Bytes.Length, "16 bytes");
            Check.Equal("bytes 0-15/" + video.Length, range.Header("Content-Range"), "Content-Range");
            Check.Equal("video/mp4", range.Header("Content-Type"), "Content type");
        }

        private static async Task CompleteChecksAsync(HttpFixture f, CancellationToken ct)
        {
            TestMember member = await Api.MemberAsync(f, ct).ConfigureAwait(false);
            byte[] jpeg = MediaTestKit.PlainJpeg(64, 64);

            JsonElement s1 = await MediaTestKit.CreateSessionAsync(f, member.AccessToken, "Image", "image/jpeg", jpeg.Length, null, ct).ConfigureAwait(false);
            string id1 = s1.GetProperty("media").GetProperty("id").GetString()!;
            await ExpectErrorAsync(f, HttpMethod.Post, "/api/v1/media/" + id1 + "/complete", member.AccessToken, null, HttpStatusCode.Conflict, "UploadIncomplete", ct).ConfigureAwait(false);
            await MediaTestKit.PutBytesAsync(f, s1.GetProperty("upload").GetProperty("single").GetProperty("url").GetString()!, jpeg.Take(jpeg.Length - 5).ToArray(), "image/jpeg", ct).ConfigureAwait(false);
            await ExpectErrorAsync(f, HttpMethod.Post, "/api/v1/media/" + id1 + "/complete", member.AccessToken, null, HttpStatusCode.Conflict, "UploadIncomplete", ct).ConfigureAwait(false);
            Check.Equal("Uploading", Api.Str(await MediaTestKit.GetMediaAsync(f, member.AccessToken, id1, ct).ConfigureAwait(false), "status"), "Session stays Uploading after a size mismatch");

            string id2 = await MediaTestKit.UploadSingleAsync(f, member.AccessToken, "Image", "image/png", jpeg, ct, null, false).ConfigureAwait(false);
            await ExpectErrorAsync(f, HttpMethod.Post, "/api/v1/media/" + id2 + "/complete", member.AccessToken, null, HttpStatusCode.UnsupportedMediaType, "UnsupportedMediaType", ct).ConfigureAwait(false);
            JsonElement failed = await MediaTestKit.GetMediaAsync(f, member.AccessToken, id2, ct).ConfigureAwait(false);
            Check.Equal("Failed", Api.Str(failed, "status"), "Sniff mismatch fails the media");
            Check.Equal("UnsupportedFormat", Api.Str(failed, "failureReason"), "UnsupportedFormat");

            byte[] longVideo = MediaTestKit.Mp4(f.Settings.Media.MaxMemberVideoSeconds + 60, 640, 360, false, 4096);
            string id3 = await MediaTestKit.UploadSingleAsync(f, member.AccessToken, "Video", "video/mp4", longVideo, ct, 10, false).ConfigureAwait(false);
            await ExpectErrorAsync(f, HttpMethod.Post, "/api/v1/media/" + id3 + "/complete", member.AccessToken, null, HttpStatusCode.BadRequest, "VideoTooLong", ct).ConfigureAwait(false);
            Check.Equal("VideoTooLong", Api.Str(await MediaTestKit.GetMediaAsync(f, member.AccessToken, id3, ct).ConfigureAwait(false), "failureReason"), "VideoTooLong failure reason");

            byte[] noMoov = new byte[64];
            Array.Copy(new byte[] { 0, 0, 0, 0x18, (byte)'f', (byte)'t', (byte)'y', (byte)'p', (byte)'i', (byte)'s', (byte)'o', (byte)'m' }, noMoov, 12);
            string id4 = await MediaTestKit.UploadSingleAsync(f, member.AccessToken, "Video", "video/mp4", noMoov, ct, 3, false).ConfigureAwait(false);
            await ExpectErrorAsync(f, HttpMethod.Post, "/api/v1/media/" + id4 + "/complete", member.AccessToken, null, HttpStatusCode.UnsupportedMediaType, "UnsupportedMediaType", ct).ConfigureAwait(false);

            JsonElement s5 = await MediaTestKit.CreateSessionAsync(f, member.AccessToken, "Image", "image/jpeg", jpeg.Length, null, ct).ConfigureAwait(false);
            string id5 = s5.GetProperty("media").GetProperty("id").GetString()!;
            MediaAttachment row = (await f.Database.Media.ReadAsync(id5, ct).ConfigureAwait(false))!;
            row.UploadExpiresUtc = DateTime.UtcNow.AddMinutes(-1);
            await f.Database.Media.UpdateAsync(row, null, ct).ConfigureAwait(false);
            await ExpectErrorAsync(f, HttpMethod.Post, "/api/v1/media/" + id5 + "/upload-urls", member.AccessToken, null, HttpStatusCode.Gone, "UploadExpired", ct).ConfigureAwait(false);
            await ExpectErrorAsync(f, HttpMethod.Post, "/api/v1/media/" + id5 + "/complete", member.AccessToken, null, HttpStatusCode.OK, null!, ct).ConfigureAwait(false);
            Check.Equal("UploadExpired", Api.Str(await MediaTestKit.GetMediaAsync(f, member.AccessToken, id5, ct).ConfigureAwait(false), "failureReason"), "Expired sessions fail with UploadExpired");

            JsonElement s6 = await MediaTestKit.CreateSessionAsync(f, member.AccessToken, "Image", "image/jpeg", jpeg.Length, null, ct).ConfigureAwait(false);
            string id6 = s6.GetProperty("media").GetProperty("id").GetString()!;
            MediaAttachment row6 = (await f.Database.Media.ReadAsync(id6, ct).ConfigureAwait(false))!;
            row6.UploadExpiresUtc = DateTime.UtcNow.AddSeconds(-1);
            await f.Database.Media.UpdateAsync(row6, null, ct).ConfigureAwait(false);
            await ExpectErrorAsync(f, HttpMethod.Post, "/api/v1/media/" + id6 + "/complete", member.AccessToken, null, HttpStatusCode.Gone, "UploadExpired", ct).ConfigureAwait(false);

            TestMember other = await Api.MemberAsync(f, ct).ConfigureAwait(false);
            await ExpectErrorAsync(f, HttpMethod.Post, "/api/v1/media/" + id1 + "/complete", other.AccessToken, null, HttpStatusCode.NotFound, "NotFound", ct).ConfigureAwait(false);
        }

        private static async Task ImageProcessingAsync(HttpFixture f, CancellationToken ct)
        {
            TestMember member = await Api.MemberAsync(f, ct).ConfigureAwait(false);
            byte[] exif = MediaTestKit.ExifJpeg(400, 200, 6);
            Check.Equal(6, ExifOrientationReader.Read(exif), "Fixture carries orientation 6");
            Check.True(MediaTestKit.Contains(exif, Encoding.ASCII.GetBytes(MediaTestKit.ExifMarker)), "Fixture carries the location marker");
            string portrait = await MediaTestKit.UploadSingleAsync(f, member.AccessToken, "Image", "image/jpeg", exif, ct).ConfigureAwait(false);
            string wide = await MediaTestKit.UploadSingleAsync(f, member.AccessToken, "Image", "image/png", MediaTestKit.Png(3000, 1500), ct).ConfigureAwait(false);
            await MediaTestKit.ProcessAsync(f, ct).ConfigureAwait(false);

            JsonElement p = await MediaTestKit.GetMediaAsync(f, member.AccessToken, portrait, ct).ConfigureAwait(false);
            Check.Equal(200, p.GetProperty("width").GetInt32(), "Portrait width after orientation");
            Check.Equal(400, p.GetProperty("height").GetInt32(), "Portrait height after orientation");
            JsonElement pv = p.GetProperty("image").GetProperty("variants");
            Check.Equal(160, pv.GetProperty("thumb").GetProperty("width").GetInt32(), "Thumb width");
            Check.Equal(320, pv.GetProperty("thumb").GetProperty("height").GetInt32(), "Thumb long edge 320");
            Check.Equal(400, pv.GetProperty("medium").GetProperty("height").GetInt32(), "Medium never upscaled");
            byte[] marker = Encoding.ASCII.GetBytes(MediaTestKit.ExifMarker);
            foreach (string file in new[] { "thumb.jpg", "thumb.webp", "medium.jpg", "medium.webp", "large.jpg", "large.webp" })
            {
                byte[] output = await File.ReadAllBytesAsync(MediaTestKit.LocalPath(f, "images", "media/" + portrait + "/" + file), ct).ConfigureAwait(false);
                Check.True(!MediaTestKit.Contains(output, marker), file + " has no location marker");
                Check.True(!MediaTestKit.Contains(output, Encoding.ASCII.GetBytes("Exif\0\0")), file + " has no EXIF block");
                if (file.EndsWith(".jpg", StringComparison.Ordinal)) Check.Equal(1, ExifOrientationReader.Read(output), file + " has no orientation tag");
            }
            byte[] large = await File.ReadAllBytesAsync(MediaTestKit.LocalPath(f, "images", "media/" + portrait + "/large.jpg"), ct).ConfigureAwait(false);
            SKColor top = MediaTestKit.PixelAt(large, 0.5, 0.1);
            SKColor bottom = MediaTestKit.PixelAt(large, 0.5, 0.9);
            Check.True(top.Red > 200 && top.Blue < 60, "Rotated 90 clockwise: the left (red) half is on top (" + top + ")");
            Check.True(bottom.Blue > 200 && bottom.Red < 60, "The right (blue) half is at the bottom (" + bottom + ")");
            byte[] original = await File.ReadAllBytesAsync(MediaTestKit.LocalPath(f, "source", "in/" + portrait + "/original"), ct).ConfigureAwait(false);
            Check.True(original.SequenceEqual(exif), "The original is kept as received");

            JsonElement w = await MediaTestKit.GetMediaAsync(f, member.AccessToken, wide, ct).ConfigureAwait(false);
            JsonElement wv = w.GetProperty("image").GetProperty("variants");
            Check.Equal(2048, wv.GetProperty("large").GetProperty("width").GetInt32(), "Large 2048");
            Check.Equal(1024, wv.GetProperty("large").GetProperty("height").GetInt32(), "Large keeps the ratio");
            Check.Equal(1080, wv.GetProperty("medium").GetProperty("width").GetInt32(), "Medium 1080");
            Check.Equal(160, wv.GetProperty("thumb").GetProperty("height").GetInt32(), "Thumb 320x160");
            using (SKCodec codec = SKCodec.Create(new MemoryStream(await File.ReadAllBytesAsync(MediaTestKit.LocalPath(f, "images", "media/" + wide + "/medium.webp"), ct).ConfigureAwait(false))))
            {
                Check.Equal(SKEncodedImageFormat.Webp, codec.EncodedFormat, "WebP variant is WebP");
                Check.Equal(1080, codec.Info.Width, "WebP width");
            }

            f.Settings.Media.MaxImagePixels = 1000000;
            string huge = await MediaTestKit.UploadSingleAsync(f, member.AccessToken, "Image", "image/png", MediaTestKit.Png(1100, 1000), ct).ConfigureAwait(false);
            byte[] corrupt = MediaTestKit.PlainJpeg(50, 50).Take(20).ToArray();
            string broken = await MediaTestKit.UploadSingleAsync(f, member.AccessToken, "Image", "image/jpeg", corrupt, ct).ConfigureAwait(false);
            await MediaTestKit.ProcessAsync(f, ct).ConfigureAwait(false);
            Check.Equal("ImageTooLarge", Api.Str(await MediaTestKit.GetMediaAsync(f, member.AccessToken, huge, ct).ConfigureAwait(false), "failureReason"), "Pixel limit");
            Check.Equal("Corrupt", Api.Str(await MediaTestKit.GetMediaAsync(f, member.AccessToken, broken, ct).ConfigureAwait(false), "failureReason"), "Undecodable image");
        }

        private static async Task AltTextAsync(HttpFixture f, CancellationToken ct)
        {
            TestMember member = await Api.MemberAsync(f, ct).ConfigureAwait(false);
            JsonElement session = await MediaTestKit.CreateSessionAsync(f, member.AccessToken, "Image", "image/png", 100, null, ct).ConfigureAwait(false);
            string id = session.GetProperty("media").GetProperty("id").GetString()!;
            MediaHttpResult updated = await MediaTestKit.JsonAsync(f, HttpMethod.Put, "/api/v1/media/" + id, member.AccessToken, new { altText = "  A member   demonstrating  ", labels = new[] { "drills" } }, ct).ConfigureAwait(false);
            Check.Equal(HttpStatusCode.OK, updated.Status, "Alt text while Uploading (" + updated.Text + ")");
            Check.Equal("A member demonstrating", Api.Str(updated.Json, "altText"), "Alt text normalized");
            Check.Equal("drills", updated.Json.GetProperty("labels")[0].GetString(), "Labels replaced");
            MediaHttpResult tooLong = await MediaTestKit.JsonAsync(f, HttpMethod.Put, "/api/v1/media/" + id, member.AccessToken, new { altText = new string('a', 1001) }, ct).ConfigureAwait(false);
            Check.Equal("ValidationFailed", tooLong.Error, "Alt text at most 1000");
            Check.True(tooLong.Json.GetProperty("fieldErrors").TryGetProperty("altText", out _), "fieldErrors.altText");
            MediaHttpResult untouched = await MediaTestKit.JsonAsync(f, HttpMethod.Put, "/api/v1/media/" + id, member.AccessToken, new { tags = new Dictionary<string, string> { { "level", "advanced" } } }, ct).ConfigureAwait(false);
            Check.Equal("A member demonstrating", Api.Str(untouched.Json, "altText"), "Absent altText unchanged");

            TestMember admin = await Api.AdminAsync(f, ct).ConfigureAwait(false);
            MediaHttpResult staff = await MediaTestKit.JsonAsync(f, HttpMethod.Put, "/api/v1/admin/media/" + id, admin.AccessToken, new { labels = new[] { "drills", "staff:featured" } }, ct).ConfigureAwait(false);
            Check.Equal(HttpStatusCode.OK, staff.Status, "Admin edit (" + staff.Text + ")");
            MediaHttpResult memberEdit = await MediaTestKit.JsonAsync(f, HttpMethod.Put, "/api/v1/media/" + id, member.AccessToken, new { labels = new[] { "basics" } }, ct).ConfigureAwait(false);
            Check.True(memberEdit.Json.GetProperty("labels").EnumerateArray().All(l => !(l.GetString() ?? String.Empty).StartsWith("staff:", StringComparison.Ordinal)), "Members never see staff labels");
            JsonElement adminView = (await MediaTestKit.JsonAsync(f, HttpMethod.Get, "/api/v1/admin/media/" + id, admin.AccessToken, null, ct).ConfigureAwait(false)).Json.GetProperty("media");
            List<string> labels = adminView.GetProperty("labels").EnumerateArray().Select(l => l.GetString()!).ToList();
            Check.True(labels.Contains("staff:featured") && labels.Contains("basics"), "Staff entries survive a member replace (" + String.Join(",", labels) + ")");

            await f.Services.Media.AttachAsync(TargetTypeEnum.FeedPost, "fpo_alt", new[] { id }, ct).ConfigureAwait(false);
            MediaHttpResult attachedEdit = await MediaTestKit.JsonAsync(f, HttpMethod.Put, "/api/v1/media/" + id, member.AccessToken, new { altText = (string?)null }, ct).ConfigureAwait(false);
            Check.Equal(HttpStatusCode.OK, attachedEdit.Status, "Alt text after attachment");
            Check.True(attachedEdit.Json.GetProperty("altText").ValueKind == JsonValueKind.Null, "Null clears alt text");
            TestMember other = await Api.MemberAsync(f, ct).ConfigureAwait(false);
            await ExpectErrorAsync(f, HttpMethod.Put, "/api/v1/media/" + id, other.AccessToken, new { altText = "x" }, HttpStatusCode.NotFound, "NotFound", ct).ConfigureAwait(false);
        }

        private static async Task DeleteRulesAsync(HttpFixture f, CancellationToken ct)
        {
            TestMember member = await Api.MemberAsync(f, ct).ConfigureAwait(false);
            TestMember other = await Api.MemberAsync(f, ct).ConfigureAwait(false);
            string loose = await MediaTestKit.UploadSingleAsync(f, member.AccessToken, "Image", "image/png", MediaTestKit.Png(50, 50), ct).ConfigureAwait(false);
            string attached = await MediaTestKit.UploadSingleAsync(f, member.AccessToken, "Image", "image/png", MediaTestKit.Png(50, 50), ct).ConfigureAwait(false);
            string uploading = (await MediaTestKit.CreateSessionAsync(f, member.AccessToken, "Image", "image/png", 50, null, ct).ConfigureAwait(false)).GetProperty("media").GetProperty("id").GetString()!;
            await MediaTestKit.ProcessAsync(f, ct).ConfigureAwait(false);
            await f.Services.Media.AttachAsync(TargetTypeEnum.FeedPost, "fpo_del", new[] { attached }, ct).ConfigureAwait(false);

            await ExpectErrorAsync(f, HttpMethod.Delete, "/api/v1/media/" + loose, other.AccessToken, null, HttpStatusCode.NotFound, "NotFound", ct).ConfigureAwait(false);
            await ExpectErrorAsync(f, HttpMethod.Delete, "/api/v1/media/" + attached, member.AccessToken, null, HttpStatusCode.Forbidden, "Forbidden", ct).ConfigureAwait(false);
            MediaHttpResult deleted = await MediaTestKit.JsonAsync(f, HttpMethod.Delete, "/api/v1/media/" + loose, member.AccessToken, null, ct).ConfigureAwait(false);
            Check.Equal(HttpStatusCode.NoContent, deleted.Status, "Owner deletes unattached media");
            Check.True(!File.Exists(MediaTestKit.LocalPath(f, "source", "in/" + loose + "/original")), "Original deleted");
            Check.True(!Directory.Exists(MediaTestKit.LocalPath(f, "images", "media/" + loose)), "Variants deleted");
            await ExpectErrorAsync(f, HttpMethod.Get, "/api/v1/media/" + loose, member.AccessToken, null, HttpStatusCode.NotFound, "NotFound", ct).ConfigureAwait(false);
            Check.Equal(0, (await f.Database.Media.ReadVariantsAsync(loose, ct).ConfigureAwait(false)).Count, "Variant rows deleted");
            Check.Equal(HttpStatusCode.NoContent, (await MediaTestKit.JsonAsync(f, HttpMethod.Delete, "/api/v1/media/" + uploading, member.AccessToken, null, ct).ConfigureAwait(false)).Status, "Uploading sessions can be deleted");

            List<string> detached = new List<string>();
            f.Services.Media.AddDetachedHandler((type, target, t) => { lock (detached) detached.Add(type + ":" + target); return Task.CompletedTask; });
            TestMember admin = await Api.AdminAsync(f, ct).ConfigureAwait(false);
            MediaHttpResult adminDelete = await MediaTestKit.JsonAsync(f, HttpMethod.Delete, "/api/v1/media/" + attached, admin.AccessToken, null, ct).ConfigureAwait(false);
            Check.Equal(HttpStatusCode.NoContent, adminDelete.Status, "SystemAdmin deletes attached media");
            Check.Equal("FeedPost:fpo_del", detached.Single(), "Detached handler called with the target");
            Check.Equal(0, (await f.Services.Media.ListViewsForTargetsAsync(TargetTypeEnum.FeedPost, new[] { "fpo_del" }, false, false, ct).ConfigureAwait(false)).Count, "Target has no media left");
        }

        private static async Task QuotaAsync(HttpFixture f, CancellationToken ct)
        {
            f.Settings.Media.MemberDailyUploadMegabytes = 1;
            TestMember member = await Api.MemberAsync(f, ct).ConfigureAwait(false);
            await MediaTestKit.CreateSessionAsync(f, member.AccessToken, "Image", "image/png", 700000, null, ct).ConfigureAwait(false);
            MediaHttpResult over = await MediaTestKit.JsonAsync(f, HttpMethod.Post, "/api/v1/media/uploads", member.AccessToken, new { kind = "Image", contentType = "image/png", sizeBytes = 700000 }, ct).ConfigureAwait(false);
            Check.Equal(HttpStatusCode.TooManyRequests, over.Status, "Daily quota");
            int retry = Int32.Parse(over.Header("Retry-After") ?? "0", System.Globalization.CultureInfo.InvariantCulture);
            Check.True(retry > 0 && retry <= 86400, "Retry-After until UTC midnight (" + retry + ")");
            TestMember admin = await Api.AdminAsync(f, ct).ConfigureAwait(false);
            await MediaTestKit.CreateSessionAsync(f, admin.AccessToken, "Image", "image/png", 700000, null, ct).ConfigureAwait(false);
            await MediaTestKit.CreateSessionAsync(f, admin.AccessToken, "Image", "image/png", 700000, null, ct).ConfigureAwait(false);

            f.Settings.Media.MemberDailyUploadMegabytes = 0;
            f.Settings.Media.UploadsPerHour = 2;
            TestMember second = await Api.MemberAsync(f, ct).ConfigureAwait(false);
            await MediaTestKit.CreateSessionAsync(f, second.AccessToken, "Image", "image/png", 10, null, ct).ConfigureAwait(false);
            await MediaTestKit.CreateSessionAsync(f, second.AccessToken, "Image", "image/png", 10, null, ct).ConfigureAwait(false);
            MediaHttpResult hourly = await MediaTestKit.JsonAsync(f, HttpMethod.Post, "/api/v1/media/uploads", second.AccessToken, new { kind = "Image", contentType = "image/png", sizeBytes = 10 }, ct).ConfigureAwait(false);
            Check.Equal(HttpStatusCode.TooManyRequests, hourly.Status, "Uploads per hour");
            Check.True(hourly.Header("Retry-After") != null, "Retry-After on the hourly limit");

            f.Settings.RateLimits.MemberReadsPerMinute = 10;
            string id = (await MediaTestKit.CreateSessionAsync(f, member.AccessToken, "Image", "image/png", 10, null, ct).ConfigureAwait(false)).GetProperty("media").GetProperty("id").GetString()!;
            MediaHttpResult last = new MediaHttpResult();
            for (int i = 0; i < 11; i++) last = await MediaTestKit.JsonAsync(f, HttpMethod.Get, "/api/v1/media/" + id, member.AccessToken, null, ct).ConfigureAwait(false);
            Check.Equal(HttpStatusCode.TooManyRequests, last.Status, "Member reads per minute");
            Check.True(last.Header("Retry-After") != null, "Retry-After on reads");
        }

        private static async Task PermissionsAsync(HttpFixture f, CancellationToken ct)
        {
            object image = new { kind = "Image", contentType = "image/png", sizeBytes = 10 };
            object video = new { kind = "Video", contentType = "video/mp4", sizeBytes = 10, durationSeconds = 2 };
            TestMember noMedia = await Api.MemberAsync(f, ct).ConfigureAwait(false);
            await f.Database.UserPermissions.UpsertAsync(new UserPermission { UserId = noMedia.Id, CanPost = true, CanPostMedia = false }, ct).ConfigureAwait(false);
            await ExpectErrorAsync(f, HttpMethod.Post, "/api/v1/media/uploads", noMedia.AccessToken, image, HttpStatusCode.Forbidden, "MediaNotAllowed", ct).ConfigureAwait(false);

            TestMember noPost = await Api.MemberAsync(f, ct).ConfigureAwait(false);
            await f.Database.UserPermissions.UpsertAsync(new UserPermission { UserId = noPost.Id, CanPost = false, CanPostMedia = true }, ct).ConfigureAwait(false);
            await ExpectErrorAsync(f, HttpMethod.Post, "/api/v1/media/uploads", noPost.AccessToken, image, HttpStatusCode.Forbidden, "PostingNotAllowed", ct).ConfigureAwait(false);

            TestMember noVideo = await Api.MemberAsync(f, ct).ConfigureAwait(false);
            await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/admin/members/" + noVideo.Id + "/flags", HttpFixture.AdminToken, new { canUploadVideo = false }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            await ExpectErrorAsync(f, HttpMethod.Post, "/api/v1/media/uploads", noVideo.AccessToken, video, HttpStatusCode.Forbidden, "VideoUploadNotAllowed", ct).ConfigureAwait(false);
            await MediaTestKit.CreateSessionAsync(f, noVideo.AccessToken, "Image", "image/png", 10, null, ct).ConfigureAwait(false);

            TestMember muted = await Api.MemberAsync(f, ct).ConfigureAwait(false);
            await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/admin/members/" + muted.Id + "/mute", HttpFixture.AdminToken, new { untilUtc = DateTime.UtcNow.AddHours(2).ToString("o"), reason = "test" }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            await ExpectErrorAsync(f, HttpMethod.Post, "/api/v1/media/uploads", muted.AccessToken, image, HttpStatusCode.Forbidden, "Muted", ct).ConfigureAwait(false);

            TestMember lapsed = await Api.RegisterAsync(f, ct).ConfigureAwait(false);
            MediaHttpResult nonMember = await MediaTestKit.JsonAsync(f, HttpMethod.Post, "/api/v1/media/uploads", lapsed.AccessToken, image, ct).ConfigureAwait(false);
            Check.Equal(HttpStatusCode.PaymentRequired, nonMember.Status, "Member group required (402)");

            TestMember admin = await Api.AdminAsync(f, ct).ConfigureAwait(false);
            await f.Database.UserPermissions.UpsertAsync(new UserPermission { UserId = admin.Id, CanPost = false, CanPostMedia = false }, ct).ConfigureAwait(false);
            await MediaTestKit.CreateSessionAsync(f, admin.AccessToken, "Video", "video/mp4", 10, 2, ct).ConfigureAwait(false);
        }

        private static async Task GrantAndFilesAsync(HttpFixture f, CancellationToken ct)
        {
            TestMember member = await Api.MemberAsync(f, ct).ConfigureAwait(false);
            string id = await MediaTestKit.UploadSingleAsync(f, member.AccessToken, "Image", "image/png", MediaTestKit.Png(500, 300), ct).ConfigureAwait(false);
            await MediaTestKit.ProcessAsync(f, ct).ConfigureAwait(false);

            MediaHttpResult grant = await MediaTestKit.JsonAsync(f, HttpMethod.Post, "/api/v1/media/grant", member.AccessToken, null, ct).ConfigureAwait(false);
            Check.Equal(HttpStatusCode.OK, grant.Status, "Grant (" + grant.Text + ")");
            Check.Equal("Local", Api.Str(grant.Json, "mode"), "Local mode");
            JsonElement cookie = grant.Json.GetProperty("cookies")[0];
            Check.Equal("tk_media", Api.Str(cookie, "name"), "tk_media");
            Check.Equal("/api/v1/media/files", Api.Str(cookie, "path"), "Path");
            Check.True(cookie.GetProperty("domain").ValueKind == JsonValueKind.Null, "No Domain");
            DateTime expires = cookie.GetProperty("expiresUtc").GetDateTime().ToUniversalTime();
            Check.True(Math.Abs((expires - DateTime.UtcNow.AddMinutes(f.Settings.Media.GrantMinutes)).TotalMinutes) < 2, "Expiry from media.grantMinutes");
            string setCookie = grant.HeaderValues("Set-Cookie").Single();
            Check.True(setCookie.StartsWith("tk_media=" + Api.Str(cookie, "value") + ";", StringComparison.Ordinal), "Set-Cookie carries the value");
            Check.True(setCookie.Contains("Path=/api/v1/media/files", StringComparison.Ordinal) && setCookie.Contains("HttpOnly", StringComparison.Ordinal) && setCookie.Contains("SameSite=Lax", StringComparison.Ordinal), "Cookie attributes (" + setCookie + ")");
            Check.True(!setCookie.Contains("Secure", StringComparison.Ordinal), "No Secure over plain http on loopback");
            Check.True(!setCookie.Contains("Domain=", StringComparison.Ordinal), "No Domain attribute");

            string file = "/api/v1/media/files/" + id + "/medium.jpg";
            MediaHttpResult anonymous = await MediaTestKit.SendAsync(f, HttpMethod.Get, file, null, null, ct).ConfigureAwait(false);
            Check.Equal(HttpStatusCode.Forbidden, anonymous.Status, "403 without credentials");
            Check.Equal("InvalidSignature", anonymous.Error, "InvalidSignature");
            MediaHttpResult forged = await MediaTestKit.SendAsync(f, HttpMethod.Get, file, null, null, ct, new Dictionary<string, string> { { "Cookie", "tk_media=" + member.AccessToken } }).ConfigureAwait(false);
            Check.Equal(HttpStatusCode.Forbidden, forged.Status, "An access token is not a media cookie");
            MediaHttpResult withCookie = await MediaTestKit.SendAsync(f, HttpMethod.Get, file, null, null, ct, new Dictionary<string, string> { { "Cookie", "other=1; tk_media=" + Api.Str(cookie, "value") } }).ConfigureAwait(false);
            Check.Equal(HttpStatusCode.OK, withCookie.Status, "Plain Cookie header (mobile)");
            Check.Equal("image/jpeg", withCookie.Header("Content-Type"), "Content type");
            Check.True(new[] { "private", "max-age=31536000", "immutable" }.All(t => (String.Join(",", withCookie.HeaderValues("Cache-Control"))).Contains(t, StringComparison.Ordinal)), "Cache-Control private, max-age=31536000, immutable");
            Check.Equal("nosniff", withCookie.Header("X-Content-Type-Options"), "nosniff");
            Check.Equal("bytes", withCookie.Header("Accept-Ranges"), "Accept-Ranges");
            Check.True(withCookie.Bytes.Length > 100 && withCookie.Bytes[0] == 0xFF && withCookie.Bytes[1] == 0xD8, "JPEG bytes");

            System.Net.CookieContainer jar = new System.Net.CookieContainer();
            jar.SetCookies(new Uri(f.BaseUrl), setCookie);
            using (HttpClientHandler handler = new HttpClientHandler { CookieContainer = jar, UseCookies = true })
            using (HttpClient browser = new HttpClient(handler) { BaseAddress = new Uri(f.BaseUrl) })
            using (HttpResponseMessage response = await browser.GetAsync(file, ct).ConfigureAwait(false))
            {
                Check.Equal(HttpStatusCode.OK, response.StatusCode, "Browser-style cookie jar");
            }

            MediaHttpResult range = await MediaTestKit.SendAsync(f, HttpMethod.Get, file, null, null, ct, new Dictionary<string, string> { { "Cookie", "tk_media=" + Api.Str(cookie, "value") }, { "Range", "bytes=10-" } }).ConfigureAwait(false);
            Check.Equal(HttpStatusCode.PartialContent, range.Status, "206");
            Check.Equal(withCookie.Bytes.Length - 10, range.Bytes.Length, "Open-ended range");
            MediaHttpResult unsatisfiable = await MediaTestKit.SendAsync(f, HttpMethod.Get, file, member.AccessToken, null, ct, new Dictionary<string, string> { { "Range", "bytes=999999-" } }).ConfigureAwait(false);
            Check.Equal(HttpStatusCode.RequestedRangeNotSatisfiable, unsatisfiable.Status, "416");
            MediaHttpResult bearer = await MediaTestKit.SendAsync(f, HttpMethod.Get, "/api/v1/media/files/" + id + "/thumb.webp", member.AccessToken, null, ct).ConfigureAwait(false);
            Check.Equal(HttpStatusCode.OK, bearer.Status, "Bearer access token of a member");
            Check.Equal("image/webp", bearer.Header("Content-Type"), "WebP content type");
            TestMember lapsed = await Api.RegisterAsync(f, ct).ConfigureAwait(false);
            Check.Equal(HttpStatusCode.Forbidden, (await MediaTestKit.SendAsync(f, HttpMethod.Get, file, lapsed.AccessToken, null, ct).ConfigureAwait(false)).Status, "Bearer without a membership");
            Check.Equal(HttpStatusCode.NotFound, (await MediaTestKit.SendAsync(f, HttpMethod.Get, "/api/v1/media/files/" + id + "/..%2Fsecret", member.AccessToken, null, ct).ConfigureAwait(false)).Status, "Traversal refused");
            Check.Equal(HttpStatusCode.NotFound, (await MediaTestKit.SendAsync(f, HttpMethod.Get, "/api/v1/media/files/" + id + "/original", member.AccessToken, null, ct).ConfigureAwait(false)).Status, "Originals are never served");

            MediaHttpResult batch = await MediaTestKit.JsonAsync(f, HttpMethod.Get, "/api/v1/media?ids=" + id + ",med_missing", member.AccessToken, null, ct).ConfigureAwait(false);
            Check.Equal(1, batch.Json.GetProperty("objects").GetArrayLength(), "Batch omits ids the caller cannot see");
            string many = String.Join(",", Enumerable.Range(0, 21).Select(i => "med_" + i));
            Check.Equal("ValidationFailed", (await MediaTestKit.JsonAsync(f, HttpMethod.Get, "/api/v1/media?ids=" + many, member.AccessToken, null, ct).ConfigureAwait(false)).Error, "At most 20 ids");

            f.Settings.RateLimits.MediaGrantsPerHour = 1;
            TestMember fresh = await Api.MemberAsync(f, ct).ConfigureAwait(false);
            await MediaTestKit.JsonAsync(f, HttpMethod.Post, "/api/v1/media/grant", fresh.AccessToken, null, ct).ConfigureAwait(false);
            MediaHttpResult limited = await MediaTestKit.JsonAsync(f, HttpMethod.Post, "/api/v1/media/grant", fresh.AccessToken, null, ct).ConfigureAwait(false);
            Check.Equal(HttpStatusCode.TooManyRequests, limited.Status, "rateLimits.mediaGrantsPerHour");
        }

        private static async Task CloudFrontGrantAsync(HttpFixture f, CancellationToken ct)
        {
            using (System.Security.Cryptography.RSA rsa = System.Security.Cryptography.RSA.Create(2048))
            {
                f.Settings.Media.PublicBaseUrl = "https://media.example.test";
                f.Settings.Media.CookieDomain = "example.test";
                f.Settings.Media.CloudFrontKeyPairId = "KTESTKEYPAIR";
                f.Settings.Media.CloudFrontPrivateKeyPem = rsa.ExportPkcs8PrivateKeyPem();
                f.Fakes.Set.MediaDelivery = new CloudFrontCookieSigner(f.Settings.Media, f.Property.Id);
                TestMember member = await Api.MemberAsync(f, ct).ConfigureAwait(false);
                MediaHttpResult grant = await MediaTestKit.JsonAsync(f, HttpMethod.Post, "/api/v1/media/grant", member.AccessToken, null, ct).ConfigureAwait(false);
                Check.Equal(HttpStatusCode.OK, grant.Status, "Grant (" + grant.Text + ")");
                Check.Equal("CloudFront", Api.Str(grant.Json, "mode"), "CloudFront mode");
                List<string> names = grant.Json.GetProperty("cookies").EnumerateArray().Select(c => Api.Str(c, "name")).ToList();
                Check.True(names.SequenceEqual(new[] { "CloudFront-Policy", "CloudFront-Signature", "CloudFront-Key-Pair-Id" }), "Three cookies");
                List<string> headers = grant.HeaderValues("Set-Cookie").SelectMany(h => h.Split(", CloudFront-", StringSplitOptions.None)).ToList();
                Check.Equal(3, headers.Count, "Three Set-Cookie values (" + String.Join(" | ", grant.HeaderValues("Set-Cookie")) + ")");
                Check.True(headers.All(h => h.Contains("Domain=example.test", StringComparison.Ordinal) && h.Contains("Secure", StringComparison.Ordinal) && h.Contains("HttpOnly", StringComparison.Ordinal)), "Domain, Secure, HttpOnly");
                f.Settings.Media.CloudFrontPrivateKeyPem = String.Empty;
                MediaHttpResult unconfigured = await MediaTestKit.JsonAsync(f, HttpMethod.Post, "/api/v1/media/grant", member.AccessToken, null, ct).ConfigureAwait(false);
                Check.Equal(HttpStatusCode.ServiceUnavailable, unconfigured.Status, "503 without a signing key");
                Check.Equal("IntegrationNotConfigured", unconfigured.Error, "IntegrationNotConfigured");
            }
        }

        private static async Task AdminLibraryAsync(HttpFixture f, CancellationToken ct)
        {
            TestMember member = await Api.MemberAsync(f, ct).ConfigureAwait(false);
            TestMember admin = await Api.AdminAsync(f, ct).ConfigureAwait(false);
            string small = await MediaTestKit.UploadSingleAsync(f, member.AccessToken, "Image", "image/png", MediaTestKit.Png(30, 30), ct).ConfigureAwait(false);
            string big = await MediaTestKit.UploadSingleAsync(f, member.AccessToken, "Image", "image/png", MediaTestKit.Png(900, 900), ct).ConfigureAwait(false);
            string video = await MediaTestKit.UploadSingleAsync(f, admin.AccessToken, "Video", "video/mp4", MediaTestKit.Mp4(2, 640, 360, false, 5000), ct, 2).ConfigureAwait(false);
            await MediaTestKit.ProcessAsync(f, ct).ConfigureAwait(false);
            await f.Services.Media.AttachAsync(TargetTypeEnum.FeedPost, "fpo_lib", new[] { big }, ct).ConfigureAwait(false);

            MediaHttpResult all = await MediaTestKit.JsonAsync(f, HttpMethod.Get, "/api/v1/admin/media", admin.AccessToken, null, ct).ConfigureAwait(false);
            Check.Equal(HttpStatusCode.OK, all.Status, "List (" + all.Text + ")");
            Check.Equal(3L, all.Json.GetProperty("totalRecords").GetInt64(), "Three items");
            Check.Equal(video, Api.Str(all.Json.GetProperty("objects")[0].GetProperty("media"), "id"), "CreatedDescending by default");
            JsonElement item = all.Json.GetProperty("objects").EnumerateArray().First(o => Api.Str(o.GetProperty("media"), "id") == big);
            Check.Equal("FeedPost", Api.Str(item.GetProperty("attachedTo"), "targetType"), "attachedTo");
            Check.True(item.GetProperty("storedBytes").GetInt64() > item.GetProperty("media").GetProperty("sizeBytes").GetInt64(), "storedBytes include variants");
            Check.Equal("upload.bin", Api.Str(item, "fileName"), "fileName");
            Check.Equal(member.Id, Api.Str(item.GetProperty("owner"), "id"), "owner summary");

            MediaHttpResult videos = await MediaTestKit.JsonAsync(f, HttpMethod.Get, "/api/v1/admin/media?kind=Video", admin.AccessToken, null, ct).ConfigureAwait(false);
            Check.Equal(1L, videos.Json.GetProperty("totalRecords").GetInt64(), "kind filter");
            MediaHttpResult unattached = await MediaTestKit.JsonAsync(f, HttpMethod.Post, "/api/v1/admin/media/enumerate", admin.AccessToken,
                new { filters = new { attached = false, ownerUserId = member.Id }, ordering = "SizeAscending" }, ct).ConfigureAwait(false);
            Check.Equal(HttpStatusCode.OK, unattached.Status, "Enumerate (" + unattached.Text + ")");
            Check.Equal(small, Api.Str(unattached.Json.GetProperty("objects")[0].GetProperty("media"), "id"), "attached=false and ownerUserId filters");
            MediaHttpResult bySize = await MediaTestKit.JsonAsync(f, HttpMethod.Post, "/api/v1/admin/media/enumerate", admin.AccessToken, new { ordering = "SizeDescending", maxResults = 1 }, ct).ConfigureAwait(false);
            Check.True(bySize.Json.GetProperty("continuationToken").ValueKind == JsonValueKind.String, "Continuation token with maxResults 1");
            HashSet<string> seen = new HashSet<string>();
            string? next = null;
            for (int page = 0; page < 5; page++)
            {
                MediaHttpResult p = await MediaTestKit.JsonAsync(f, HttpMethod.Post, "/api/v1/admin/media/enumerate", admin.AccessToken, new { ordering = "SizeDescending", maxResults = 1, continuationToken = next }, ct).ConfigureAwait(false);
                foreach (JsonElement o in p.Json.GetProperty("objects").EnumerateArray()) Check.True(seen.Add(Api.Str(o.GetProperty("media"), "id")), "No duplicates across pages");
                if (p.Json.GetProperty("endOfResults").GetBoolean()) break;
                next = p.Json.GetProperty("continuationToken").GetString();
            }
            Check.Equal(3, seen.Count, "Every item once");
            Check.Equal("ValidationFailed", (await MediaTestKit.JsonAsync(f, HttpMethod.Get, "/api/v1/admin/media?ordering=NameAscending", admin.AccessToken, null, ct).ConfigureAwait(false)).Error, "Unknown ordering");
            Check.Equal("ValidationFailed", (await MediaTestKit.JsonAsync(f, HttpMethod.Get, "/api/v1/admin/media?status=Done", admin.AccessToken, null, ct).ConfigureAwait(false)).Error, "Bad status filter");
            MediaHttpResult window = await MediaTestKit.JsonAsync(f, HttpMethod.Get, "/api/v1/admin/media?createdAfterUtc=" + Uri.EscapeDataString(DateTime.UtcNow.AddMinutes(5).ToString("o")), admin.AccessToken, null, ct).ConfigureAwait(false);
            Check.Equal(0L, window.Json.GetProperty("totalRecords").GetInt64(), "createdAfterUtc window");
            await MediaTestKit.JsonAsync(f, HttpMethod.Put, "/api/v1/admin/media/" + small, admin.AccessToken, new { altText = "Unique searchable caption" }, ct).ConfigureAwait(false);
            MediaHttpResult search = await MediaTestKit.JsonAsync(f, HttpMethod.Get, "/api/v1/admin/media?search=searchable", admin.AccessToken, null, ct).ConfigureAwait(false);
            Check.Equal(1L, search.Json.GetProperty("totalRecords").GetInt64(), "Search over alt text");
            Check.Equal(HttpStatusCode.Forbidden, (await MediaTestKit.JsonAsync(f, HttpMethod.Get, "/api/v1/admin/media", member.AccessToken, null, ct).ConfigureAwait(false)).Status, "Admin only");

            MediaHttpResult summary = await MediaTestKit.JsonAsync(f, HttpMethod.Get, "/api/v1/admin/media/summary", admin.AccessToken, null, ct).ConfigureAwait(false);
            Check.Equal(3L, summary.Json.GetProperty("totalCount").GetInt64(), "Summary count");
            Check.Equal(2L, summary.Json.GetProperty("byKind").GetProperty("Image").GetInt64(), "byKind");
            Check.Equal(3L, summary.Json.GetProperty("byStatus").GetProperty("Ready").GetInt64(), "byStatus");
            Check.Equal(0L, summary.Json.GetProperty("byStatus").GetProperty("Failed").GetInt64(), "Every status key present");

            MediaHttpResult detail = await MediaTestKit.JsonAsync(f, HttpMethod.Get, "/api/v1/admin/media/" + big, admin.AccessToken, null, ct).ConfigureAwait(false);
            Check.Equal("skia", Api.Str(detail.Json, "processor"), "processor");
            Check.Equal(1, detail.Json.GetProperty("attempts").GetInt32(), "attempts");
            Check.Equal(6, detail.Json.GetProperty("variants").GetArrayLength(), "Six variants");
            Check.Equal("NotReprocessable", (await MediaTestKit.JsonAsync(f, HttpMethod.Post, "/api/v1/admin/media/" + big + "/reprocess", admin.AccessToken, null, ct).ConfigureAwait(false)).Error, "Ready items cannot be reprocessed");

            MediaAttachment row = (await f.Database.Media.ReadAsync(small, ct).ConfigureAwait(false))!;
            row.Status = MediaStatusEnum.Failed;
            row.FailureReason = MediaFailureReasonEnum.ProcessingError;
            await f.Database.Media.UpdateAsync(row, null, ct).ConfigureAwait(false);
            MediaHttpResult reprocess = await MediaTestKit.JsonAsync(f, HttpMethod.Post, "/api/v1/admin/media/" + small + "/reprocess", admin.AccessToken, null, ct).ConfigureAwait(false);
            Check.Equal(HttpStatusCode.OK, reprocess.Status, "Reprocess (" + reprocess.Text + ")");
            Check.Equal("Processing", Api.Str(reprocess.Json.GetProperty("media"), "status"), "Back to Processing");
            await MediaTestKit.ProcessAsync(f, ct).ConfigureAwait(false);
            Check.Equal("Ready", Api.Str(await MediaTestKit.GetMediaAsync(f, member.AccessToken, small, ct).ConfigureAwait(false), "status"), "Ready again");

            Check.Equal(HttpStatusCode.NoContent, (await MediaTestKit.JsonAsync(f, HttpMethod.Delete, "/api/v1/admin/media/" + big, admin.AccessToken, null, ct).ConfigureAwait(false)).Status, "Admin delete");
            Check.Equal(HttpStatusCode.NotFound, (await MediaTestKit.JsonAsync(f, HttpMethod.Get, "/api/v1/admin/media/" + big, admin.AccessToken, null, ct).ConfigureAwait(false)).Status, "Gone");
            Check.Equal(HttpStatusCode.NotFound, (await MediaTestKit.JsonAsync(f, HttpMethod.Delete, "/api/v1/admin/media/med_nothing", admin.AccessToken, null, ct).ConfigureAwait(false)).Status, "Unknown id");
        }

        private static async Task AttachApiAsync(HttpFixture f, CancellationToken ct)
        {
            TestMember member = await Api.MemberAsync(f, ct).ConfigureAwait(false);
            TestMember other = await Api.MemberAsync(f, ct).ConfigureAwait(false);
            string a = await MediaTestKit.UploadSingleAsync(f, member.AccessToken, "Image", "image/png", MediaTestKit.Png(40, 40), ct).ConfigureAwait(false);
            string b = await MediaTestKit.UploadSingleAsync(f, member.AccessToken, "Image", "image/png", MediaTestKit.Png(40, 40), ct).ConfigureAwait(false);
            string v1 = await MediaTestKit.UploadSingleAsync(f, member.AccessToken, "Video", "video/mp4", MediaTestKit.Mp4(2, 640, 360, false, 3000), ct, 2).ConfigureAwait(false);
            string v2 = await MediaTestKit.UploadSingleAsync(f, member.AccessToken, "Video", "video/mp4", MediaTestKit.Mp4(2, 640, 360, false, 3000), ct, 2).ConfigureAwait(false);
            string uploading = (await MediaTestKit.CreateSessionAsync(f, member.AccessToken, "Image", "image/png", 10, null, ct).ConfigureAwait(false)).GetProperty("media").GetProperty("id").GetString()!;
            string theirs = await MediaTestKit.UploadSingleAsync(f, other.AccessToken, "Image", "image/png", MediaTestKit.Png(40, 40), ct).ConfigureAwait(false);
            string processing = await MediaTestKit.UploadSingleAsync(f, member.AccessToken, "Image", "image/png", MediaTestKit.Png(40, 40), ct).ConfigureAwait(false);
            MediaService media = f.Services.Media;

            ContentActor actor = await f.Services.Actors.ForUserAsync(member.Id, ct).ConfigureAwait(false);
            Check.Equal(0, (await media.ValidateAttachableAsync(actor, new List<string>(), 4, 1, null, null, ct).ConfigureAwait(false)).Count, "No media, no checks");
            List<MediaAttachment> ok = await media.ValidateAttachableAsync(actor, new[] { b, processing, a }, 4, 1, null, null, ct).ConfigureAwait(false);
            Check.True(ok.Select(m => m.Id).SequenceEqual(new[] { b, processing, a }), "Returned in order; Processing is attachable");
            ApiErrorAssert(await Check.ThrowsAsync<TrellisKit.Core.Exceptions.ApiErrorException>(() => media.ValidateAttachableAsync(actor, new[] { a, b }, 1, 1, null, null, ct), "Too many").ConfigureAwait(false), 400, "MediaLimitExceeded");
            TrellisKit.Core.Exceptions.ApiErrorException notAttachable = await Check.ThrowsAsync<TrellisKit.Core.Exceptions.ApiErrorException>(() => media.ValidateAttachableAsync(actor, new[] { a, theirs, uploading, "med_missing" }, 10, 1, null, null, ct), "Not attachable").ConfigureAwait(false);
            ApiErrorAssert(notAttachable, 409, "MediaNotAttachable");
            Check.True(!notAttachable.FieldErrors!.ContainsKey("mediaIds[0]") && notAttachable.FieldErrors.ContainsKey("mediaIds[1]") && notAttachable.FieldErrors.ContainsKey("mediaIds[2]") && notAttachable.FieldErrors.ContainsKey("mediaIds[3]"), "fieldErrors by index");
            ApiErrorAssert(await Check.ThrowsAsync<TrellisKit.Core.Exceptions.ApiErrorException>(() => media.ValidateAttachableAsync(actor, new[] { v1, v2 }, 10, 1, null, null, ct), "Two videos").ConfigureAwait(false), 400, "MediaLimitExceeded");
            await f.Database.UserPermissions.UpsertAsync(new UserPermission { UserId = member.Id, CanPost = true, CanPostMedia = false }, ct).ConfigureAwait(false);
            ContentActor restricted = await f.Services.Actors.ForUserAsync(member.Id, ct).ConfigureAwait(false);
            ApiErrorAssert(await Check.ThrowsAsync<TrellisKit.Core.Exceptions.ApiErrorException>(() => media.ValidateAttachableAsync(restricted, new[] { a }, 10, 1, null, null, ct), "No media rights").ConfigureAwait(false), 403, "MediaNotAllowed");
            await f.Database.UserPermissions.UpsertAsync(new UserPermission { UserId = member.Id, CanPost = true, CanPostMedia = true }, ct).ConfigureAwait(false);
            await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/admin/members/" + member.Id + "/flags", HttpFixture.AdminToken, new { canUploadVideo = false }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            ContentActor noVideo = await f.Services.Actors.ForUserAsync(member.Id, ct).ConfigureAwait(false);
            ApiErrorAssert(await Check.ThrowsAsync<TrellisKit.Core.Exceptions.ApiErrorException>(() => media.ValidateAttachableAsync(noVideo, new[] { v1 }, 10, 1, null, null, ct), "No video rights").ConfigureAwait(false), 403, "VideoUploadNotAllowed");
            await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/admin/members/" + member.Id + "/flags", HttpFixture.AdminToken, new { canUploadVideo = true }, HttpStatusCode.OK, ct).ConfigureAwait(false);

            List<MediaAttachment> attached = await media.AttachAsync(TargetTypeEnum.FeedPost, "fpo_one", new[] { b, a, v1 }, ct).ConfigureAwait(false);
            Check.True(attached.Select(m => m.Id).SequenceEqual(new[] { b, a, v1 }), "Attached in order");
            Check.True(attached.Select(m => m.AttachedPosition).SequenceEqual(new[] { 0, 1, 2 }), "Positions");
            ApiErrorAssert(await Check.ThrowsAsync<TrellisKit.Core.Exceptions.ApiErrorException>(() => media.ValidateAttachableAsync(actor, new[] { a }, 10, 1, TargetTypeEnum.Comment, "cmt_x", ct), "Attached elsewhere").ConfigureAwait(false), 409, "MediaNotAttachable");
            Check.Equal(2, (await media.ValidateAttachableAsync(actor, new[] { a, b }, 10, 1, TargetTypeEnum.FeedPost, "fpo_one", ct).ConfigureAwait(false)).Count, "Same target is fine for edits");
            ApiErrorAssert(await Check.ThrowsAsync<TrellisKit.Core.Exceptions.ApiErrorException>(() => media.AttachAsync(TargetTypeEnum.Comment, "cmt_race", new[] { a }, ct), "Attach race").ConfigureAwait(false), 409, "MediaNotAttachable");

            await media.AttachAsync(TargetTypeEnum.FeedPost, "fpo_two", new[] { v2 }, ct).ConfigureAwait(false);
            Dictionary<string, List<MediaView>> views = await media.ListViewsForTargetsAsync(TargetTypeEnum.FeedPost, new[] { "fpo_one", "fpo_two", "fpo_none" }, false, false, ct).ConfigureAwait(false);
            Check.True(views["fpo_one"].Select(v => v.Id).SequenceEqual(new[] { b, a, v1 }), "Views in attachment order");
            Check.Equal(1, views["fpo_two"].Count, "Second target");
            Check.True(!views.ContainsKey("fpo_none"), "Targets without media are absent");
            Check.True(views["fpo_one"].All(v => v.Labels != null && v.Tags != null), "Labels and tags present");

            MediaReadiness readiness = await media.GetReadinessAsync(TargetTypeEnum.FeedPost, "fpo_one", ct).ConfigureAwait(false);
            Check.True(readiness.Count == 3 && !readiness.AllReady && !readiness.AnyFailed, "Videos still Processing");
            for (int pass = 0; pass < 6; pass++) await MediaTestKit.ProcessAsync(f, ct).ConfigureAwait(false);
            readiness = await media.GetReadinessAsync(TargetTypeEnum.FeedPost, "fpo_one", ct).ConfigureAwait(false);
            Check.True(readiness.AllReady, "All Ready after processing");

            media.AttachedVisibility = (m, viewer, t) => Task.FromResult(true);
            await MediaTestKit.GetMediaAsync(f, other.AccessToken, a, ct).ConfigureAwait(false);
            await ExpectErrorAsync(f, HttpMethod.Get, "/api/v1/media/" + processing, other.AccessToken, null, HttpStatusCode.NotFound, "NotFound", ct).ConfigureAwait(false);
            media.AttachedVisibility = (m, viewer, t) => Task.FromResult(m.AttachedTargetId != "fpo_one");
            await ExpectErrorAsync(f, HttpMethod.Get, "/api/v1/media/" + a, other.AccessToken, null, HttpStatusCode.NotFound, "NotFound", ct).ConfigureAwait(false);
            await MediaTestKit.GetMediaAsync(f, member.AccessToken, a, ct).ConfigureAwait(false);
            media.AttachedVisibility = (m, viewer, t) => Task.FromResult(true);

            Check.Equal(1, await media.DetachAsync(TargetTypeEnum.FeedPost, "fpo_one", new[] { a }, ct).ConfigureAwait(false), "Detach one");
            MediaAttachment detachedRow = (await f.Database.Media.ReadAsync(a, ct).ConfigureAwait(false))!;
            Check.True(!detachedRow.IsAttached && detachedRow.DetachedUtc != null, "detachedutc set");
            List<MediaAttachment> reattached = await media.AttachAsync(TargetTypeEnum.FeedPost, "fpo_one", new[] { v1 }, ct).ConfigureAwait(false);
            Check.True(reattached.Select(m => m.Id).SequenceEqual(new[] { v1 }), "Edit keeps only the listed media");
            Check.True(!(await f.Database.Media.ReadAsync(b, ct).ConfigureAwait(false))!.IsAttached, "Media removed by the edit is detached");

            Task<List<MediaAttachment>> t1 = media.AttachAsync(TargetTypeEnum.Comment, "cmt_1", new[] { b }, ct);
            Task<List<MediaAttachment>> t2 = media.AttachAsync(TargetTypeEnum.Comment, "cmt_2", new[] { b }, ct);
            int wins = 0;
            foreach (Task<List<MediaAttachment>> t in new[] { t1, t2 })
            {
                try
                {
                    await t.ConfigureAwait(false);
                    wins++;
                }
                catch (TrellisKit.Core.Exceptions.ApiErrorException)
                {
                    // The loser of the race.
                }
            }
            Check.Equal(1, wins, "Concurrent attaches: exactly one target wins");
        }

        private static void ApiErrorAssert(TrellisKit.Core.Exceptions.ApiErrorException e, int status, string error)
        {
            Check.Equal(status, e.StatusCode, error + " status");
            Check.Equal(error, e.Error, error + " code");
        }

        private static async Task RetainAndPublicAsync(HttpFixture f, CancellationToken ct)
        {
            TestMember member = await Api.MemberAsync(f, ct).ConfigureAwait(false);
            string id = await MediaTestKit.UploadSingleAsync(f, member.AccessToken, "Image", "image/png", MediaTestKit.Png(60, 60), ct).ConfigureAwait(false);
            await MediaTestKit.ProcessAsync(f, ct).ConfigureAwait(false);
            MediaService media = f.Services.Media;
            await media.AttachAsync(TargetTypeEnum.FeedPost, "fpo_mod", new[] { id }, ct).ConfigureAwait(false);

            Check.Equal(1, await media.PublishTargetAsync(TargetTypeEnum.FeedPost, "fpo_mod", ct).ConfigureAwait(false), "Published");
            Dictionary<string, List<MediaView>> pub = await media.ListViewsForTargetsAsync(TargetTypeEnum.FeedPost, new[] { "fpo_mod" }, true, false, ct).ConfigureAwait(false);
            string publicUrl = pub["fpo_mod"][0].Image!.Variants["thumb"].JpegUrl!;
            Check.Equal("/api/v1/media/files/public/" + id + "/thumb.jpg", publicUrl, "Public path URL");
            Check.Equal(HttpStatusCode.OK, (await MediaTestKit.SendAsync(f, HttpMethod.Get, publicUrl, null, null, ct).ConfigureAwait(false)).Status, "Public copy served without credentials");
            f.Settings.Media.PublicBaseUrl = "https://media.example.test";
            pub = await media.ListViewsForTargetsAsync(TargetTypeEnum.FeedPost, new[] { "fpo_mod" }, true, false, ct).ConfigureAwait(false);
            Check.Equal("https://media.example.test/" + f.Property.Id + "/public/media/" + id + "/thumb.jpg", pub["fpo_mod"][0].Image!.Variants["thumb"].JpegUrl, "Absolute public path");
            f.Settings.Media.PublicBaseUrl = String.Empty;

            Check.Equal(1, await media.RetainTargetAsync(TargetTypeEnum.FeedPost, "fpo_mod", ct).ConfigureAwait(false), "Retained");
            Check.True(File.Exists(MediaTestKit.LocalPath(f, "retained", "removed/" + id + "/media/thumb.jpg")), "Moved to retained/removed/{id}/");
            Check.True(!File.Exists(MediaTestKit.LocalPath(f, "images", "media/" + id + "/thumb.jpg")), "Not served any more");
            Check.True(File.Exists(MediaTestKit.LocalPath(f, "source", "in/" + id + "/original")), "Original kept");
            Check.Equal(HttpStatusCode.NotFound, (await MediaTestKit.SendAsync(f, HttpMethod.Get, publicUrl, null, null, ct).ConfigureAwait(false)).Status, "Public copy removed");
            Check.Equal(HttpStatusCode.NotFound, (await MediaTestKit.SendAsync(f, HttpMethod.Get, "/api/v1/media/files/" + id + "/thumb.jpg", member.AccessToken, null, ct).ConfigureAwait(false)).Status, "Retained file not served");
            MediaAttachment retained = (await f.Database.Media.ReadAsync(id, ct).ConfigureAwait(false))!;
            Check.True(retained.IsRetained && !retained.IsPublic, "isretained true, ispublic false");
            Check.True((await MediaTestKit.GetMediaAsync(f, member.AccessToken, id, ct).ConfigureAwait(false)).GetProperty("image").ValueKind == JsonValueKind.Null, "No URLs while retained");

            Check.Equal(1, await media.RestoreTargetAsync(TargetTypeEnum.FeedPost, "fpo_mod", ct).ConfigureAwait(false), "Restored");
            Check.Equal(HttpStatusCode.OK, (await MediaTestKit.SendAsync(f, HttpMethod.Get, "/api/v1/media/files/" + id + "/thumb.jpg", member.AccessToken, null, ct).ConfigureAwait(false)).Status, "Served again");
            Check.True(!Directory.Exists(MediaTestKit.LocalPath(f, "retained", "removed/" + id + "/media")), "Retained copy moved back");

            await media.PublishTargetAsync(TargetTypeEnum.FeedPost, "fpo_mod", ct).ConfigureAwait(false);
            Check.Equal(1, await media.UnpublishTargetAsync(TargetTypeEnum.FeedPost, "fpo_mod", ct).ConfigureAwait(false), "Unpublished");
            Check.Equal(HttpStatusCode.NotFound, (await MediaTestKit.SendAsync(f, HttpMethod.Get, publicUrl, null, null, ct).ConfigureAwait(false)).Status, "Public copy deleted");

            Check.Equal(1, await media.PurgeTargetAsync(TargetTypeEnum.FeedPost, "fpo_mod", ct).ConfigureAwait(false), "Purged");
            Check.True(!File.Exists(MediaTestKit.LocalPath(f, "source", "in/" + id + "/original")), "Purge deletes the original");
            Check.True((await f.Database.Media.ReadAsync(id, ct).ConfigureAwait(false))!.IsDeleted, "Row marked deleted");

            string loose = await MediaTestKit.UploadSingleAsync(f, member.AccessToken, "Image", "image/png", MediaTestKit.Png(20, 20), ct).ConfigureAwait(false);
            string kept = await MediaTestKit.UploadSingleAsync(f, member.AccessToken, "Image", "image/png", MediaTestKit.Png(20, 20), ct).ConfigureAwait(false);
            await media.AttachAsync(TargetTypeEnum.Comment, "cmt_keep", new[] { kept }, ct).ConfigureAwait(false);
            Check.Equal(1, await media.DeleteUnattachedForUserAsync(member.Id, ct).ConfigureAwait(false), "Account deletion removes unattached media");
            Check.True((await f.Database.Media.ReadAsync(loose, ct).ConfigureAwait(false))!.IsDeleted && !(await f.Database.Media.ReadAsync(kept, ct).ConfigureAwait(false))!.IsDeleted, "Attached media follows its content");
            Check.Equal(1, await media.DeleteAllForUserAsync(member.Id, ct).ConfigureAwait(false), "Delete everything");
        }

        private static async Task CleanupAsync(HttpFixture f, CancellationToken ct)
        {
            f.Settings.Media.MultipartThresholdMegabytes = 5;
            f.Settings.Media.MultipartPartMegabytes = 5;
            f.Settings.Media.UnattachedRetentionHours = 1;
            TestMember member = await Api.MemberAsync(f, ct).ConfigureAwait(false);
            JsonElement session = await MediaTestKit.CreateSessionAsync(f, member.AccessToken, "Video", "video/mp4", 6L * 1048576, 3, ct).ConfigureAwait(false);
            string stale = session.GetProperty("media").GetProperty("id").GetString()!;
            string partUrl = session.GetProperty("upload").GetProperty("multipart").GetProperty("parts")[0].GetProperty("url").GetString()!;
            await MediaTestKit.PutBytesAsync(f, partUrl, new byte[1024], null, ct).ConfigureAwait(false);
            MediaAttachment row = (await f.Database.Media.ReadAsync(stale, ct).ConfigureAwait(false))!;
            row.UploadExpiresUtc = DateTime.UtcNow.AddMinutes(-5);
            await f.Database.Media.UpdateAsync(row, null, ct).ConfigureAwait(false);

            string fresh = await MediaTestKit.UploadSingleAsync(f, member.AccessToken, "Image", "image/png", MediaTestKit.Png(20, 20), ct).ConfigureAwait(false);
            string old = await MediaTestKit.UploadSingleAsync(f, member.AccessToken, "Image", "image/png", MediaTestKit.Png(20, 20), ct).ConfigureAwait(false);
            string attached = await MediaTestKit.UploadSingleAsync(f, member.AccessToken, "Image", "image/png", MediaTestKit.Png(20, 20), ct).ConfigureAwait(false);
            await MediaTestKit.ProcessAsync(f, ct).ConfigureAwait(false);
            await f.Database.Media.AttachAsync(TargetTypeEnum.FeedPost, "fpo_old", new List<string> { old }, DateTime.UtcNow.AddHours(-3), ct).ConfigureAwait(false);
            await f.Database.Media.DetachAsync(TargetTypeEnum.FeedPost, "fpo_old", null, DateTime.UtcNow.AddHours(-2), ct).ConfigureAwait(false);
            await f.Services.Media.AttachAsync(TargetTypeEnum.FeedPost, "fpo_live", new[] { attached }, ct).ConfigureAwait(false);

            MediaCleanupWorker worker = new MediaCleanupWorker(f.Database, f.Services.Media, f.Settings.Media, f.Services.Logger);
            Check.True(await worker.RunOnceAsync(ct).ConfigureAwait(false) >= 2, "Cleanup handled items");
            MediaAttachment expired = (await f.Database.Media.ReadAsync(stale, ct).ConfigureAwait(false))!;
            Check.Equal(MediaStatusEnum.Failed, expired.Status, "Stale upload failed");
            Check.Equal(MediaFailureReasonEnum.UploadExpired, expired.FailureReason!.Value, "UploadExpired");
            Check.True(!Directory.Exists(MediaTestKit.LocalPath(f, "video-source", "in/" + stale + "/original.parts")), "Parts deleted");
            Check.True((await f.Database.Media.ReadAsync(old, ct).ConfigureAwait(false))!.IsDeleted, "Detached media past the retention deleted");
            Check.True(!File.Exists(MediaTestKit.LocalPath(f, "source", "in/" + old + "/original")), "Its original deleted");
            Check.True(!(await f.Database.Media.ReadAsync(fresh, ct).ConfigureAwait(false))!.IsDeleted, "Recent unattached media kept");
            Check.True(File.Exists(MediaTestKit.LocalPath(f, "source", "in/" + attached + "/original")), "Attached originals never touched");

            MediaCleanupWorker rival = new MediaCleanupWorker(f.Database, f.Services.Media, f.Settings.Media, f.Services.Logger);
            Check.Equal(0, await rival.RunOnceAsync(ct).ConfigureAwait(false), "Singleton lease keeps a second instance idle");
        }

        private static async Task LinkPreviewAsync(HttpFixture f, CancellationToken ct)
        {
            ILinkPreviewImageStore store = f.Services.Media;
            StoredPreviewImage? stored = await store.StoreLinkPreviewImageAsync("0123456789abcdef", MediaTestKit.Png(2400, 1200), ct).ConfigureAwait(false);
            Check.True(stored != null, "Stored");
            Check.Equal(f.Property.Id + "/link-previews/0123456789abcdef/card.jpg", stored!.Key, "Key (property-scoped)");
            Check.Equal(1200, stored.Width, "Long edge 1200");
            Check.Equal(600, stored.Height, "Ratio kept");
            string url = store.LinkPreviewImageUrl(stored.Key)!;
            Check.Equal("/api/v1/media/files/link-previews/0123456789abcdef/card.jpg", url, "Root-relative URL");
            MediaHttpResult served = await MediaTestKit.SendAsync(f, HttpMethod.Get, url, null, null, ct).ConfigureAwait(false);
            Check.Equal(HttpStatusCode.OK, served.Status, "Served");
            Check.Equal("image/jpeg", served.Header("Content-Type"), "JPEG");
            Check.True(await store.StoreLinkPreviewImageAsync("0123456789abcdef", Encoding.UTF8.GetBytes("<html>not an image</html>"), ct).ConfigureAwait(false) == null, "Non-images are refused");
            Check.True(store.LinkPreviewImageUrl(null) == null, "Null key, null URL");
            f.Settings.Media.PublicBaseUrl = "https://media.example.test";
            Check.Equal("https://media.example.test/" + f.Property.Id + "/link-previews/0123456789abcdef/card.jpg", store.LinkPreviewImageUrl(stored.Key), "Absolute URL (property-scoped)");
            f.Settings.Media.PublicBaseUrl = String.Empty;
        }

        private static async Task WebhookAsync(HttpFixture f, CancellationToken ct)
        {
            using (SnsTestSigner signer = new SnsTestSigner("us-west-2"))
            {
                f.Settings.Media.SnsTopicArn = SnsTestSigner.TopicArn;
                f.Fakes.SnsHttp.Map("GET", signer.CertificateUrl, r => new Test.Shared.IntegrationTests.FakeHttpResponse { Status = 200, Body = signer.CertificatePem, ContentType = "application/x-pem-file" });
                f.Fakes.SnsHttp.Map("GET", "https://sns.us-west-2.amazonaws.com/?Action=ConfirmSubscription", r => new Test.Shared.IntegrationTests.FakeHttpResponse { Status = 200, Body = "<ok/>", ContentType = "text/xml" });

                TestMember member = await Api.MemberAsync(f, ct).ConfigureAwait(false);
                string id = await MediaTestKit.UploadSingleAsync(f, member.AccessToken, "Video", "video/mp4", MediaTestKit.Mp4(3, 1280, 720, false, 4000), ct, 3).ConfigureAwait(false);
                MediaAttachment row = (await f.Database.Media.ReadAsync(id, ct).ConfigureAwait(false))!;
                row.ProcessorJobId = "job-" + id;
                row.Processor = "mediaconvert";
                row.ProcessingStartedUtc = DateTime.UtcNow.AddMinutes(-2);
                row.ClaimUntilUtc = DateTime.UtcNow.AddMinutes(15);
                await f.Database.Media.UpdateAsync(row, null, ct).ConfigureAwait(false);
                await f.Fakes.ObjectStore.PutAsync(MediaLocationEnum.Transcoded, MediaKeys.VideoPrefix(f.Property.Id, id) + "master.m3u8", Encoding.UTF8.GetBytes("#EXTM3U\n"), "application/vnd.apple.mpegurl", ct).ConfigureAwait(false);
                await f.Fakes.ObjectStore.PutAsync(MediaLocationEnum.Transcoded, MediaKeys.VideoPrefix(f.Property.Id, id) + "master_720p.m3u8", Encoding.UTF8.GetBytes("#EXTM3U\n"), "application/vnd.apple.mpegurl", ct).ConfigureAwait(false);
                await f.Fakes.ObjectStore.PutAsync(MediaLocationEnum.Images, MediaKeys.ImagePrefix(f.Property.Id, id) + "poster.0000001.jpg", MediaTestKit.PlainJpeg(1280, 720), "image/jpeg", ct).ConfigureAwait(false);
                await f.Fakes.ObjectStore.PutAsync(MediaLocationEnum.Images, MediaKeys.ImagePrefix(f.Property.Id, id) + "poster_thumb.0000001.jpg", MediaTestKit.PlainJpeg(320, 180), "image/jpeg", ct).ConfigureAwait(false);

                string confirm = signer.Sign("SubscriptionConfirmation", "{}", "https://sns.us-west-2.amazonaws.com/?Action=ConfirmSubscription&Token=abc");
                MediaHttpResult subscribed = await MediaTestKit.JsonAsync(f, HttpMethod.Post, "/api/v1/webhooks/mediaconvert", null, confirm, ct).ConfigureAwait(false);
                Check.Equal(HttpStatusCode.OK, subscribed.Status, "Subscription confirmation (" + subscribed.Text + ")");
                Check.Equal(1, f.Fakes.SnsHttp.Count("https://sns.us-west-2.amazonaws.com/?Action=ConfirmSubscription"), "SubscribeURL fetched");

                string eventJson = "{\"detail\":{\"status\":\"COMPLETE\",\"jobId\":\"job-" + id + "\",\"userMetadata\":{\"mediaId\":\"" + id + "\"}}}";
                MediaHttpResult complete = await MediaTestKit.JsonAsync(f, HttpMethod.Post, "/api/v1/webhooks/mediaconvert", null, signer.Sign("Notification", eventJson, null), ct).ConfigureAwait(false);
                Check.Equal(HttpStatusCode.OK, complete.Status, "Notification (" + complete.Text + ")");
                Check.Equal("processed", Api.Str(complete.Json, "message"), "Processed");
                JsonElement ready = await MediaTestKit.GetMediaAsync(f, member.AccessToken, id, ct).ConfigureAwait(false);
                Check.Equal("Ready", Api.Str(ready, "status"), "Ready from the webhook");
                Check.Equal("/api/v1/media/files/" + id + "/master.m3u8", Api.Str(ready.GetProperty("video"), "hlsUrl"), "Master playlist");
                Check.Equal("/api/v1/media/files/" + id + "/poster.jpg", Api.Str(ready.GetProperty("video"), "posterUrl"), "Poster renamed to poster.jpg");
                Check.Equal("/api/v1/media/files/" + id + "/poster_thumb.jpg", Api.Str(ready.GetProperty("video"), "posterThumbUrl"), "Poster thumb");
                Check.Equal(28, Api.Str(ready, "blurhash").Length, "Blurhash from the poster");
                MediaHttpResult playlist = await MediaTestKit.SendAsync(f, HttpMethod.Get, "/api/v1/media/files/" + id + "/master.m3u8", member.AccessToken, null, ct).ConfigureAwait(false);
                Check.Equal("application/vnd.apple.mpegurl", playlist.Header("Content-Type"), "Playlist content type");

                MediaHttpResult duplicate = await MediaTestKit.JsonAsync(f, HttpMethod.Post, "/api/v1/webhooks/mediaconvert", null, signer.Sign("Notification", eventJson, null), ct).ConfigureAwait(false);
                Check.Equal(HttpStatusCode.OK, duplicate.Status, "Duplicate still 200");
                Check.Equal("duplicate", Api.Str(duplicate.Json, "message"), "Duplicate is a no-op");

                string tampered = signer.Sign("Notification", eventJson, null).Replace("COMPLETE", "ERROR", StringComparison.Ordinal);
                Check.Equal(HttpStatusCode.BadRequest, (await MediaTestKit.JsonAsync(f, HttpMethod.Post, "/api/v1/webhooks/mediaconvert", null, tampered, ct).ConfigureAwait(false)).Status, "Tampered message 400");
                f.Settings.Media.SnsTopicArn = "arn:aws:sns:us-west-2:111111111111:other";
                Check.Equal(HttpStatusCode.BadRequest, (await MediaTestKit.JsonAsync(f, HttpMethod.Post, "/api/v1/webhooks/mediaconvert", null, signer.Sign("Notification", eventJson, null), ct).ConfigureAwait(false)).Status, "Wrong topic 400");
                f.Settings.Media.SnsTopicArn = SnsTestSigner.TopicArn;

                string id2 = await MediaTestKit.UploadSingleAsync(f, member.AccessToken, "Video", "video/mp4", MediaTestKit.Mp4(3, 1280, 720, false, 4000), ct, 3).ConfigureAwait(false);
                MediaAttachment row2 = (await f.Database.Media.ReadAsync(id2, ct).ConfigureAwait(false))!;
                row2.ProcessorJobId = "job-" + id2;
                row2.ClaimUntilUtc = DateTime.UtcNow.AddMinutes(15);
                await f.Database.Media.UpdateAsync(row2, null, ct).ConfigureAwait(false);
                await f.Fakes.ObjectStore.PutAsync(MediaLocationEnum.Transcoded, MediaKeys.VideoPrefix(f.Property.Id, id2) + "partial.ts", new byte[10], "video/mp2t", ct).ConfigureAwait(false);
                string error = "{\"detail\":{\"status\":\"ERROR\",\"jobId\":\"job-" + id2 + "\",\"userMetadata\":{\"mediaId\":\"" + id2 + "\"}}}";
                await MediaTestKit.JsonAsync(f, HttpMethod.Post, "/api/v1/webhooks/mediaconvert", null, signer.Sign("Notification", error, null), ct).ConfigureAwait(false);
                JsonElement failed = await MediaTestKit.GetMediaAsync(f, member.AccessToken, id2, ct).ConfigureAwait(false);
                Check.Equal("ProcessingError", Api.Str(failed, "failureReason"), "Job errors fail the media");
                Check.True(!File.Exists(MediaTestKit.LocalPath(f, "transcoded", "video/" + id2 + "/partial.ts")), "Partial outputs deleted");
            }
        }

        private static async Task ConcurrentCompleteAsync(HttpFixture f, CancellationToken ct)
        {
            TestMember member = await Api.MemberAsync(f, ct).ConfigureAwait(false);
            string id = await MediaTestKit.UploadSingleAsync(f, member.AccessToken, "Image", "image/png", MediaTestKit.Png(30, 30), ct, null, false).ConfigureAwait(false);
            MediaHttpResult[] results = await Task.WhenAll(Enumerable.Range(0, 5).Select(i => MediaTestKit.JsonAsync(f, HttpMethod.Post, "/api/v1/media/" + id + "/complete", member.AccessToken, null, ct))).ConfigureAwait(false);
            Check.Equal(1, results.Count(r => r.Status == HttpStatusCode.Accepted), "Exactly one 202");
            Check.True(results.All(r => r.Status == HttpStatusCode.Accepted || r.Status == HttpStatusCode.OK), "The others 200 (" + String.Join(",", results.Select(r => (int)r.Status)) + ")");
            Check.Equal(1, await MediaTestKit.ProcessAsync(f, ct).ConfigureAwait(false), "Processed once");
        }

        private static async Task FfmpegAsync(HttpFixture f, CancellationToken ct)
        {
            string work = Path.Combine(TestEnvironment.TempRoot, "ffmpeg-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(work);
            string file = Path.Combine(work, "test.mp4");
            using (System.Diagnostics.Process process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("ffmpeg", "-y -v error -f lavfi -i testsrc=size=640x360:rate=24 -t 2 -pix_fmt yuv420p -c:v libx264 -preset veryfast \"" + file + "\"") { UseShellExecute = false })!)
            {
                await process.WaitForExitAsync(ct).ConfigureAwait(false);
                Check.Equal(0, process.ExitCode, "ffmpeg generated the test video");
            }
            f.Fakes.Set.VideoTranscoder = new FfmpegTranscoder(f.Fakes.ObjectStore, f.Settings.Media);
            TestMember member = await Api.MemberAsync(f, ct).ConfigureAwait(false);
            string id = await MediaTestKit.UploadSingleAsync(f, member.AccessToken, "Video", "video/mp4", await File.ReadAllBytesAsync(file, ct).ConfigureAwait(false), ct, 2).ConfigureAwait(false);
            await MediaTestKit.ProcessAsync(f, ct).ConfigureAwait(false);
            JsonElement ready = await MediaTestKit.GetMediaAsync(f, member.AccessToken, id, ct).ConfigureAwait(false);
            Check.Equal("Ready", Api.Str(ready, "status"), "Ready (" + ready + ")");
            Check.Equal(640, ready.GetProperty("width").GetInt32(), "Width from ffprobe");
            string hls = Api.Str(ready.GetProperty("video"), "hlsUrl");
            MediaHttpResult master = await MediaTestKit.SendAsync(f, HttpMethod.Get, hls, member.AccessToken, null, ct).ConfigureAwait(false);
            Check.Equal(HttpStatusCode.OK, master.Status, "Master playlist served");
            Check.True(master.Text.Contains("hls_360p.m3u8", StringComparison.Ordinal) && !master.Text.Contains("hls_720p", StringComparison.Ordinal), "360p rung only for a 360p source");
            MediaHttpResult rung = await MediaTestKit.SendAsync(f, HttpMethod.Get, "/api/v1/media/files/" + id + "/hls_360p.m3u8", member.AccessToken, null, ct).ConfigureAwait(false);
            Check.True(rung.Text.Contains("hls_360p_00001.ts", StringComparison.Ordinal), "Flat segment names");
            MediaHttpResult segment = await MediaTestKit.SendAsync(f, HttpMethod.Get, "/api/v1/media/files/" + id + "/hls_360p_00001.ts", member.AccessToken, null, ct).ConfigureAwait(false);
            Check.Equal("video/mp2t", segment.Header("Content-Type"), "Segment content type");
            Check.Equal(HttpStatusCode.OK, (await MediaTestKit.SendAsync(f, HttpMethod.Get, Api.Str(ready.GetProperty("video"), "posterUrl"), member.AccessToken, null, ct).ConfigureAwait(false)).Status, "Poster served");
            Directory.Delete(work, true);
        }

        #endregion
    }
}
