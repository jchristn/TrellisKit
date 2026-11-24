namespace TrellisKit.Server.Workers
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Server.Services;

    /// <summary>
    /// Queue worker for account exports: claims Pending exports (conditional update on claimedby and claimuntilutc, so
    /// several server instances share the queue), writes their files, and deletes files whose retention ended.
    /// </summary>
    public sealed class AccountExportWorker : IntervalWorker
    {
        #region Public-Members

        /// <summary>
        /// Job name for telemetry.
        /// </summary>
        public override string JobName
        {
            get { return "account_exports"; }
        }

        /// <summary>
        /// Exports generated per pass. Default 5, minimum 1, maximum 100.
        /// </summary>
        public int BatchSize
        {
            get { return _BatchSize; }
            set { _BatchSize = Math.Clamp(value, 1, 100); }
        }

        #endregion

        #region Protected-Members

        /// <summary>
        /// Delay before the first pass.
        /// </summary>
        protected override TimeSpan InitialDelay
        {
            get { return TimeSpan.FromSeconds(10); }
        }

        /// <summary>
        /// Time between passes.
        /// </summary>
        protected override TimeSpan Interval
        {
            get { return TimeSpan.FromSeconds(15); }
        }

        #endregion

        #region Private-Members

        private readonly AccountExportService _Exports;
        private readonly string _Holder = "exp-" + Environment.MachineName + "-" + Guid.NewGuid().ToString("N").Substring(0, 8);
        private int _BatchSize = 5;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the worker.
        /// </summary>
        /// <param name="exports">Export service.</param>
        /// <param name="logger">Logger.</param>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        public AccountExportWorker(AccountExportService exports, AppLogger logger) : base(logger)
        {
            _Exports = exports ?? throw new ArgumentNullException(nameof(exports));
        }

        #endregion

        #region Protected-Methods

        /// <summary>
        /// One pass: generate pending exports, then expire old files.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Exports generated plus exports expired.</returns>
        protected override async Task<int> ExecuteAsync(CancellationToken token)
        {
            int generated = await _Exports.ProcessAsync(_Holder, BatchSize, token).ConfigureAwait(false);
            int expired = await _Exports.ExpireAsync(100, token).ConfigureAwait(false);
            return generated + expired;
        }


        /// <inheritdoc />
        protected override async Task WaitForNextPassAsync(TimeSpan interval, CancellationToken token)
        {
            await _Exports.Signal.WaitAsync(interval, token).ConfigureAwait(false);
        }

        #endregion
    }
}
