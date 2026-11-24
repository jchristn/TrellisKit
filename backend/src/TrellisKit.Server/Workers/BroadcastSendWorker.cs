namespace TrellisKit.Server.Workers
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Server.Services;

    /// <summary>
    /// Runs broadcast delivery passes (docs/PHASE5_CONTRACT.md section 8.3): each pass is
    /// <see cref="BroadcastSendService.RunPassAsync"/>, which holds the broadcast_send job lease, so one instance in a
    /// cluster sends at a time, and pauses while the broadcasts kill switch is on. <see cref="Signal"/> runs a pass now
    /// (wired to BroadcastService.SendSignal, so a send starts without waiting for the interval).
    /// </summary>
    public sealed class BroadcastSendWorker : IntervalWorker
    {
        #region Public-Members

        /// <inheritdoc />
        public override string JobName
        {
            get { return BroadcastSendService.LeaseName; }
        }

        /// <summary>
        /// Seconds between passes. Default 15. Minimum 1, maximum 3600.
        /// </summary>
        public int IntervalSeconds
        {
            get { return _IntervalSeconds; }
            set { _IntervalSeconds = Math.Clamp(value, 1, 3600); }
        }

        #endregion

        #region Protected-Members

        /// <inheritdoc />
        protected override TimeSpan Interval
        {
            get { return TimeSpan.FromSeconds(_IntervalSeconds); }
        }

        /// <inheritdoc />
        protected override TimeSpan InitialDelay
        {
            get { return TimeSpan.FromSeconds(20); }
        }

        #endregion

        #region Private-Members

        private readonly BroadcastSendService _Sender;
        private int _IntervalSeconds = 15;
        private int _Running = 0;
        private int _Pending = 0;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the worker.
        /// </summary>
        /// <param name="sender">Broadcast delivery.</param>
        /// <param name="logger">Logger.</param>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        public BroadcastSendWorker(BroadcastSendService sender, AppLogger logger) : base(logger)
        {
            _Sender = sender ?? throw new ArgumentNullException(nameof(sender));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Run a pass in the background now; a signal during a signaled pass runs one more pass afterwards. Never throws.
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
                            Logger.Error("[BroadcastSendWorker] signaled pass failed", e);
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
        protected override Task<int> ExecuteAsync(CancellationToken token)
        {
            return _Sender.RunPassAsync(token);
        }

        #endregion
    }
}
