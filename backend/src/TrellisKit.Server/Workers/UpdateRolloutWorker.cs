namespace TrellisKit.Server.Workers
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core.Database;
    using TrellisKit.Core.Settings;
    using TrellisKit.Server.Services;

    /// <summary>
    /// Applies live update rollout plan stages when their time arrives and pauses rollouts whose launch failure rate
    /// exceeds their threshold (resolved decision 4). Runs every updates.rolloutWorkerSeconds under a job lease so one
    /// task acts per property.
    /// </summary>
    public sealed class UpdateRolloutWorker : IntervalWorker
    {
        #region Public-Members

        /// <summary>
        /// Job name.
        /// </summary>
        public const string Name = "update_rollout";

        /// <inheritdoc />
        public override string JobName
        {
            get { return Name; }
        }

        /// <summary>
        /// Lease holder id of this task.
        /// </summary>
        public string Holder { get; } = "upr-" + Environment.MachineName + "-" + Guid.NewGuid().ToString("N").Substring(0, 12);

        #endregion

        #region Protected-Members

        /// <inheritdoc />
        protected override TimeSpan Interval
        {
            get { return TimeSpan.FromSeconds(_Settings.RolloutWorkerSeconds); }
        }

        #endregion

        #region Private-Members

        private readonly DatabaseDriverBase _Database;
        private readonly UpdatePublishService _Publish;
        private readonly UpdatesSettings _Settings;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the worker.
        /// </summary>
        /// <param name="database">Property-bound database driver.</param>
        /// <param name="publish">Publish service.</param>
        /// <param name="settings">Updates settings.</param>
        /// <param name="logger">Logger.</param>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        public UpdateRolloutWorker(DatabaseDriverBase database, UpdatePublishService publish, UpdatesSettings settings, AppLogger logger) : base(logger)
        {
            _Database = database ?? throw new ArgumentNullException(nameof(database));
            _Publish = publish ?? throw new ArgumentNullException(nameof(publish));
            _Settings = settings ?? throw new ArgumentNullException(nameof(settings));
        }

        #endregion

        #region Protected-Methods

        /// <inheritdoc />
        protected override async Task<int> ExecuteAsync(CancellationToken token)
        {
            DateTime now = DateTime.UtcNow;
            if (!await _Database.JobLeases.TryAcquireAsync(Name, Holder, now, TimeSpan.FromSeconds(Math.Max(30, _Settings.RolloutWorkerSeconds * 2)), token).ConfigureAwait(false)) return 0;
            int stages = await _Publish.ApplyDueStagesAsync(now, token).ConfigureAwait(false);
            int halted = await _Publish.AutoHaltAsync(now, token).ConfigureAwait(false);
            return stages + halted;
        }

        #endregion
    }
}
