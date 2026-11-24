namespace TrellisKit.Server.Services
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core.Models;
    using TrellisKit.Core.Responses;

    /// <summary>
    /// Registers the library account export sections (docs/PHASE4_CONTRACT.md section 10): lessonProgress,
    /// seriesFollows, and downloads (license metadata), each streamed in bounded batches.
    /// </summary>
    public static class LibraryExportSections
    {
        #region Public-Members

        /// <summary>
        /// Section names in registration order.
        /// </summary>
        public static readonly IReadOnlyList<string> Names = new List<string> { "lessonProgress", "seriesFollows", "downloads" };

        #endregion

        #region Public-Methods

        /// <summary>
        /// Register the sections on the services' account export service (call once at startup).
        /// </summary>
        /// <param name="services">Application services.</param>
        /// <exception cref="ArgumentNullException">The services are null.</exception>
        public static void Register(ApplicationServices services)
        {
            ArgumentNullException.ThrowIfNull(services);
            AccountExportService exports = services.AccountExports;
            exports.Register(new PagedExportSection<LessonProgress>("lessonProgress", (userId, after, limit, token) => ProgressAsync(services, userId, after, limit, token), 500));
            exports.Register(new PagedExportSection<SeriesFollow>("seriesFollows", (userId, after, limit, token) => FollowsAsync(services, userId, after, limit, token), 500));
            exports.Register(new PagedExportSection<LessonDownload>("downloads", (userId, after, limit, token) => DownloadsAsync(services, userId, after, limit, token), 500));
        }

        #endregion

        #region Private-Methods

        private static async Task<ContentExportPage<LessonProgress>> ProgressAsync(ApplicationServices services, string userId, string? after, int limit, CancellationToken token)
        {
            List<LessonProgress> rows = await services.Database.Library.Progress.ListForUserAsync(userId, after, limit, token).ConfigureAwait(false);
            return new ContentExportPage<LessonProgress> { Objects = rows, NextAfterId = rows.Count < limit ? null : rows[rows.Count - 1].Id };
        }

        private static async Task<ContentExportPage<SeriesFollow>> FollowsAsync(ApplicationServices services, string userId, string? after, int limit, CancellationToken token)
        {
            List<SeriesFollow> rows = await services.Database.Library.Progress.ListFollowsForUserAsync(userId, after, limit, token).ConfigureAwait(false);
            return new ContentExportPage<SeriesFollow> { Objects = rows, NextAfterId = rows.Count < limit ? null : rows[rows.Count - 1].Id };
        }

        private static async Task<ContentExportPage<LessonDownload>> DownloadsAsync(ApplicationServices services, string userId, string? after, int limit, CancellationToken token)
        {
            List<LessonDownload> rows = await services.Database.Library.Downloads.ListForUserAsync(userId, after, limit, token).ConfigureAwait(false);
            return new ContentExportPage<LessonDownload> { Objects = rows, NextAfterId = rows.Count < limit ? null : rows[rows.Count - 1].Id };
        }

        #endregion
    }
}
