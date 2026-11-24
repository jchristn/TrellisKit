namespace TrellisKit.Server.Workers
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core.Database;
    using TrellisKit.Core.Settings;
    using TrellisKit.Server.Services;

    /// <summary>
    /// Deletes live update install rows idle for updates.installRetentionDays and client events older than
    /// updates.eventRetentionDays (daily counts are kept). Hourly, under a job lease.
    /// </summary>
    public sealed class UpdatePruneWorker : IntervalWorker
    {
        #region Public-Members

        /// <summary>
        /// Job name.
        /// </summary>
        public const string Name = "update_prune";

        /// <inheritdoc />
        public override string JobName
        {
            get { return Name; }
        }

        /// <summary>
        /// Lease holder id of this task.
        /// </summary>
        public string Holder { get; } = "upp-" + Environment.MachineName + "-" + Guid.NewGuid().ToString("N").Substring(0, 12);

        #endregion

        #region Protected-Members

        /// <inheritdoc />
        protected override TimeSpan Interval
        {
            get { return TimeSpan.FromHours(1); }
        }

        #endregion

        #region Private-Members

        private readonly DatabaseDriverBase _Database;
        private readonly UpdatesSettings _Settings;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the worker.
        /// </summary>
        /// <param name="database">Property-bound database driver.</param>
        /// <param name="settings">Updates settings.</param>
        /// <param name="logger">Logger.</param>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        public UpdatePruneWorker(DatabaseDriverBase database, UpdatesSettings settings, AppLogger logger) : base(logger)
        {
            _Database = database ?? throw new ArgumentNullException(nameof(database));
            _Settings = settings ?? throw new ArgumentNullException(nameof(settings));
        }

        #endregion

        #region Protected-Methods

        /// <inheritdoc />
        protected override async Task<int> ExecuteAsync(CancellationToken token)
        {
            DateTime now = DateTime.UtcNow;
            if (!await _Database.JobLeases.TryAcquireAsync(Name, Holder, now, TimeSpan.FromMinutes(10), token).ConfigureAwait(false)) return 0;
            int installs = await _Database.LiveUpdates.PruneInstallsAsync(now.AddDays(-_Settings.InstallRetentionDays), token).ConfigureAwait(false);
            int events = await _Database.LiveUpdates.PruneEventsAsync(now.AddDays(-_Settings.EventRetentionDays), token).ConfigureAwait(false);
            if (installs + events > 0) Logger.Info("[UpdatePruneWorker] pruned " + installs + " update installs and " + events + " update events");
            return installs + events;
        }

        #endregion
    }
}
