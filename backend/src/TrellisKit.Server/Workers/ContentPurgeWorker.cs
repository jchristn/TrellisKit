namespace TrellisKit.Server.Workers
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core.Database;
    using TrellisKit.Server.Services;

    /// <summary>
    /// Removed-content retention (Phase 2 contract section 9.3): posts and comments removed or deleted more than
    /// moderation.retainRemovedDays ago with no Open report are purged (media and originals deleted, bodies and revisions
    /// cleared, labels and tags deleted, purgedutc set).
    /// Singleton through a jobleases lease; the moderation service's Clock supplies the time (tests fix it).
    /// </summary>
    public sealed class ContentPurgeWorker : IntervalWorker
    {
        #region Public-Members

        /// <inheritdoc />
        public override string JobName
        {
            get { return "content_purge"; }
        }

        /// <summary>
        /// Minutes between passes. Default 60. Minimum 1, maximum 1440.
        /// </summary>
        public int IntervalMinutes
        {
            get { return _IntervalMinutes; }
            set { _IntervalMinutes = Math.Clamp(value, 1, 1440); }
        }

        /// <summary>
        /// Items of each type per batch. Default 100. Minimum 1, maximum 10000.
        /// </summary>
        public int BatchSize
        {
            get { return _BatchSize; }
            set { _BatchSize = Math.Clamp(value, 1, 10000); }
        }

        /// <summary>
        /// Batches per pass. Default 10. Minimum 1, maximum 1000.
        /// </summary>
        public int MaxBatchesPerPass
        {
            get { return _MaxBatchesPerPass; }
            set { _MaxBatchesPerPass = Math.Clamp(value, 1, 1000); }
        }

        /// <summary>
        /// Lease holder identity of this instance.
        /// </summary>
        public string Holder { get; } = "content-purge-" + Guid.NewGuid().ToString("N");

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
        private readonly ModerationService _Moderation;
        private int _IntervalMinutes = 60;
        private int _BatchSize = 100;
        private int _MaxBatchesPerPass = 10;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the worker.
        /// </summary>
        /// <param name="database">Database driver (job lease).</param>
        /// <param name="moderation">Moderation service.</param>
        /// <param name="logger">Logger.</param>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        public ContentPurgeWorker(DatabaseDriverBase database, ModerationService moderation, AppLogger logger) : base(logger)
        {
            _Database = database ?? throw new ArgumentNullException(nameof(database));
            _Moderation = moderation ?? throw new ArgumentNullException(nameof(moderation));
        }

        #endregion

        #region Protected-Methods

        /// <inheritdoc />
        protected override async Task<int> ExecuteAsync(CancellationToken token)
        {
            if (!await _Database.JobLeases.TryAcquireAsync(JobName, Holder, _Moderation.Clock(), TimeSpan.FromMinutes(Math.Max(5, _IntervalMinutes * 2)), token).ConfigureAwait(false)) return 0;
            int purged = 0;
            for (int i = 0; i < _MaxBatchesPerPass; i++)
            {
                token.ThrowIfCancellationRequested();
                int batch = await _Moderation.PurgeDueAsync(_BatchSize, token).ConfigureAwait(false);
                purged += batch;
                if (batch == 0) break;
            }
            if (purged > 0) Logger.Info("[ContentPurgeWorker] purged " + purged + " removed or deleted posts and comments");
            return purged;
        }

        #endregion
    }
}
