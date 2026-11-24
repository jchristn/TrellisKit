namespace TrellisKit.Server.Workers
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core.Database;
    using TrellisKit.Core.Settings;
    using TrellisKit.Server.Services;

    /// <summary>
    /// Publishes due feed posts (contract section 5.6) every feed.publishIntervalSeconds (read live): Scheduled posts
    /// whose publishAtUtc has passed and Processing posts whose attachments are all Ready. Each post is published by a
    /// conditional update (publishedUtc set once), which writes the feedPost.publish audit record, starts the
    /// NewFeedPost fan-out, sends pending mention notifications, and refreshes public media copies
    /// (<see cref="FeedService.TryPublishAsync"/>). The media pipeline signals the worker when media becomes Ready or
    /// Failed (<see cref="Signal"/>; a signal during a pass runs one more pass). Singleton: a pass runs only while this
    /// instance holds the feed_publish lease, which it releases when the pass ends.
    /// </summary>
    public sealed class FeedPublishWorker : IntervalWorker
    {
        #region Public-Members

        /// <summary>
        /// Job and lease name.
        /// </summary>
        public const string Name = "feed_publish";

        /// <inheritdoc />
        public override string JobName
        {
            get { return Name; }
        }

        /// <summary>
        /// Posts published per pass. Default 100, minimum 1, maximum 1000.
        /// </summary>
        public int PostsPerPass
        {
            get { return _PostsPerPass; }
            set { _PostsPerPass = Math.Clamp(value, 1, 1000); }
        }

        /// <summary>
        /// Lease holder identity of this instance.
        /// </summary>
        public string Holder { get; } = "fpw-" + Environment.MachineName + "-" + Guid.NewGuid().ToString("N").Substring(0, 12);

        #endregion

        #region Protected-Members

        /// <inheritdoc />
        protected override TimeSpan Interval
        {
            get { return TimeSpan.FromSeconds(Math.Clamp(_Settings.Feed.PublishIntervalSeconds, 5, 600)); }
        }

        /// <inheritdoc />
        protected override TimeSpan InitialDelay
        {
            get { return TimeSpan.FromSeconds(10); }
        }

        #endregion

        #region Private-Members

        private readonly DatabaseDriverBase _Database;
        private readonly FeedService _Feed;
        private readonly AppSettings _Settings;
        private int _PostsPerPass = 100;
        private int _Running = 0;
        private int _Pending = 0;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the worker.
        /// </summary>
        /// <param name="database">Database driver.</param>
        /// <param name="feed">Feed service.</param>
        /// <param name="settings">Running settings (live).</param>
        /// <param name="logger">Logger.</param>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        public FeedPublishWorker(DatabaseDriverBase database, FeedService feed, AppSettings settings, AppLogger logger) : base(logger)
        {
            _Database = database ?? throw new ArgumentNullException(nameof(database));
            _Feed = feed ?? throw new ArgumentNullException(nameof(feed));
            _Settings = settings ?? throw new ArgumentNullException(nameof(settings));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Run a pass in the background now (wired to FeedService.PublishSignal). A signal that arrives during a
        /// signaled pass runs one more pass afterwards. Never throws.
        /// </summary>
        /// <param name="token">Token that cancels the pass.</param>
        public void Signal(CancellationToken token = default)
        {
            Interlocked.Exchange(ref _Pending, 1);
            if (Interlocked.CompareExchange(ref _Running, 1, 0) != 0) return;
            _ = Task.Run(async () =>
            {
                try
                {
                    while (Interlocked.Exchange(ref _Pending, 0) == 1)
                    {
                        try
                        {
                            await RunOnceAsync(token).ConfigureAwait(false);
                        }
                        catch (OperationCanceledException)
                        {
                            return;
                        }
                        catch (Exception e)
                        {
                            Logger.Error("[FeedPublishWorker] signaled pass failed", e);
                        }
                    }
                }
                finally
                {
                    Interlocked.Exchange(ref _Running, 0);
                }
            }, CancellationToken.None);
        }

        #endregion

        #region Protected-Methods

        /// <inheritdoc />
        protected override async Task<int> ExecuteAsync(CancellationToken token)
        {
            if (!await _Database.JobLeases.TryAcquireAsync(Name, Holder, DateTime.UtcNow, TimeSpan.FromMinutes(2), token).ConfigureAwait(false)) return 0;
            try
            {
                int published = await _Feed.PublishDueAsync(_PostsPerPass, token).ConfigureAwait(false);
                if (published > 0) Logger.Info("[FeedPublishWorker] published " + published + " feed posts");
                return published;
            }
            finally
            {
                await _Database.JobLeases.ReleaseAsync(Name, Holder, CancellationToken.None).ConfigureAwait(false);
            }
        }

        #endregion
    }
}
