namespace TrellisKit.Server.Services.Realtime
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// A short-lived cache of rows loaded while rendering realtime events (docs/PHASE5_CONTRACT.md section 4.11: one
    /// batched read per event, cached for 2 seconds by id so concurrent events and the recipients of one event do not
    /// repeat reads). Concurrent callers of the same key share one load. Entries are per property (one cache per
    /// <see cref="RealtimeModule"/>), so a key never crosses properties. Failed loads are not cached.
    /// Thread safety: safe to use concurrently.
    /// </summary>
    public sealed class RealtimeRenderCache
    {
        #region Public-Members

        /// <summary>
        /// Entry lifetime in milliseconds. Default 2000. Minimum 0 (no caching), maximum 60000.
        /// </summary>
        public int TtlMs
        {
            get { return _TtlMs; }
            set { _TtlMs = Math.Clamp(value, 0, 60000); }
        }

        /// <summary>
        /// Entries held before expired ones are swept. Default 2000. Minimum 16, maximum 100000.
        /// </summary>
        public int SweepThreshold
        {
            get { return _SweepThreshold; }
            set { _SweepThreshold = Math.Clamp(value, 16, 100000); }
        }

        #endregion

        #region Private-Members

        private readonly ConcurrentDictionary<string, RealtimeCacheEntry> _Entries = new ConcurrentDictionary<string, RealtimeCacheEntry>(StringComparer.Ordinal);
        private int _TtlMs = 2000;
        private int _SweepThreshold = 2000;

        #endregion

        #region Public-Methods

        /// <summary>
        /// The cached value of a key, loading it once when absent or expired.
        /// </summary>
        /// <typeparam name="T">Value type.</typeparam>
        /// <param name="key">Key, for example "notification:ntf_...".</param>
        /// <param name="load">Loader.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Value, or null.</returns>
        /// <exception cref="ArgumentNullException">The key or loader is null.</exception>
        public async Task<T?> GetOrLoadAsync<T>(string key, Func<CancellationToken, Task<T?>> load, CancellationToken token = default) where T : class
        {
            if (String.IsNullOrEmpty(key)) throw new ArgumentNullException(nameof(key));
            ArgumentNullException.ThrowIfNull(load);
            DateTime now = DateTime.UtcNow;
            if (_TtlMs == 0) return await load(token).ConfigureAwait(false);
            if (_Entries.Count > _SweepThreshold) Sweep(now);
            RealtimeCacheEntry entry = _Entries.AddOrUpdate(key,
                k => new RealtimeCacheEntry(LoadBoxed(load, token), now.AddMilliseconds(_TtlMs)),
                (k, existing) => existing.ExpiresUtc > now && !existing.Value.IsFaulted && !existing.Value.IsCanceled ? existing : new RealtimeCacheEntry(LoadBoxed(load, token), now.AddMilliseconds(_TtlMs)));
            try
            {
                return await entry.Value.ConfigureAwait(false) as T;
            }
            catch
            {
                _Entries.TryRemove(new KeyValuePair<string, RealtimeCacheEntry>(key, entry));
                throw;
            }
        }

        /// <summary>
        /// Drop a key (after a write that changes it).
        /// </summary>
        /// <param name="key">Key.</param>
        public void Remove(string key)
        {
            if (String.IsNullOrEmpty(key)) return;
            _Entries.TryRemove(key, out RealtimeCacheEntry? _);
        }

        /// <summary>
        /// Drop every entry.
        /// </summary>
        public void Clear()
        {
            _Entries.Clear();
        }

        #endregion

        #region Private-Methods

        private static async Task<object?> LoadBoxed<T>(Func<CancellationToken, Task<T?>> load, CancellationToken token) where T : class
        {
            return await load(token).ConfigureAwait(false);
        }

        private void Sweep(DateTime now)
        {
            foreach (KeyValuePair<string, RealtimeCacheEntry> pair in _Entries.Where(p => p.Value.ExpiresUtc <= now).ToList())
                _Entries.TryRemove(pair);
        }

        #endregion
    }
}
