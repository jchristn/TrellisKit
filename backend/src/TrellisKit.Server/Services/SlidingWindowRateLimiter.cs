namespace TrellisKit.Server.Services
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;

    /// <summary>
    /// In-memory sliding window rate limiter keyed by caller (for example "ip:1.2.3.4" or "user:usr_..."). Limits are
    /// per process; with several instances each enforces its own window.
    /// Thread safety: safe to call concurrently.
    /// </summary>
    public class SlidingWindowRateLimiter
    {
        #region Public-Members

        /// <summary>
        /// Keys tracked before stale windows are swept. Default 10000. Minimum 100, maximum 10000000.
        /// </summary>
        public int SweepThreshold
        {
            get { return _SweepThreshold; }
            set { _SweepThreshold = Math.Clamp(value, 100, 10000000); }
        }

        #endregion

        #region Private-Members

        private readonly ConcurrentDictionary<string, Queue<DateTime>> _Windows = new ConcurrentDictionary<string, Queue<DateTime>>(StringComparer.Ordinal);
        private int _SweepThreshold = 10000;

        #endregion

        #region Public-Methods

        /// <summary>
        /// Record an attempt if the key is under its limit.
        /// </summary>
        /// <param name="key">Caller key.</param>
        /// <param name="limit">Attempts allowed in the window. Minimum 1.</param>
        /// <param name="window">Window length.</param>
        /// <param name="nowUtc">Current time.</param>
        /// <returns>True when the attempt is allowed (and recorded); false when the limit is reached.</returns>
        /// <exception cref="ArgumentNullException">The key is null or empty.</exception>
        public bool TryAcquire(string key, int limit, TimeSpan window, DateTime nowUtc)
        {
            if (String.IsNullOrEmpty(key)) throw new ArgumentNullException(nameof(key));
            if (_Windows.Count > _SweepThreshold) Sweep(window, nowUtc);
            Queue<DateTime> queue = _Windows.GetOrAdd(key, k => new Queue<DateTime>());
            lock (queue)
            {
                DateTime cutoff = nowUtc - window;
                while (queue.Count > 0 && queue.Peek() <= cutoff) queue.Dequeue();
                if (queue.Count >= Math.Max(1, limit)) return false;
                queue.Enqueue(nowUtc);
                return true;
            }
        }

        /// <summary>
        /// Record an attempt if the key is under its limit, reporting how long to wait when it is not.
        /// </summary>
        /// <param name="key">Caller key.</param>
        /// <param name="limit">Attempts allowed in the window. Minimum 1.</param>
        /// <param name="window">Window length.</param>
        /// <param name="nowUtc">Current time.</param>
        /// <param name="retryAfterSeconds">Whole seconds until an attempt would be allowed (0 when allowed).</param>
        /// <returns>True when the attempt is allowed (and recorded).</returns>
        /// <exception cref="ArgumentNullException">The key is null or empty.</exception>
        public bool TryAcquire(string key, int limit, TimeSpan window, DateTime nowUtc, out int retryAfterSeconds)
        {
            retryAfterSeconds = 0;
            if (TryAcquire(key, limit, window, nowUtc)) return true;
            retryAfterSeconds = RetryAfterSeconds(key, limit, window, nowUtc);
            return false;
        }

        /// <summary>
        /// Whole seconds until a limited key may make another attempt (minimum 1), or 0 when it is not limited.
        /// </summary>
        /// <param name="key">Caller key.</param>
        /// <param name="limit">Attempts allowed in the window.</param>
        /// <param name="window">Window length.</param>
        /// <param name="nowUtc">Current time.</param>
        /// <returns>Seconds.</returns>
        public int RetryAfterSeconds(string key, int limit, TimeSpan window, DateTime nowUtc)
        {
            if (String.IsNullOrEmpty(key)) return 0;
            if (!_Windows.TryGetValue(key, out Queue<DateTime>? queue)) return 0;
            lock (queue)
            {
                DateTime cutoff = nowUtc - window;
                while (queue.Count > 0 && queue.Peek() <= cutoff) queue.Dequeue();
                if (queue.Count < Math.Max(1, limit)) return 0;
                DateTime[] entries = queue.ToArray();
                DateTime freesAt = entries[queue.Count - Math.Max(1, limit)] + window;
                return Math.Max(1, (int)Math.Ceiling((freesAt - nowUtc).TotalSeconds));
            }
        }

        /// <summary>
        /// Whether a key is currently at its limit, without recording an attempt.
        /// </summary>
        /// <param name="key">Caller key.</param>
        /// <param name="limit">Attempts allowed in the window.</param>
        /// <param name="window">Window length.</param>
        /// <param name="nowUtc">Current time.</param>
        /// <returns>True when limited.</returns>
        public bool IsLimited(string key, int limit, TimeSpan window, DateTime nowUtc)
        {
            if (String.IsNullOrEmpty(key)) return false;
            if (!_Windows.TryGetValue(key, out Queue<DateTime>? queue)) return false;
            lock (queue)
            {
                DateTime cutoff = nowUtc - window;
                while (queue.Count > 0 && queue.Peek() <= cutoff) queue.Dequeue();
                return queue.Count >= Math.Max(1, limit);
            }
        }

        /// <summary>
        /// Forget every window (tests).
        /// </summary>
        public void Clear()
        {
            _Windows.Clear();
        }

        #endregion

        #region Private-Methods

        private void Sweep(TimeSpan window, DateTime nowUtc)
        {
            DateTime cutoff = nowUtc - window;
            foreach (KeyValuePair<string, Queue<DateTime>> pair in _Windows)
            {
                bool empty;
                lock (pair.Value)
                {
                    while (pair.Value.Count > 0 && pair.Value.Peek() <= cutoff) pair.Value.Dequeue();
                    empty = pair.Value.Count == 0;
                }
                if (empty) _Windows.TryRemove(pair.Key, out Queue<DateTime>? _);
            }
        }

        #endregion
    }
}
