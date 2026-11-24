namespace TrellisKit.Server.Workers
{
    using System;
    using System.Diagnostics;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core.Observability;
    using TrellisKit.Core.Metrics;
    using TrellisKit.Server.Services;

    /// <summary>
    /// Base for background workers that run a pass on an interval read live from settings. Each pass records the
    /// trelliskit.job.runs and trelliskit.job.duration metrics with the job_name label and a span.
    /// </summary>
    public abstract class IntervalWorker : IAsyncDisposable
    {
        #region Public-Members

        /// <summary>
        /// Job name used in telemetry (low-cardinality).
        /// </summary>
        public abstract string JobName { get; }

        /// <summary>Optional property-bound pass recorder, assigned by the property runtime before starting workers.</summary>
        public Func<WorkerPassRecord, CancellationToken, Task>? RecordPass { get; set; }

        #endregion

        #region Protected-Members

        /// <summary>
        /// Logger.
        /// </summary>
        protected AppLogger Logger { get; }

        /// <summary>
        /// Delay before the first pass. Default one minute.
        /// </summary>
        protected virtual TimeSpan InitialDelay
        {
            get { return TimeSpan.FromMinutes(1); }
        }

        /// <summary>
        /// Interval between passes, read before each wait.
        /// </summary>
        protected abstract TimeSpan Interval { get; }

        #endregion

        #region Private-Members

        private CancellationTokenSource? _Source = null;
        private Task? _Loop = null;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the worker.
        /// </summary>
        /// <param name="logger">Logger.</param>
        /// <exception cref="ArgumentNullException">The logger is null.</exception>
        protected IntervalWorker(AppLogger logger)
        {
            Logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Start the loop.
        /// </summary>
        /// <param name="token">Token that stops the loop.</param>
        /// <exception cref="InvalidOperationException">Already running.</exception>
        public void Start(CancellationToken token)
        {
            if (_Loop != null) throw new InvalidOperationException("The " + JobName + " worker is already running.");
            _Source = CancellationTokenSource.CreateLinkedTokenSource(token);
            _Loop = Task.Run(() => RunAsync(_Source.Token));
        }

        /// <summary>
        /// Run one pass now, with telemetry.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Items processed by the pass.</returns>
        public async Task<int> RunOnceAsync(CancellationToken token = default)
        {
            using (Activity? activity = TrellisKitTelemetry.ActivitySource.StartActivity("job " + JobName))
            {
                Stopwatch stopwatch = Stopwatch.StartNew();
                WorkerPassRecord pass = new WorkerPassRecord { Worker = JobName, StartedUtc = DateTime.UtcNow, IntervalSeconds = Interval.TotalSeconds };
                try
                {
                    int processed = await ExecuteAsync(token).ConfigureAwait(false);
                    pass.ItemsProcessed = processed;
                    TrellisKitTelemetry.RecordJob(JobName, stopwatch.Elapsed, true);
                    activity?.SetTag("processed", processed);
                    activity?.SetStatus(ActivityStatusCode.Ok);
                    return processed;
                }
                catch (Exception e) when (e is not OperationCanceledException)
                {
                    pass.Outcome = "Failed";
                    pass.Error = e.GetType().Name;
                    TrellisKitTelemetry.RecordJob(JobName, stopwatch.Elapsed, false);
                    activity?.SetStatus(ActivityStatusCode.Error, e.GetType().Name);
                    throw;
                }
                catch (OperationCanceledException)
                {
                    pass.Outcome = "Cancelled";
                    throw;
                }
                finally
                {
                    pass.EndedUtc = DateTime.UtcNow;
                    if (RecordPass != null)
                    {
                        try { await RecordPass(pass, CancellationToken.None).ConfigureAwait(false); }
                        catch (Exception e) { Logger.Warn("[Metrics] worker record failed: " + e.GetType().Name); }
                    }
                }
            }
        }

        /// <summary>
        /// Stop the loop and wait for it.
        /// </summary>
        /// <returns>Task.</returns>
        public async ValueTask DisposeAsync()
        {
            if (_Source == null) return;
            _Source.Cancel();
            if (_Loop != null)
            {
                try
                {
                    await _Loop.ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    // Expected on shutdown.
                }
            }
            _Source.Dispose();
            _Source = null;
            _Loop = null;
            GC.SuppressFinalize(this);
        }

        #endregion

        #region Protected-Methods

        /// <summary>
        /// One pass of work.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Items processed.</returns>
        protected abstract Task<int> ExecuteAsync(CancellationToken token);

        /// <summary>
        /// Wait between passes. Default: a plain delay of the interval. Workers that can be woken early (for example by
        /// an in-process signal) override it.
        /// </summary>
        /// <param name="interval">Interval read before the wait.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        /// <exception cref="OperationCanceledException">The token was cancelled.</exception>
        protected virtual Task WaitForNextPassAsync(TimeSpan interval, CancellationToken token)
        {
            return Task.Delay(interval, token);
        }

        #endregion

        #region Private-Methods

        private async Task RunAsync(CancellationToken token)
        {
            try
            {
                await Task.Delay(InitialDelay, token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            while (!token.IsCancellationRequested)
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
                    Logger.Error("[" + GetType().Name + "] pass failed", e);
                }
                try
                {
                    await WaitForNextPassAsync(Interval, token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
        }

        #endregion
    }
}
