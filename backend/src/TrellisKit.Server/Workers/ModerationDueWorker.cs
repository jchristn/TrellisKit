namespace TrellisKit.Server.Workers
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core.Database;
    using TrellisKit.Server.Services;

    /// <summary>
    /// Moderation review target (Phase 2 contract section 9.1): Open reports past moderation.reviewTargetHours that were not
    /// notified yet send one ModerationOverdue notification (in-app and push) per reported target to every moderator in
    /// the report's scope, once (overduenotifiedutc), and each pass sets the trelliskit.moderation.overdue gauge.
    /// Singleton through a jobleases lease; the moderation service's Clock supplies the time (tests fix it).
    /// </summary>
    public sealed class ModerationDueWorker : IntervalWorker
    {
        #region Public-Members

        /// <inheritdoc />
        public override string JobName
        {
            get { return "moderation_due"; }
        }

        /// <summary>
        /// Minutes between passes. Default 1. Minimum 1, maximum 1440.
        /// </summary>
        public int IntervalMinutes
        {
            get { return _IntervalMinutes; }
            set { _IntervalMinutes = Math.Clamp(value, 1, 1440); }
        }

        /// <summary>
        /// Reports claimed per batch. Default 200. Minimum 1, maximum 10000.
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
        public string Holder { get; } = "moderation-due-" + Guid.NewGuid().ToString("N");

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
        private int _IntervalMinutes = 1;
        private int _BatchSize = 200;
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
        public ModerationDueWorker(DatabaseDriverBase database, ModerationService moderation, AppLogger logger) : base(logger)
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
            int handled = 0;
            for (int i = 0; i < _MaxBatchesPerPass; i++)
            {
                token.ThrowIfCancellationRequested();
                int batch = await _Moderation.NotifyOverdueAsync(_BatchSize, token).ConfigureAwait(false);
                handled += batch;
                if (batch < _BatchSize) break;
            }
            long overdue = await _Moderation.RefreshOverdueGaugeAsync(token).ConfigureAwait(false);
            if (handled > 0) Logger.Info("[ModerationDueWorker] sent overdue notices for " + handled + " reports (" + overdue + " overdue open reports)");
            return handled;
        }

        #endregion
    }
}
