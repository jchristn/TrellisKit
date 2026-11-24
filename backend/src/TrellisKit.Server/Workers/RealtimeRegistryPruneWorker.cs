namespace TrellisKit.Server.Workers
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core.Database;
    using TrellisKit.Server.Services;

    /// <summary>
    /// Deletes realtimeconnections rows not seen for <see cref="StaleMinutes"/> (docs/PHASE5_CONTRACT.md section 4.11):
    /// rows of tasks that stopped without draining. Deployment-level (one per server task, every property), using the
    /// reviewed unbound prune statement; several tasks pruning at once is harmless.
    /// </summary>
    public sealed class RealtimeRegistryPruneWorker : IntervalWorker
    {
        #region Public-Members

        /// <inheritdoc />
        public override string JobName
        {
            get { return "realtime_registry_prune"; }
        }

        /// <summary>
        /// Rows not seen for this many minutes are deleted. Default 5. Minimum 2, maximum 1440.
        /// </summary>
        public int StaleMinutes
        {
            get { return _StaleMinutes; }
            set { _StaleMinutes = Math.Clamp(value, 2, 1440); }
        }

        /// <summary>
        /// Rows deleted per statement; a pass repeats until fewer remain. Default 1000. Minimum 1, maximum 10000.
        /// </summary>
        public int BatchSize
        {
            get { return _BatchSize; }
            set { _BatchSize = Math.Clamp(value, 1, 10000); }
        }

        #endregion

        #region Protected-Members

        /// <inheritdoc />
        protected override TimeSpan Interval
        {
            get { return TimeSpan.FromMinutes(1); }
        }

        #endregion

        #region Private-Members

        private readonly DatabaseDriverBase _Database;
        private int _StaleMinutes = 5;
        private int _BatchSize = 1000;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the worker.
        /// </summary>
        /// <param name="database">Deployment driver.</param>
        /// <param name="logger">Logger.</param>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        public RealtimeRegistryPruneWorker(DatabaseDriverBase database, AppLogger logger) : base(logger)
        {
            _Database = (database ?? throw new ArgumentNullException(nameof(database))).Deployment;
        }

        #endregion

        #region Protected-Methods

        /// <inheritdoc />
        protected override async Task<int> ExecuteAsync(CancellationToken token)
        {
            DateTime cutoff = DateTime.UtcNow.AddMinutes(-_StaleMinutes);
            int total = 0;
            for (int pass = 0; pass < 100; pass++)
            {
                int deleted = await _Database.RealtimeConnections.PruneStaleAsync(cutoff, _BatchSize, token).ConfigureAwait(false);
                total += deleted;
                if (deleted < _BatchSize) break;
            }
            return total;
        }

        #endregion
    }
}
