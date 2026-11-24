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
    using TrellisKit.Core;
    using TrellisKit.Core.Database;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Helpers;
    using TrellisKit.Core.Models;

    /// <summary>
    /// Helpers of the Phase 4 library suites.
    /// </summary>
    internal static class LibraryTestKit
    {
        internal static void Relax(HttpFixture f)
        {
            ContentTestKit.Relax(f);
            f.Settings.Library.ProgressPerMinute = 600;
            f.Settings.Library.ProgressPerHour = 100000;
            f.Settings.Library.DownloadsPerDay = 1000;
            f.Settings.RateLimits.MemberReadsPerMinute = 100000;
            f.Settings.Reactions.PerMinute = 10000;
        }

        internal static async Task<JsonElement> SeriesAsync(HttpFixture f, string bearer, string title, CancellationToken ct, object? extra = null, bool publish = true)
        {
            Dictionary<string, object?> body = new Dictionary<string, object?> { { "title", title }, { "summary", title + " summary" } };
            if (extra != null) foreach (System.Reflection.PropertyInfo p in extra.GetType().GetProperties()) body[p.Name] = p.GetValue(extra);
            JsonElement series = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/admin/series", bearer, body, HttpStatusCode.Created, ct).ConfigureAwait(false);
            if (publish) series = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/admin/series/" + Api.Str(series, "id") + "/publish", bearer, new { }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            return series;
        }

        internal static async Task<JsonElement> LessonAsync(HttpFixture f, string bearer, string seriesId, string title, CancellationToken ct, object? extra = null, bool publish = true)
        {
            Dictionary<string, object?> body = new Dictionary<string, object?> { { "title", title }, { "summary", title + " summary" }, { "kind", "Article" }, { "description", title + " article body" } };
            if (extra != null) foreach (System.Reflection.PropertyInfo p in extra.GetType().GetProperties()) body[p.Name] = p.GetValue(extra);
            JsonElement lesson = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/admin/series/" + seriesId + "/lessons", bearer, body, HttpStatusCode.Created, ct).ConfigureAwait(false);
            if (publish) lesson = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/admin/lessons/" + Api.Str(lesson, "id") + "/publish", bearer, new { }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            return lesson;
        }

        internal static async Task<string> VideoAsync(HttpFixture f, string ownerUserId, double durationSeconds, CancellationToken ct, MediaStatusEnum status = MediaStatusEnum.Ready)
        {
            DateTime now = DateTime.UtcNow;
            MediaAttachment media = new MediaAttachment
            {
                Id = IdGenerator.Generate(Constants.MediaIdPrefix),
                OwnerUserId = ownerUserId,
                Kind = MediaKindEnum.Video,
                Status = status,
                ContentType = "video/mp4",
                SizeBytes = 1000,
                Width = 1280,
                Height = 720,
                DurationSeconds = durationSeconds,
                SourceKey = "test/" + Guid.NewGuid().ToString("N"),
                HlsManifestFile = status == MediaStatusEnum.Ready ? "master.m3u8" : null,
                ReadyUtc = status == MediaStatusEnum.Ready ? now : null,
                UploadExpiresUtc = now.AddHours(1),
                CreatedUtc = now,
                UpdatedUtc = now
            };
            await f.Database.Media.CreateAsync(media, ct).ConfigureAwait(false);
            return media.Id;
        }

        internal static async Task MakeReadyAsync(HttpFixture f, string mediaId, double durationSeconds, CancellationToken ct)
        {
            MediaAttachment media = (await f.Database.Media.ReadAsync(mediaId, ct).ConfigureAwait(false))!;
            media.Status = MediaStatusEnum.Ready;
            media.DurationSeconds = durationSeconds;
            media.HlsManifestFile = "master.m3u8";
            media.ReadyUtc = DateTime.UtcNow;
            media.UpdatedUtc = DateTime.UtcNow;
            await f.Database.Media.UpdateAsync(media, null, ct).ConfigureAwait(false);
        }

        internal static async Task<string> VideoLessonAsync(HttpFixture f, TestMember admin, string seriesId, string title, double seconds, CancellationToken ct, object? extra = null)
        {
            string video = await VideoAsync(f, admin.Id, seconds, ct).ConfigureAwait(false);
            Dictionary<string, object?> body = new Dictionary<string, object?> { { "kind", "Video" }, { "videoMediaId", video } };
            if (extra != null) foreach (System.Reflection.PropertyInfo p in extra.GetType().GetProperties()) body[p.Name] = p.GetValue(extra);
            JsonElement lesson = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/admin/series/" + seriesId + "/lessons", admin.AccessToken,
                Merge(new Dictionary<string, object?> { { "title", title }, { "summary", title + " summary" } }, body), HttpStatusCode.Created, ct).ConfigureAwait(false);
            lesson = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/admin/lessons/" + Api.Str(lesson, "id") + "/publish", admin.AccessToken, new { }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            return Api.Str(lesson, "id");
        }

        internal static Dictionary<string, object?> Merge(Dictionary<string, object?> a, Dictionary<string, object?> b)
        {
            foreach (KeyValuePair<string, object?> p in b) a[p.Key] = p.Value;
            return a;
        }

        internal static Task<JsonElement> ProgressAsync(HttpFixture f, string bearer, string lessonId, double position, double played, DateTime? clientUtc, CancellationToken ct, string? session = null)
        {
            return Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/lessons/" + lessonId + "/progress", bearer,
                new { positionSeconds = position, playedSeconds = played, clientUpdatedUtc = clientUtc, sessionId = session }, HttpStatusCode.OK, ct);
        }

        internal static async Task<TestMember> EditorAsync(HttpFixture f, CancellationToken ct)
        {
            TestMember editor = await Api.MemberAsync(f, ct).ConfigureAwait(false);
            await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/admin/moderators", HttpFixture.AdminToken, new { userId = editor.Id, resourceType = "Library", resourceId = "*" }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            f.Services.Tokens.InvalidateUser(editor.Id);
            return editor;
        }

        internal static async Task<JsonElement> SetAccessAsync(HttpFixture f, object body, CancellationToken ct)
        {
            return await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/admin/property/access", HttpFixture.AdminToken, body, HttpStatusCode.OK, ct).ConfigureAwait(false);
        }

        internal static async Task SqlAsync(HttpFixture f, string sql, CancellationToken ct)
        {
            await f.Raw.ExecuteQueryAsync(sql, false, ct).ConfigureAwait(false);
        }

        internal static Task<long> CountAsync(HttpFixture f, string sql, CancellationToken ct)
        {
            return ContentTestKit.ScalarAsync(f, sql, ct);
        }

        internal static string Pid(HttpFixture f)
        {
            return f.PropertyId ?? f.Property.Id;
        }

        internal static List<string> Ids(JsonElement page)
        {
            return page.GetProperty("objects").EnumerateArray().Select(o => Api.Str(o, "id")).ToList();
        }

        internal static JsonElement Find(JsonElement page, string id)
        {
            return page.GetProperty("objects").EnumerateArray().First(o => Api.Str(o, "id") == id);
        }

        internal static async Task<int> FanoutAsync(HttpFixture f, CancellationToken ct)
        {
            int total = 0;
            for (int i = 0; i < 10; i++)
            {
                int n = await NotificationTestKit.FanoutWorker(f).RunOnceAsync(ct).ConfigureAwait(false);
                total += n;
                if (n == 0) break;
            }
            return total;
        }
    }
}
