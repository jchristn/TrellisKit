namespace TrellisKit.Server.Services.Jobs
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text.RegularExpressions;
    using System.Threading;

    /// <summary>
    /// The job handlers of one property. Features register their handlers at startup (Phase 5 discovery, catalogs,
    /// outreach, and so on). Thread safety: safe for concurrent use.
    /// </summary>
    public class JobRegistry
    {
        #region Public-Members

        /// <summary>
        /// Incremented on every registration (the service re-seeds definitions when it changes).
        /// </summary>
        public int Version
        {
            get { return _Version; }
        }

        #endregion

        #region Private-Members

        private static readonly Regex _KeyPattern = new Regex("^[a-z][a-zA-Z0-9]*(\\.[a-z][a-zA-Z0-9]*)+$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
        private readonly ConcurrentDictionary<string, IJobHandler> _Handlers = new ConcurrentDictionary<string, IJobHandler>(StringComparer.Ordinal);
        private int _Version = 0;

        #endregion

        #region Public-Methods

        /// <summary>
        /// Register a handler, replacing one with the same key.
        /// </summary>
        /// <param name="handler">Handler.</param>
        /// <exception cref="ArgumentNullException">handler is null.</exception>
        /// <exception cref="ArgumentException">The key is not dotted lower camel case (for example discovery.organizations), or the defaults are missing a name.</exception>
        public void Register(IJobHandler handler)
        {
            ArgumentNullException.ThrowIfNull(handler);
            if (String.IsNullOrEmpty(handler.Key) || handler.Key.Length > 100 || !_KeyPattern.IsMatch(handler.Key)) throw new ArgumentException("Job keys are dotted lower camel case, for example discovery.organizations.", nameof(handler));
            if (handler.Defaults == null || String.IsNullOrWhiteSpace(handler.Defaults.Name)) throw new ArgumentException("Job handlers need a name in their defaults.", nameof(handler));
            _Handlers[handler.Key] = handler;
            Interlocked.Increment(ref _Version);
        }

        /// <summary>
        /// The handler of a key, or null.
        /// </summary>
        /// <param name="key">Key.</param>
        /// <returns>Handler or null.</returns>
        public IJobHandler? Find(string? key)
        {
            if (String.IsNullOrEmpty(key)) return null;
            return _Handlers.TryGetValue(key, out IJobHandler? handler) ? handler : null;
        }

        /// <summary>
        /// Every handler, by key.
        /// </summary>
        /// <returns>Handlers.</returns>
        public List<IJobHandler> All()
        {
            return _Handlers.Values.OrderBy(h => h.Key, StringComparer.Ordinal).ToList();
        }

        #endregion
    }
}
