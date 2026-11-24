namespace TrellisKit.Server.Services
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// In-process wake-up signal for the media processing worker: a SemaphoreSlim with a maximum count of one, so any
    /// number of signals between two passes coalesce into one early pass. Completing an upload signals it; the worker
    /// waits on it with media.processingIntervalSeconds as the timeout. Other server instances still pick work up on
    /// their interval, because the queue itself is the database.
    /// Thread safety: safe to call concurrently.
    /// </summary>
    public sealed class MediaWorkSignal : IDisposable
    {
        #region Private-Members

        private readonly SemaphoreSlim _Semaphore = new SemaphoreSlim(0, 1);
        private bool _Disposed = false;

        #endregion

        #region Public-Methods

        /// <summary>
        /// Wake the worker (a no-op when a wake-up is already pending).
        /// </summary>
        public void Signal()
        {
            if (_Disposed) return;
            try
            {
                if (_Semaphore.CurrentCount == 0) _Semaphore.Release();
            }
            catch (SemaphoreFullException)
            {
                // A wake-up is already pending.
            }
            catch (ObjectDisposedException)
            {
                // Shutting down.
            }
        }

        /// <summary>
        /// Wait for a signal or the timeout.
        /// </summary>
        /// <param name="timeout">Longest wait.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True when signalled, false on timeout.</returns>
        /// <exception cref="OperationCanceledException">The token was cancelled.</exception>
        public Task<bool> WaitAsync(TimeSpan timeout, CancellationToken token)
        {
            return _Semaphore.WaitAsync(timeout, token);
        }

        /// <summary>
        /// Release the semaphore.
        /// </summary>
        public void Dispose()
        {
            if (_Disposed) return;
            _Disposed = true;
            _Semaphore.Dispose();
        }

        #endregion
    }
}
