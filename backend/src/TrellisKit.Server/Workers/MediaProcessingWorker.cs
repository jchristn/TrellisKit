namespace TrellisKit.Server.Workers
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core.Database;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Models;
    using TrellisKit.Core.Settings;
    using TrellisKit.Server.Services;

    /// <summary>
    /// Processes media in the Processing state (contract section 4.6): each pass claims up to media.imageConcurrency
    /// images and media.videoConcurrency videos with a conditional claim (so several instances share the queue) and
    /// processes them concurrently through <see cref="MediaPipeline"/>, which also polls MediaConvert jobs older than
    /// the webhook grace period. Passes run every media.processingIntervalSeconds and immediately when
    /// <see cref="MediaWorkSignal"/> is signalled (complete and reprocess signal it).
    /// Thread safety: one loop per instance; RunOnceAsync may be called directly (tests).
    /// </summary>
    public sealed class MediaProcessingWorker : IntervalWorker
    {
        #region Public-Members

        /// <inheritdoc />
        public override string JobName
        {
            get { return "media_processing"; }
        }

        /// <summary>
        /// Worker instance identifier used for claims.
        /// </summary>
        public string Holder { get; } = "media-" + Environment.MachineName + "-" + Guid.NewGuid().ToString("N").Substring(0, 8);

        /// <summary>
        /// Image claim length in minutes. Default 10. Minimum 1, maximum 120.
        /// </summary>
        public int ImageClaimMinutes
        {
            get { return _ImageClaimMinutes; }
            set { _ImageClaimMinutes = Math.Clamp(value, 1, 120); }
        }

        /// <summary>
        /// Video claim length in minutes (covers an ffmpeg transcode). Default 90. Minimum 5, maximum 1440.
        /// </summary>
        public int VideoClaimMinutes
        {
            get { return _VideoClaimMinutes; }
            set { _VideoClaimMinutes = Math.Clamp(value, 5, 1440); }
        }

        #endregion

        #region Protected-Members

        /// <inheritdoc />
        protected override TimeSpan InitialDelay
        {
            get { return TimeSpan.FromSeconds(2); }
        }

        /// <inheritdoc />
        protected override TimeSpan Interval
        {
            get { return TimeSpan.FromSeconds(_Settings.ProcessingIntervalSeconds); }
        }

        #endregion

        #region Private-Members

        private readonly DatabaseDriverBase _Database;
        private readonly MediaService _Media;
        private readonly MediaSettings _Settings;
        private int _ImageClaimMinutes = 10;
        private int _VideoClaimMinutes = 90;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the worker.
        /// </summary>
        /// <param name="database">Database.</param>
        /// <param name="media">Media service.</param>
        /// <param name="settings">Media settings (concurrency and interval read on each pass).</param>
        /// <param name="logger">Logger.</param>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        public MediaProcessingWorker(DatabaseDriverBase database, MediaService media, MediaSettings settings, AppLogger logger) : base(logger)
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
            List<MediaAttachment> images = await _Database.Media.ClaimAsync(MediaKindEnum.Image, Holder, now, now.AddMinutes(_ImageClaimMinutes), _Settings.ImageConcurrency, token).ConfigureAwait(false);
            List<MediaAttachment> videos = await _Database.Media.ClaimAsync(MediaKindEnum.Video, Holder, now, now.AddMinutes(_VideoClaimMinutes), _Settings.VideoConcurrency, token).ConfigureAwait(false);
            images.AddRange(await _Database.Media.ClaimAsync(MediaKindEnum.Document, Holder, now, now.AddMinutes(_ImageClaimMinutes), _Settings.ImageConcurrency, token).ConfigureAwait(false));
            List<Task> work = images.Concat(videos).Select(m => _Media.Pipeline.ProcessAsync(m, token)).Cast<Task>().ToList();
            await Task.WhenAll(work).ConfigureAwait(false);
            return work.Count;
        }

        /// <inheritdoc />
        protected override async Task WaitForNextPassAsync(TimeSpan interval, CancellationToken token)
        {
            await _Media.Signal.WaitAsync(interval, token).ConfigureAwait(false);
        }

        #endregion
    }
}
