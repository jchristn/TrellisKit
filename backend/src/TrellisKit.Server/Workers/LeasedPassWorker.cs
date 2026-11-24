namespace TrellisKit.Server.Workers
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core.Database;
    using TrellisKit.Server.Services;

    /// <summary>
    /// A singleton background pass (Phase 3 workers: thread publishing, trending, poll closing, read-state pruning, and
    /// search indexing). Each pass runs only while this instance holds the job lease named <see cref="JobName"/>, which
    /// it releases when the pass ends, so one instance in a cluster runs it at a time. The interval is read live from
    /// settings. <see cref="Signal"/> runs a pass now (a signal during a pass runs one more pass afterwards).
    /// </summary>
    public sealed class LeasedPassWorker : IntervalWorker
    {
        #region Public-Members

        /// <inheritdoc />
        public override string JobName
        {
            get { return _Name; }
        }

        /// <summary>
        /// Lease holder identity of this instance.
        /// </summary>
        public string Holder { get; }

        /// <summary>
        /// Lease duration. Default 2 minutes, minimum 10 seconds, maximum 1 hour.
        /// </summary>
        public TimeSpan LeaseDuration
        {
            get { return _LeaseDuration; }
            set { _LeaseDuration = TimeSpan.FromSeconds(Math.Clamp(value.TotalSeconds, 10, 3600)); }
        }

        #endregion

        #region Protected-Members

        /// <inheritdoc />
        protected override TimeSpan Interval
        {
            get { return _Interval(); }
        }

        /// <inheritdoc />
        protected override TimeSpan InitialDelay
        {
            get { return _InitialDelay; }
        }

        #endregion

        #region Private-Members

        private readonly DatabaseDriverBase _Database;
        private readonly string _Name;
        private readonly Func<TimeSpan> _Interval;
        private readonly TimeSpan _InitialDelay;
        private readonly Func<CancellationToken, Task<int>> _Pass;
        private TimeSpan _LeaseDuration = TimeSpan.FromMinutes(2);
        private int _Running = 0;
        private int _Pending = 0;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the worker.
        /// </summary>
        /// <param name="database">Database driver (job leases).</param>
        /// <param name="name">Job and lease name.</param>
        /// <param name="interval">Interval between passes (read before each wait).</param>
        /// <param name="initialDelay">Delay before the first pass.</param>
        /// <param name="pass">The pass; returns the number of objects processed.</param>
        /// <param name="logger">Logger.</param>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        public LeasedPassWorker(DatabaseDriverBase database, string name, Func<TimeSpan> interval, TimeSpan initialDelay, Func<CancellationToken, Task<int>> pass, AppLogger logger) : base(logger)
        {
            _Database = database ?? throw new ArgumentNullException(nameof(database));
            if (String.IsNullOrEmpty(name)) throw new ArgumentNullException(nameof(name));
            _Name = name;
            _Interval = interval ?? throw new ArgumentNullException(nameof(interval));
            _InitialDelay = initialDelay < TimeSpan.Zero ? TimeSpan.Zero : initialDelay;
            _Pass = pass ?? throw new ArgumentNullException(nameof(pass));
            Holder = name + "-" + Environment.MachineName + "-" + Guid.NewGuid().ToString("N").Substring(0, 12);
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Run a pass in the background now. A signal that arrives during a signaled pass runs one more pass afterwards.
        /// Never throws.
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
                            Logger.Error("[LeasedPassWorker] signaled pass of " + _Name + " failed", e);
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
            if (!await _Database.JobLeases.TryAcquireAsync(_Name, Holder, DateTime.UtcNow, _LeaseDuration, token).ConfigureAwait(false)) return 0;
            try
            {
                int processed = await _Pass(token).ConfigureAwait(false);
                if (processed > 0) Logger.Debug("[LeasedPassWorker] " + _Name + " processed " + processed);
                return processed;
            }
            finally
            {
                await _Database.JobLeases.ReleaseAsync(_Name, Holder, CancellationToken.None).ConfigureAwait(false);
            }
        }

        #endregion
    }
}
