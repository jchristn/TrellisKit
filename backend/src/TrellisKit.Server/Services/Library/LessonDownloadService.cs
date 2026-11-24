namespace TrellisKit.Server.Services
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core;
    using TrellisKit.Core.Enumeration;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Exceptions;
    using TrellisKit.Core.Helpers;
    using TrellisKit.Core.Models;
    using TrellisKit.Core.Observability;
    using TrellisKit.Core.Requests;
    using TrellisKit.Core.Responses;

    /// <summary>
    /// Offline download licenses (docs/PHASE4_CONTRACT.md section 5.9): issue, renew, revoke, limits on active
    /// licenses and devices, short-lived signed URLs of the download_720p rendition, the libraryDownloads kill switch,
    /// pruning of expired licenses, and telemetry per platform (no per-member data in metrics).
    /// Thread safety: safe to call concurrently.
    /// </summary>
    public class LessonDownloadService
    {
        #region Public-Members

        /// <summary>
        /// Path of the signed rendition route; the license id follows.
        /// </summary>
        public const string FilePath = Constants.ApiPrefix + "/public/lessons/downloads/";

        #endregion

        #region Private-Members

        private readonly ApplicationServices _Services;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="services">Property services.</param>
        /// <exception cref="ArgumentNullException">The services are null.</exception>
        public LessonDownloadService(ApplicationServices services)
        {
            _Services = services ?? throw new ArgumentNullException(nameof(services));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// POST /api/v1/lessons/{id}/downloads ({ deviceId, platform }).
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="lessonId">Lesson.</param>
        /// <param name="request">Body.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>License with a signed URL.</returns>
        /// <exception cref="ApiErrorException">400 ValidationFailed, 401, 402, 403 DownloadsNotAllowed, 404, 409 DownloadLimitReached, 429 RateLimited, 503 FeatureDisabled.</exception>
        public async Task<LessonDownloadView> IssueAsync(ContentActor actor, string lessonId, LessonDownloadRequest request, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            ArgumentNullException.ThrowIfNull(request);
            string userId = actor.RequireUserId();
            await RequireNotKilledAsync(token).ConfigureAwait(false);
            Dictionary<string, string> errors = new Dictionary<string, string>();
            string device = (request.DeviceId ?? String.Empty).Trim();
            if (device.Length < 1 || device.Length > 100) errors["deviceId"] = "Use 1 to 100 characters.";
            if (request.Platform == null) errors["platform"] = "Use Ios or Android (downloads are offered in the apps only).";
            if (errors.Count > 0) throw ApiErrorException.Validation(errors);
            LibraryConfig config = await _Services.Library.Config.CurrentAsync(token).ConfigureAwait(false);
            if (!_Services.Library.Config.DownloadsEnabled(config)) throw NotAllowed("Offline downloads are turned off (library.downloads.enabled).");
            Lesson lesson = await _Services.Library.Lessons.RequirePlayableAsync(actor, lessonId, token).ConfigureAwait(false);
            if (!lesson.Downloadable || lesson.Kind != LessonKindEnum.Video) throw NotAllowed("This lesson cannot be downloaded.");
            if (!LibraryAccess.QualifiesForDownload(actor, lesson.Audience)) throw NotAllowed("Downloads are for members.");
            LessonDownloadFile rendition = await RenditionAsync(lesson, token).ConfigureAwait(false) ?? throw NotAllowed("The video of this lesson is not ready for download yet.");
            _Services.Limits.EnforceMember(actor, "library.downloads", _Services.Settings.Library.DownloadsPerDay, TimeSpan.FromDays(1));
            DateTime now = DateTime.UtcNow;
            EnumerationRequest probe = _Services.Enumeration.Prepare(new EnumerationQuery { MaxResults = Math.Min(100, _Services.Settings.Library.MaxActiveDownloadsPerMember + 1) }, LibraryCollections.Downloads, false, "downloads|" + userId);
            EnumerationResult<LessonDownload> same = await _Services.Database.Library.Downloads.EnumerateAsync(probe, userId, true, device, now, token).ConfigureAwait(false);
            bool renewing = same.Objects.Any(d => d.LessonId == lesson.Id);
            if (!renewing && await _Services.Database.Library.Downloads.CountActiveAsync(userId, now, token).ConfigureAwait(false) >= _Services.Settings.Library.MaxActiveDownloadsPerMember)
                throw new ApiErrorException(409, "DownloadLimitReached", "You hold the most active downloads allowed (" + _Services.Settings.Library.MaxActiveDownloadsPerMember + "). Delete one first.");
            List<string> devices = await _Services.Database.Library.Downloads.ActiveDevicesAsync(userId, now, token).ConfigureAwait(false);
            if (!devices.Contains(device, StringComparer.Ordinal) && devices.Count >= _Services.Settings.Library.MaxDownloadDevicesPerMember)
                throw new ApiErrorException(409, "DownloadLimitReached", "Downloads are already on " + devices.Count + " devices (the most allowed). Remove downloads from another device first.");
            LessonDownload license = await _Services.Database.Library.Downloads.IssueAsync(new LessonDownload
            {
                Id = IdGenerator.Generate(LibraryIdPrefixes.Download),
                LessonId = lesson.Id,
                UserId = userId,
                DeviceId = device,
                Platform = request.Platform!.Value,
                LicenseExpiresUtc = now.AddDays(_Services.Settings.Library.DownloadLicenseDays),
                CreatedUtc = now
            }, token).ConfigureAwait(false);
            LibraryTelemetry.Count(LibraryTelemetry.Downloads, "action", renewing ? "renew" : "issue", "platform", PlatformLabel(license.Platform));
            return await ViewAsync(license, rendition, true, token).ConfigureAwait(false);
        }

        /// <summary>
        /// POST /api/v1/me/library/downloads/{id}/renew: extends the license when the member still qualifies; revokes
        /// it (403 DownloadsNotAllowed, the app deletes the file) otherwise.
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="downloadId">License.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>License with a fresh signed URL.</returns>
        /// <exception cref="ApiErrorException">403 DownloadsNotAllowed, 404 NotFound, 503 FeatureDisabled.</exception>
        public async Task<LessonDownloadView> RenewAsync(ContentActor actor, string downloadId, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            string userId = actor.RequireUserId();
            await RequireNotKilledAsync(token).ConfigureAwait(false);
            LessonDownload license = await RequireOwnAsync(userId, downloadId, token).ConfigureAwait(false);
            DateTime now = DateTime.UtcNow;
            if (license.RevokedUtc != null) throw NotAllowed("This download license was revoked (" + (license.RevokeReason ?? "revoked") + ").");
            string? reason = await DisqualificationAsync(actor, license, token).ConfigureAwait(false);
            if (reason != null)
            {
                await _Services.Database.Library.Downloads.RevokeAsync(license.Id, reason, now, token).ConfigureAwait(false);
                LibraryTelemetry.Count(LibraryTelemetry.Downloads, "action", "revoke", "platform", PlatformLabel(license.Platform));
                throw NotAllowed("This download can no longer be renewed (" + reason + "); delete the file.");
            }
            Lesson lesson = (await _Services.Database.Library.Catalog.ReadLessonAsync(license.LessonId, token).ConfigureAwait(false))!;
            LessonDownloadFile? rendition = await RenditionAsync(lesson, token).ConfigureAwait(false);
            if (rendition == null) throw NotAllowed("The video of this lesson is not available for download.");
            LessonDownload? renewed = await _Services.Database.Library.Downloads.RenewAsync(license.Id, now.AddDays(_Services.Settings.Library.DownloadLicenseDays), now, token).ConfigureAwait(false);
            if (renewed == null) throw NotAllowed("This download license was revoked.");
            LibraryTelemetry.Count(LibraryTelemetry.Downloads, "action", "renew", "platform", PlatformLabel(renewed.Platform));
            return await ViewAsync(renewed, rendition, true, token).ConfigureAwait(false);
        }

        /// <summary>
        /// DELETE /api/v1/me/library/downloads/{id}: revoke (the app deletes the file).
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="downloadId">License.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        /// <exception cref="ApiErrorException">404 NotFound.</exception>
        public async Task RevokeAsync(ContentActor actor, string downloadId, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            string userId = actor.RequireUserId();
            LessonDownload license = await RequireOwnAsync(userId, downloadId, token).ConfigureAwait(false);
            if (await _Services.Database.Library.Downloads.RevokeAsync(license.Id, "deleted", DateTime.UtcNow, token).ConfigureAwait(false))
                LibraryTelemetry.Count(LibraryTelemetry.Downloads, "action", "revoke", "platform", PlatformLabel(license.Platform));
        }

        /// <summary>
        /// POST /api/v1/me/library/downloads/enumerate (filters activeOnly, deviceId). URLs are not included; renew to get one.
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="query">Query.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Page.</returns>
        /// <exception cref="ApiErrorException">400 ValidationFailed.</exception>
        public async Task<EnumerationResult<LessonDownloadView>> ListAsync(ContentActor actor, EnumerationQuery? query, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            string userId = actor.RequireUserId();
            EnumerationQuery q = query ?? new EnumerationQuery();
            Dictionary<string, string> errors = new Dictionary<string, string>();
            bool activeOnly = q.FilterBool("activeOnly", errors) ?? false;
            if (errors.Count > 0) throw ApiErrorException.Validation(errors);
            EnumerationRequest request = _Services.Enumeration.Prepare(q, LibraryCollections.Downloads, false, "downloads|" + userId);
            EnumerationResult<LessonDownload> page = await _Services.Database.Library.Downloads.EnumerateAsync(request, userId, activeOnly, q.FilterString("deviceId"), DateTime.UtcNow, token).ConfigureAwait(false);
            List<LessonDownloadView> views = new List<LessonDownloadView>();
            foreach (LessonDownload d in page.Objects) views.Add(await ViewAsync(d, null, false, token).ConfigureAwait(false));
            return page.With(views);
        }

        /// <summary>
        /// Resolve the rendition a signed download URL names (GET /api/v1/public/lessons/downloads/{id}?exp=&amp;sig=):
        /// the signature, the license (active), and the rendition.
        /// </summary>
        /// <param name="downloadId">License.</param>
        /// <param name="exp">exp query value.</param>
        /// <param name="sig">sig query value.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The file to serve: the download_720p rendition, or the original upload when there is none.</returns>
        /// <exception cref="ApiErrorException">403 InvalidSignature or DownloadsNotAllowed, 404 NotFound, 503 FeatureDisabled.</exception>
        public async Task<LessonDownloadFile> ResolveFileAsync(string downloadId, string? exp, string? sig, CancellationToken token = default)
        {
            string id = (downloadId ?? String.Empty).Trim();
            if (!Verify(FilePath + id, exp, sig, DateTime.UtcNow)) throw new ApiErrorException(403, "InvalidSignature", "The download URL signature is invalid or expired. Renew the license for a fresh URL.");
            await RequireNotKilledAsync(token).ConfigureAwait(false);
            LessonDownload? license = await _Services.Database.Library.Downloads.ReadAsync(id, token).ConfigureAwait(false);
            if (license == null) throw ApiErrorException.NotFound("Download");
            if (license.RevokedUtc != null || license.LicenseExpiresUtc <= DateTime.UtcNow) throw NotAllowed("This download license is no longer active.");
            Lesson? lesson = await _Services.Database.Library.Catalog.ReadLessonAsync(license.LessonId, token).ConfigureAwait(false);
            LessonDownloadFile? rendition = lesson == null ? null : await RenditionAsync(lesson, token).ConfigureAwait(false);
            if (lesson == null || rendition == null) throw ApiErrorException.NotFound("Download");
            return rendition;
        }

        /// <summary>
        /// DownloadLicensePruneWorker pass: expire licenses past their expiry and delete revoked rows after
        /// library.downloads.pruneDays.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Licenses expired.</returns>
        public async Task<int> PruneAsync(CancellationToken token = default)
        {
            DateTime now = DateTime.UtcNow;
            List<string> platforms = await _Services.Database.Library.Downloads.PruneAsync(now, now.AddDays(-_Services.Settings.Library.DownloadPruneDays), 1000, token).ConfigureAwait(false);
            foreach (string platform in platforms) LibraryTelemetry.Count(LibraryTelemetry.Downloads, "action", "expire", "platform", platform.ToLowerInvariant());
            return platforms.Count;
        }

        #endregion

        #region Private-Methods

        private async Task RequireNotKilledAsync(CancellationToken token)
        {
            AppGateDecision? decision = await _Services.LiveUpdates.AppConfig.GateAsync("GET", Constants.ApiPrefix + "/me/library/downloads", null, name => null, token).ConfigureAwait(false);
            if (decision != null) throw new ApiErrorException(decision.StatusCode, decision.Error, decision.Message);
        }

        private async Task<string?> DisqualificationAsync(ContentActor actor, LessonDownload license, CancellationToken token)
        {
            LibraryConfig config = await _Services.Library.Config.CurrentAsync(token).ConfigureAwait(false);
            if (!_Services.Library.Config.DownloadsEnabled(config)) return "downloads_disabled";
            Lesson? lesson = await _Services.Database.Library.Catalog.ReadLessonAsync(license.LessonId, token).ConfigureAwait(false);
            if (lesson == null) return "lesson_deleted";
            LibrarySeries? series = await _Services.Database.Library.Catalog.ReadSeriesAsync(lesson.SeriesId, token).ConfigureAwait(false);
            if (!LibraryAccess.IsLive(lesson, series)) return "lesson_unavailable";
            if (!lesson.Downloadable) return "not_downloadable";
            if (!LibraryAccess.QualifiesForDownload(actor, lesson.Audience)) return "membership";
            return null;
        }

        private async Task<LessonDownload> RequireOwnAsync(string userId, string downloadId, CancellationToken token)
        {
            LessonDownload? license = String.IsNullOrWhiteSpace(downloadId) ? null : await _Services.Database.Library.Downloads.ReadAsync(downloadId.Trim(), token).ConfigureAwait(false);
            if (license == null || !String.Equals(license.UserId, userId, StringComparison.Ordinal)) throw ApiErrorException.NotFound("Download");
            return license;
        }

        private async Task<LessonDownloadFile?> RenditionAsync(Lesson lesson, CancellationToken token)
        {
            if (lesson.VideoMediaId == null) return null;
            List<MediaVariant> variants = await _Services.Database.Media.ReadVariantsAsync(lesson.VideoMediaId, token).ConfigureAwait(false);
            MediaVariant? download = variants.FirstOrDefault(v => v.Name == MediaKeys.DownloadVariantName);
            if (download != null)
            {
                return new LessonDownloadFile
                {
                    Location = MediaLocationEnum.Transcoded,
                    Key = MediaKeys.VideoPrefix(_Services.PropertyId, lesson.VideoMediaId) + download.FileName,
                    FileName = MediaKeys.DownloadFileName,
                    ContentType = "video/mp4",
                    SizeBytes = download.SizeBytes,
                    Sha256 = download.Sha256,
                    IsOriginal = false
                };
            }
            // No download rendition (for example a transcoder template without the download output group): fall back to
            // the original upload, which stays in the video source location while the media exists.
            MediaAttachment? media = await _Services.Database.Media.ReadAsync(lesson.VideoMediaId, token).ConfigureAwait(false);
            if (media == null || media.Kind != MediaKindEnum.Video || media.Status != MediaStatusEnum.Ready || media.DeletedUtc != null || String.IsNullOrEmpty(media.SourceKey)) return null;
            return new LessonDownloadFile
            {
                Location = media.SourceLocation,
                Key = media.SourceKey,
                FileName = OriginalFileName(media),
                ContentType = String.IsNullOrWhiteSpace(media.ContentType) ? "video/mp4" : media.ContentType,
                SizeBytes = media.SizeBytes,
                Sha256 = null,
                IsOriginal = true
            };
        }

        private static string OriginalFileName(MediaAttachment media)
        {
            string extension = System.IO.Path.GetExtension(media.SourceKey ?? String.Empty);
            if (String.IsNullOrEmpty(extension) || extension.Length > 6) extension = ".mp4";
            return "original" + extension.ToLowerInvariant();
        }

        private async Task<LessonDownloadView> ViewAsync(LessonDownload license, LessonDownloadFile? rendition, bool withUrl, CancellationToken token)
        {
            ObjectMetadata metadata = await _Services.Metadata.LoadOneAsync(TargetTypeEnum.LessonDownload, license.Id, false, token).ConfigureAwait(false);
            LessonDownloadView view = new LessonDownloadView
            {
                Id = license.Id,
                LessonId = license.LessonId,
                DeviceId = license.DeviceId,
                Platform = license.Platform,
                LicenseExpiresUtc = license.LicenseExpiresUtc,
                SizeBytes = rendition?.SizeBytes ?? 0,
                Sha256 = rendition?.Sha256,
                RevokedUtc = license.RevokedUtc,
                RevokeReason = license.RevokeReason,
                LastRenewedUtc = license.LastRenewedUtc,
                CreatedUtc = license.CreatedUtc,
                Labels = metadata.Labels,
                Tags = metadata.Tags
            };
            if (withUrl && rendition != null)
            {
                DateTime expires = DateTime.UtcNow.AddMinutes(_Services.Settings.Library.DownloadUrlMinutes);
                view.Url = BuildUrl(FilePath + license.Id, expires);
                view.UrlExpiresUtc = DateTimeOffset.FromUnixTimeSeconds(new DateTimeOffset(expires).ToUnixTimeSeconds()).UtcDateTime;
            }
            return view;
        }

        private string Sign(string path, long expires)
        {
            byte[] key = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes("tk-library-download|" + _Services.Settings.Auth.TokenEncryptionKey + "|" + _Services.PropertyId));
            byte[] message = System.Text.Encoding.UTF8.GetBytes("GET\n" + path + "\n" + expires.ToString(System.Globalization.CultureInfo.InvariantCulture));
            return Convert.ToHexString(System.Security.Cryptography.HMACSHA256.HashData(key, message)).ToLowerInvariant();
        }

        private string BuildUrl(string path, DateTime expiresUtc)
        {
            long exp = new DateTimeOffset(DateTime.SpecifyKind(expiresUtc, DateTimeKind.Utc)).ToUnixTimeSeconds();
            return path + "?exp=" + exp.ToString(System.Globalization.CultureInfo.InvariantCulture) + "&sig=" + Sign(path, exp);
        }

        private bool Verify(string path, string? exp, string? sig, DateTime nowUtc)
        {
            if (String.IsNullOrEmpty(exp) || String.IsNullOrEmpty(sig) || sig.Length != 64) return false;
            if (!Int64.TryParse(exp, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out long expires)) return false;
            if (expires < new DateTimeOffset(DateTime.SpecifyKind(nowUtc, DateTimeKind.Utc)).ToUnixTimeSeconds()) return false;
            return System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(System.Text.Encoding.ASCII.GetBytes(Sign(path, expires)), System.Text.Encoding.ASCII.GetBytes(sig.ToLowerInvariant()));
        }

        private static ApiErrorException NotAllowed(string message)
        {
            return new ApiErrorException(403, "DownloadsNotAllowed", message);
        }

        private static string PlatformLabel(DownloadPlatformEnum platform)
        {
            return platform == DownloadPlatformEnum.Android ? "android" : "ios";
        }

        #endregion
    }
}
