namespace Test.Shared.Suites
{
    using System;
    using System.IO;
    using System.Linq;
    using System.Net;
    using System.Net.Http;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Touchstone.Core;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Helpers;

    /// <summary>
    /// Offline downloads (docs/PHASE4_CONTRACT.md section 5.9): off by default, licenses with signed short-lived URLs,
    /// the signed file route, active and device limits, renew and revoke, revocation on archive, pruning of expired
    /// licenses, and the libraryDownloads kill switch.
    /// </summary>
    public static class LessonDownloadSuite
    {
        #region Public-Members

        /// <summary>
        /// Suite identifier.
        /// </summary>
        public const string Id = "LessonDownload";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the suite.
        /// </summary>
        /// <returns>Suite.</returns>
        public static TestSuiteDescriptor Build()
        {
            return new TestSuiteDescriptor(Id, "Lesson downloads (" + TestEnvironment.Provider + ")", new[]
            {
                SuiteCases.Http(Id, "Downloads.Lifecycle", "Downloads are off by default; once enabled a member gets a license with a signed URL that serves the rendition; limits on active licenses and devices; renew, revoke, archive revocation, prune, and the kill switch", LifecycleAsync),
                SuiteCases.Http(Id, "Downloads.OriginalFallback", "Without a download_720p rendition the license and the signed URL serve the original upload; once the rendition exists it is preferred", OriginalFallbackAsync)
            });
        }

        #endregion

        #region Private-Methods

        private static async Task<string> RenditionLessonAsync(HttpFixture f, TestMember admin, string seriesId, string title, CancellationToken ct)
        {
            string lesson = await LibraryTestKit.VideoLessonAsync(f, admin, seriesId, title, 60, ct).ConfigureAwait(false);
            JsonElement detail = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/admin/lessons/" + lesson, admin.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            string media = Api.Str(detail.GetProperty("video"), "id");
            await LibraryTestKit.SqlAsync(f, "INSERT INTO mediavariants (propertyid, mediaid, name, format, width, height, sizebytes, filename, sha256) VALUES ('" + LibraryTestKit.Pid(f) + "', '" + media +
                "', 'download_720p', 'mp4', 1280, 720, 11, 'download_720p.mp4', 'abc123');", ct).ConfigureAwait(false);
            string path = MediaTestKit.LocalPath(f, MediaKeys.FolderName(MediaLocationEnum.Transcoded), MediaKeys.VideoPrefix(LibraryTestKit.Pid(f), media) + MediaKeys.DownloadFileName);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllBytesAsync(path, System.Text.Encoding.ASCII.GetBytes("fake-mp4-11"), ct).ConfigureAwait(false);
            return lesson;
        }

        private static async Task OriginalFallbackAsync(HttpFixture f, CancellationToken ct)
        {
            LibraryTestKit.Relax(f);
            TestMember admin = await Api.AdminAsync(f, ct).ConfigureAwait(false);
            TestMember member = await Api.MemberAsync(f, ct).ConfigureAwait(false);
            string seriesId = Api.Str(await LibraryTestKit.SeriesAsync(f, admin.AccessToken, "Fallback course", ct).ConfigureAwait(false), "id");
            string lesson = await LibraryTestKit.VideoLessonAsync(f, admin, seriesId, "Fallback one", 60, ct).ConfigureAwait(false);
            JsonElement detail = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/admin/lessons/" + lesson, admin.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            string media = Api.Str(detail.GetProperty("video"), "id");
            await LibraryTestKit.SqlAsync(f, "UPDATE mediaattachments SET sourcekey = '" + LibraryTestKit.Pid(f) + "/in/" + media + "/original.mp4' WHERE id = '" + media + "';", ct).ConfigureAwait(false);
            TrellisKit.Core.Models.MediaAttachment? row = await f.Database.Media.ReadAsync(media, ct).ConfigureAwait(false);
            Check.True(row != null, "Video media row");
            byte[] original = System.Text.Encoding.ASCII.GetBytes("original-upload-bytes");
            string source = MediaTestKit.LocalPath(f, MediaKeys.FolderName(MediaLocationEnum.VideoSource), row!.SourceKey);
            Directory.CreateDirectory(Path.GetDirectoryName(source)!);
            await File.WriteAllBytesAsync(source, original, ct).ConfigureAwait(false);
            await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/admin/library/settings", admin.AccessToken, new { downloadsEnabled = true }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            JsonElement license = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/lessons/" + lesson + "/downloads", member.AccessToken, new { deviceId = "phone-1", platform = "Ios" }, HttpStatusCode.Created, ct).ConfigureAwait(false);
            Check.Equal(row.SizeBytes, license.GetProperty("sizeBytes").GetInt64(), "Original size");
            Check.Equal(JsonValueKind.Null, license.GetProperty("sha256").ValueKind, "No hash for the original");
            MediaHttpResult file = await MediaTestKit.SendAsync(f, HttpMethod.Get, Api.Str(license, "url"), null, null, ct).ConfigureAwait(false);
            Check.Equal(HttpStatusCode.OK, file.Status, "The signed URL serves the original (" + file.Text + ")");
            Check.Equal("original-upload-bytes", file.Text, "Original bytes");

            await LibraryTestKit.SqlAsync(f, "INSERT INTO mediavariants (propertyid, mediaid, name, format, width, height, sizebytes, filename, sha256) VALUES ('" + LibraryTestKit.Pid(f) + "', '" + media +
                "', 'download_720p', 'mp4', 1280, 720, 11, 'download_720p.mp4', 'abc123');", ct).ConfigureAwait(false);
            string rendition = MediaTestKit.LocalPath(f, MediaKeys.FolderName(MediaLocationEnum.Transcoded), MediaKeys.VideoPrefix(LibraryTestKit.Pid(f), media) + MediaKeys.DownloadFileName);
            Directory.CreateDirectory(Path.GetDirectoryName(rendition)!);
            await File.WriteAllBytesAsync(rendition, System.Text.Encoding.ASCII.GetBytes("fake-mp4-11"), ct).ConfigureAwait(false);
            JsonElement renewed = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/me/library/downloads/" + Api.Str(license, "id") + "/renew", member.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal("abc123", Api.Str(renewed, "sha256"), "The rendition is preferred once present");
            MediaHttpResult preferred = await MediaTestKit.SendAsync(f, HttpMethod.Get, Api.Str(renewed, "url"), null, null, ct).ConfigureAwait(false);
            Check.Equal("fake-mp4-11", preferred.Text, "Rendition bytes");
        }

        private static async Task LifecycleAsync(HttpFixture f, CancellationToken ct)
        {
            LibraryTestKit.Relax(f);
            TestMember admin = await Api.AdminAsync(f, ct).ConfigureAwait(false);
            TestMember member = await Api.MemberAsync(f, ct).ConfigureAwait(false);
            TestMember free = await Api.RegisterAsync(f, ct).ConfigureAwait(false);
            string seriesId = Api.Str(await LibraryTestKit.SeriesAsync(f, admin.AccessToken, "Offline course", ct).ConfigureAwait(false), "id");
            string lesson = await RenditionLessonAsync(f, admin, seriesId, "Offline one", ct).ConfigureAwait(false);
            string second = await RenditionLessonAsync(f, admin, seriesId, "Offline two", ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Post, "/api/v1/lessons/" + lesson + "/downloads", member.AccessToken, new { deviceId = "phone-1", platform = "Ios" }, HttpStatusCode.Forbidden, "DownloadsNotAllowed", ct).ConfigureAwait(false);
            await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/admin/library/settings", admin.AccessToken, new { downloadsEnabled = true }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            JsonElement detail = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/lessons/" + lesson, member.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.True(detail.GetProperty("viewer").GetProperty("canDownload").GetBoolean(), "viewer.canDownload for members");
            await Api.ExpectErrorAsync(f, HttpMethod.Post, "/api/v1/lessons/" + lesson + "/downloads", member.AccessToken, new { deviceId = "phone-1" }, HttpStatusCode.BadRequest, "ValidationFailed", ct).ConfigureAwait(false);
            JsonElement license = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/lessons/" + lesson + "/downloads", member.AccessToken, new { deviceId = "phone-1", platform = "Ios" }, HttpStatusCode.Created, ct).ConfigureAwait(false);
            Check.True(Api.Str(license, "id").StartsWith("ldl_", StringComparison.Ordinal), "License id");
            Check.Equal(11L, license.GetProperty("sizeBytes").GetInt64(), "Rendition size");
            Check.Equal("abc123", Api.Str(license, "sha256"), "Rendition hash");
            string url = Api.Str(license, "url");
            MediaHttpResult file = await MediaTestKit.SendAsync(f, HttpMethod.Get, url, null, null, ct).ConfigureAwait(false);
            Check.Equal(HttpStatusCode.OK, file.Status, "The signed URL serves the rendition (" + file.Text + ")");
            MediaHttpResult forged = await MediaTestKit.SendAsync(f, HttpMethod.Get, url.Replace("sig=", "sig=0"), null, null, ct).ConfigureAwait(false);
            Check.Equal(HttpStatusCode.Forbidden, forged.Status, "A forged signature is refused");

            f.Settings.Library.MaxActiveDownloadsPerMember = 1;
            JsonElement again = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/lessons/" + lesson + "/downloads", member.AccessToken, new { deviceId = "phone-1", platform = "Ios" }, HttpStatusCode.Created, ct).ConfigureAwait(false);
            Check.Equal(Api.Str(license, "id"), Api.Str(again, "id"), "The same device and lesson reuse the license");
            await Api.ExpectErrorAsync(f, HttpMethod.Post, "/api/v1/lessons/" + second + "/downloads", member.AccessToken, new { deviceId = "phone-1", platform = "Ios" }, HttpStatusCode.Conflict, "DownloadLimitReached", ct).ConfigureAwait(false);
            f.Settings.Library.MaxActiveDownloadsPerMember = 25;
            f.Settings.Library.MaxDownloadDevicesPerMember = 1;
            await Api.ExpectErrorAsync(f, HttpMethod.Post, "/api/v1/lessons/" + second + "/downloads", member.AccessToken, new { deviceId = "tablet-2", platform = "Android" }, HttpStatusCode.Conflict, "DownloadLimitReached", ct).ConfigureAwait(false);
            f.Settings.Library.MaxDownloadDevicesPerMember = 3;
            JsonElement other = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/lessons/" + second + "/downloads", member.AccessToken, new { deviceId = "tablet-2", platform = "Android" }, HttpStatusCode.Created, ct).ConfigureAwait(false);

            JsonElement renewed = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/me/library/downloads/" + Api.Str(license, "id") + "/renew", member.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.True(Api.Str(renewed, "url").Length > 0, "Renewal returns a fresh URL");
            JsonElement list = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/me/library/downloads/enumerate", member.AccessToken, new { filters = new { activeOnly = true } }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal(2, list.GetProperty("objects").GetArrayLength(), "Active licenses listed");
            await Api.ExpectErrorAsync(f, HttpMethod.Post, "/api/v1/me/library/downloads/" + Api.Str(license, "id") + "/renew", free.AccessToken, null, HttpStatusCode.NotFound, "NotFound", ct).ConfigureAwait(false);

            await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/admin/lessons/" + second + "/archive", admin.AccessToken, new { }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Post, "/api/v1/me/library/downloads/" + Api.Str(other, "id") + "/renew", member.AccessToken, null, HttpStatusCode.Forbidden, "DownloadsNotAllowed", ct).ConfigureAwait(false);
            await Api.ExpectAsync(f, HttpMethod.Delete, "/api/v1/me/library/downloads/" + Api.Str(license, "id"), member.AccessToken, null, HttpStatusCode.NoContent, ct).ConfigureAwait(false);
            MediaHttpResult revoked = await MediaTestKit.SendAsync(f, HttpMethod.Get, Api.Str(renewed, "url"), null, null, ct).ConfigureAwait(false);
            Check.Equal(HttpStatusCode.Forbidden, revoked.Status, "A revoked license no longer serves the file");

            JsonElement third = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/lessons/" + lesson + "/downloads", member.AccessToken, new { deviceId = "phone-1", platform = "Ios" }, HttpStatusCode.Created, ct).ConfigureAwait(false);
            await LibraryTestKit.SqlAsync(f, "UPDATE lessondownloads SET licenseexpiresutc = now() - interval '1 hour' WHERE id = '" + Api.Str(third, "id") + "';", ct).ConfigureAwait(false);
            Check.True(await f.Services.Library.Downloads.PruneAsync(ct).ConfigureAwait(false) >= 1, "Expired licenses are pruned");
            Check.Equal(1L, await LibraryTestKit.CountAsync(f, "SELECT COUNT(*) AS cnt FROM lessondownloads WHERE id = '" + Api.Str(third, "id") + "' AND revokereason = 'expired';", ct).ConfigureAwait(false), "Marked expired");

            await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/admin/app/kill-switches/libraryDownloads", HttpFixture.AdminToken, new { killed = true, message = "Downloads paused.", reason = "Test" }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            ApiResult killed = await Api.SendAsync(f, HttpMethod.Post, "/api/v1/lessons/" + lesson + "/downloads", member.AccessToken, new { deviceId = "phone-1", platform = "Ios" }, ct).ConfigureAwait(false);
            Check.Equal(HttpStatusCode.ServiceUnavailable, killed.Status, "libraryDownloads kill switch");
            await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/lessons/" + lesson, member.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
        }

        #endregion
    }
}
