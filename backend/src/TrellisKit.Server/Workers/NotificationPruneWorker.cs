namespace TrellisKit.Server.Workers
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core.Database;
    using TrellisKit.Core.Settings;
    using TrellisKit.Server.Services;

    /// <summary>
    /// Notification retention (Phase 2 contract 8.3): deletes read notifications last updated more than
    /// notifications.retentionDays ago and unread ones older than twice that (actors, deliveries, and labels and tags
    /// first), plus push deliveries older than <see cref="DeliveryRetentionDays"/>. Singleton through a jobleases lease.
    /// </summary>
    public sealed class NotificationPruneWorker : IntervalWorker
    {
        #region Public-Members

        /// <inheritdoc />
        public override string JobName
        {
            get { return "notification_prune"; }
        }

        /// <summary>
        /// Clock. Default DateTime.UtcNow. Never null.
        /// </summary>
        /// <exception cref="ArgumentNullException">Set to null.</exception>
        public Func<DateTime> Clock
        {
            get { return _Clock; }
            set { _Clock = value ?? throw new ArgumentNullException(nameof(Clock)); }
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
        /// Push deliveries are deleted after this many days. Default 30. Minimum 1, maximum 3650.
        /// </summary>
        public int DeliveryRetentionDays
        {
            get { return _DeliveryRetentionDays; }
            set { _DeliveryRetentionDays = Math.Clamp(value, 1, 3650); }
        }

        /// <summary>
        /// Notifications deleted per batch. Default 1000. Minimum 1, maximum 10000.
        /// </summary>
        public int BatchSize
        {
            get { return _BatchSize; }
            set { _BatchSize = Math.Clamp(value, 1, 10000); }
        }

        /// <summary>
        /// Batches per pass. Default 20. Minimum 1, maximum 1000.
        /// </summary>
        public int MaxBatchesPerPass
        {
            get { return _MaxBatchesPerPass; }
            set { _MaxBatchesPerPass = Math.Clamp(value, 1, 1000); }
        }

        /// <summary>
        /// Lease holder identity of this instance.
        /// </summary>
        public string Holder { get; } = "prune-" + Guid.NewGuid().ToString("N");

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
        private readonly NotificationSettings _Settings;
        private Func<DateTime> _Clock = () => DateTime.UtcNow;
        private int _IntervalMinutes = 60;
        private int _DeliveryRetentionDays = 30;
        private int _BatchSize = 1000;
        private int _MaxBatchesPerPass = 20;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the worker.
        /// </summary>
        /// <param name="database">Database driver.</param>
        /// <param name="settings">Notification settings (live).</param>
        /// <param name="logger">Logger.</param>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        public NotificationPruneWorker(DatabaseDriverBase database, NotificationSettings settings, AppLogger logger) : base(logger)
        {
            _Database = database ?? throw new ArgumentNullException(nameof(database));
            _Settings = settings ?? throw new ArgumentNullException(nameof(settings));
        }

        #endregion

        #region Protected-Methods

        /// <inheritdoc />
        protected override async Task<int> ExecuteAsync(CancellationToken token)
        {
            DateTime now = _Clock();
            if (!await _Database.JobLeases.TryAcquireAsync(JobName, Holder, now, TimeSpan.FromMinutes(Math.Max(5, _IntervalMinutes)), token).ConfigureAwait(false)) return 0;
            DateTime readBefore = now.AddDays(-_Settings.RetentionDays);
            DateTime unreadBefore = now.AddDays(-2 * _Settings.RetentionDays);
            int deleted = 0;
            for (int i = 0; i < _MaxBatchesPerPass; i++)
            {
                token.ThrowIfCancellationRequested();
                int batch = await _Database.Notifications.PruneAsync(readBefore, unreadBefore, _BatchSize, token).ConfigureAwait(false);
                deleted += batch;
                if (batch < _BatchSize) break;
            }
            deleted += await _Database.PushTokens.PruneDeliveriesAsync(now.AddDays(-_DeliveryRetentionDays), token).ConfigureAwait(false);
            if (deleted > 0) Logger.Info("[NotificationPruneWorker] deleted " + deleted + " old notifications and push deliveries");
            return deleted;
        }

        #endregion
    }
}
