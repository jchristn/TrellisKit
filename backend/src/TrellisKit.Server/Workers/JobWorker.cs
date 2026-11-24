namespace TrellisKit.Server.Workers
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core.Settings;
    using TrellisKit.Server.Services;
    using TrellisKit.Server.Services.Jobs;

    /// <summary>
    /// Runs the job engine of one property every jobs.tickSeconds: fires due schedules, then processes items. No lease
    /// is needed because every claim uses FOR UPDATE SKIP LOCKED, so every instance shares the work.
    /// </summary>
    public sealed class JobWorker : IntervalWorker
    {
        #region Public-Members

        /// <inheritdoc />
        public override string JobName
        {
            get { return "jobs"; }
        }

        #endregion

        #region Protected-Members

        /// <inheritdoc />
        protected override TimeSpan Interval
        {
            get { return TimeSpan.FromSeconds(_Settings.TickSeconds); }
        }

        /// <inheritdoc />
        protected override TimeSpan InitialDelay
        {
            get { return TimeSpan.FromSeconds(20); }
        }

        #endregion

        #region Private-Members

        private readonly JobService _Jobs;
        private readonly JobEngineSettings _Settings;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="jobs">Job service of the property.</param>
        /// <param name="settings">Job settings.</param>
        /// <param name="logger">Logger.</param>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        public JobWorker(JobService jobs, JobEngineSettings settings, AppLogger logger) : base(logger)
        {
            _Jobs = jobs ?? throw new ArgumentNullException(nameof(jobs));
            _Settings = settings ?? throw new ArgumentNullException(nameof(settings));
        }

        #endregion

        #region Protected-Methods

        /// <inheritdoc />
        protected override async Task<int> ExecuteAsync(CancellationToken token)
        {
            if (!_Settings.Enabled) return 0;
            int queued = await _Jobs.TickAsync(token).ConfigureAwait(false);
            int attempted = await _Jobs.ProcessAsync(token).ConfigureAwait(false);
            return queued + attempted;
        }

        #endregion
    }
}
