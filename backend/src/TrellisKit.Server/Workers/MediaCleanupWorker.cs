namespace TrellisKit.Server.Workers
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core.Database;
    using TrellisKit.Core.Models;
    using TrellisKit.Core.Observability;
    using TrellisKit.Core.Settings;
    using TrellisKit.Server.Services;

    /// <summary>
    /// Singleton media cleanup (jobleases row "media_cleanup"): expires Uploading sessions older than
    /// media.uploadExpiryHours (Failed with UploadExpired; multipart uploads aborted and parts deleted), deletes media
    /// that was never attached or was detached more than media.unattachedRetentionHours ago (all objects, including
    /// originals: they belong to no content), and removes deleted rows after <see cref="DeletedRowRetentionHours"/>.
    /// Originals of attached content are never touched.
    /// Thread safety: one loop per instance; the lease keeps one instance active.
    /// </summary>
    public sealed class MediaCleanupWorker : IntervalWorker
    {
        #region Public-Members

        /// <inheritdoc />
        public override string JobName
        {
            get { return "media_cleanup"; }
        }

        /// <summary>
        /// Lease holder identifier.
        /// </summary>
        public string Holder { get; } = "media-cleanup-" + Environment.MachineName + "-" + Guid.NewGuid().ToString("N").Substring(0, 8);

        /// <summary>
        /// Minutes between passes. Default 10. Minimum 1, maximum 1440.
        /// </summary>
        public int IntervalMinutes
        {
            get { return _IntervalMinutes; }
            set { _IntervalMinutes = Math.Clamp(value, 1, 1440); }
        }

        /// <summary>
        /// Items handled per step and pass. Default 100. Minimum 1, maximum 5000.
        /// </summary>
        public int BatchSize
        {
            get { return _BatchSize; }
            set { _BatchSize = Math.Clamp(value, 1, 5000); }
        }

        /// <summary>
        /// Hours a deleted row is kept (so the daily upload quota still counts it). Default 48. Minimum 25, maximum 8760.
        /// </summary>
        public int DeletedRowRetentionHours
        {
            get { return _DeletedRowRetentionHours; }
            set { _DeletedRowRetentionHours = Math.Clamp(value, 25, 8760); }
        }

        #endregion

        #region Protected-Members

        /// <inheritdoc />
        protected override TimeSpan Interval
        {
            get { return TimeSpan.FromMinutes(_IntervalMinutes); }
        }

        #endregion

        #region Private-Members

        private readonly DatabaseDriverBase _Database;
        private readonly MediaService _Media;
        private readonly MediaSettings _Settings;
        private int _IntervalMinutes = 10;
        private int _BatchSize = 100;
        private int _DeletedRowRetentionHours = 48;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the worker.
        /// </summary>
        /// <param name="database">Database.</param>
        /// <param name="media">Media service.</param>
        /// <param name="settings">Media settings (read on each pass).</param>
        /// <param name="logger">Logger.</param>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        public MediaCleanupWorker(DatabaseDriverBase database, MediaService media, MediaSettings settings, AppLogger logger) : base(logger)
        {
            _Database = database ?? throw new ArgumentNullException(nameof(database));
            _Media = media ?? throw new ArgumentNullException(nameof(media));
            _Settings = settings ?? throw new ArgumentNullException(nameof(settings));
        }

        #endregion

        #region Protected-Methods

        /// <inheritdoc />
        protected override async Task<int> ExecuteAsync(CancellationToken token)
        {
            DateTime now = DateTime.UtcNow;
            if (!await _Database.JobLeases.TryAcquireAsync(JobName, Holder, now, TimeSpan.FromMinutes(_IntervalMinutes * 2 + 5), token).ConfigureAwait(false)) return 0;
            int handled = 0;
            Stopwatch stopwatch = Stopwatch.StartNew();
            List<MediaAttachment> expired = await _Database.Media.ListExpiredUploadsAsync(now, _BatchSize, token).ConfigureAwait(false);
            foreach (MediaAttachment media in expired)
            {
                try
                {
                    if (await _Media.ExpireUploadAsync(media, token).ConfigureAwait(false)) handled++;
                }
                catch (Exception e) when (e is not OperationCanceledException)
                {
                    Logger.Warn("[MediaCleanupWorker] expiring " + media.Id + " failed: " + e.Message);
                }
            }

            List<MediaAttachment> unattached = await _Database.Media.ListUnattachedBeforeAsync(now.AddHours(-_Settings.UnattachedRetentionHours), _BatchSize, token).ConfigureAwait(false);
            foreach (MediaAttachment media in unattached)
            {
                try
                {
                    await _Media.DeleteMediaAsync(media, token).ConfigureAwait(false);
                    handled++;
                }
                catch (Exception e) when (e is not OperationCanceledException)
                {
                    Logger.Warn("[MediaCleanupWorker] deleting " + media.Id + " failed: " + e.Message);
                }
            }

            handled += await _Database.Media.PurgeDeletedAsync(now.AddHours(-_DeletedRowRetentionHours), token).ConfigureAwait(false);
            TrellisKitTelemetry.Record(TrellisKitTelemetry.MediaStageDuration, stopwatch.Elapsed.TotalSeconds, "kind", "all", "stage", "cleanup");
            if (handled > 0) Logger.Info("[MediaCleanupWorker] expired " + expired.Count + " uploads, deleted " + unattached.Count + " unattached items");
            return handled;
        }

        #endregion
    }
}
