namespace TrellisKit.Server.Services
{
    using System;
    using System.Threading;

    /// <summary>
    /// Coordinates graceful shutdown and operator-requested restarts. A restart exits the process with
    /// <see cref="RestartExitCode"/> so the container runtime (restart: unless-stopped, or ECS) relaunches it.
    /// Thread safety: safe to call concurrently.
    /// </summary>
    public sealed class ServerLifetime : IDisposable
    {
        #region Public-Members

        /// <summary>
        /// Exit code used for an operator-requested restart. Default 75 (EX_TEMPFAIL), which every common restart
        /// policy treats as a reason to relaunch. Minimum 1, maximum 255.
        /// </summary>
        public int RestartExitCode
        {
            get { return _RestartExitCode; }
            set { _RestartExitCode = Math.Clamp(value, 1, 255); }
        }

        /// <summary>
        /// Delay before a requested restart begins, so the HTTP response can be delivered. Default 500 ms.
        /// Minimum 0, maximum 10000.
        /// </summary>
        public int RestartDelayMs
        {
            get { return _RestartDelayMs; }
            set { _RestartDelayMs = Math.Clamp(value, 0, 10000); }
        }

        /// <summary>
        /// Token cancelled when shutdown begins.
        /// </summary>
        public CancellationToken Token
        {
            get { return _Source.Token; }
        }

        /// <summary>
        /// Process exit code to return once shutdown completes. 0 for a normal stop.
        /// </summary>
        public int ExitCode
        {
            get { return Volatile.Read(ref _ExitCode); }
        }

        /// <summary>
        /// Whether a restart was requested.
        /// </summary>
        public bool RestartRequested
        {
            get { return Volatile.Read(ref _ExitCode) == _RestartExitCode; }
        }

        #endregion

        #region Private-Members

        private readonly CancellationTokenSource _Source = new CancellationTokenSource();
        private int _RestartExitCode = 75;
        private int _RestartDelayMs = 500;
        private int _ExitCode = 0;
        private int _Disposed = 0;

        #endregion

        #region Public-Methods

        /// <summary>
        /// Begin a normal shutdown.
        /// </summary>
        public void RequestStop()
        {
            Cancel();
        }

        /// <summary>
        /// Begin a restart after <see cref="RestartDelayMs"/>.
        /// </summary>
        public void RequestRestart()
        {
            Interlocked.Exchange(ref _ExitCode, _RestartExitCode);
            Timer? timer = null;
            timer = new Timer(_ =>
            {
                Cancel();
                timer?.Dispose();
            }, null, _RestartDelayMs, Timeout.Infinite);
        }

        /// <summary>
        /// Dispose the lifetime.
        /// </summary>
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _Disposed, 1) == 1) return;
            _Source.Dispose();
        }

        #endregion

        #region Private-Methods

        private void Cancel()
        {
            if (Volatile.Read(ref _Disposed) == 1) return;
            try
            {
                _Source.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // Already disposed during shutdown.
            }
        }

        #endregion
    }
}
