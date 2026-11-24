namespace TrellisKit.Server.Workers
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core.Database;
    using TrellisKit.Core.Observability;
    using TrellisKit.Core.Settings;
    using TrellisKit.Server.Services;
    using TrellisKit.Server.Services.Usage;

    /// <summary>
    /// Per-property usage worker (docs/USAGE_METERING_CONTRACT.md section 4.2): every pass flushes this instance's
    /// recorded counters, then, while holding the property's job lease "usage_rollup", runs one bounded rollup pass.
    /// The interval is read live from usage.rollupIntervalMinutes. Thread safety: one pass at a time per instance; the
    /// lease keeps instances from rolling up the same property concurrently.
    /// </summary>
    public sealed class UsageWorker : IntervalWorker
    {
        #region Public-Members

        /// <summary>
        /// Lease and job name.
        /// </summary>
        public const string Name = "usage_rollup";

        /// <inheritdoc />
        public override string JobName
        {
            get { return Name; }
        }

        /// <summary>
        /// Identifier of this instance in the lease table.
        /// </summary>
        public string Holder { get; } = "usage-" + Environment.MachineName + "-" + Guid.NewGuid().ToString("N").Substring(0, 12);

        #endregion

        #region Protected-Members

        /// <inheritdoc />
        protected override TimeSpan Interval
        {
            get { return TimeSpan.FromMinutes(_Settings.RollupIntervalMinutes); }
        }

        /// <inheritdoc />
        protected override TimeSpan InitialDelay
        {
            get { return TimeSpan.FromSeconds(45); }
        }

        #endregion

        #region Private-Members

        private readonly DatabaseDriverBase _Database;
        private readonly UsageService _Usage;
        private readonly UsageSettings _Settings;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the worker.
        /// </summary>
        /// <param name="database">Property-bound driver (job leases).</param>
        /// <param name="usage">The property's usage service.</param>
        /// <param name="settings">Usage settings (read live).</param>
        /// <param name="logger">Logger.</param>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        public UsageWorker(DatabaseDriverBase database, UsageService usage, UsageSettings settings, AppLogger logger) : base(logger)
        {
            _Database = database ?? throw new ArgumentNullException(nameof(database));
            _Usage = usage ?? throw new ArgumentNullException(nameof(usage));
            _Settings = settings ?? throw new ArgumentNullException(nameof(settings));
        }

        #endregion

        #region Protected-Methods

        /// <inheritdoc />
        protected override async Task<int> ExecuteAsync(CancellationToken token)
        {
            await _Usage.FlushAsync(token).ConfigureAwait(false);
            if (!_Settings.Enabled) return 0;
            DateTime now = DateTime.UtcNow;
            if (!await _Database.JobLeases.TryAcquireAsync(Name, Holder, now, TimeSpan.FromMinutes(15), token).ConfigureAwait(false))
            {
                UsageTelemetry.Add(UsageTelemetry.Passes, 1, "outcome", "skipped");
                return 0;
            }
            try
            {
                return await _Usage.RunPassAsync(now, token).ConfigureAwait(false);
            }
            finally
            {
                await _Database.JobLeases.ReleaseAsync(Name, Holder, CancellationToken.None).ConfigureAwait(false);
            }
        }

        #endregion
    }
}
