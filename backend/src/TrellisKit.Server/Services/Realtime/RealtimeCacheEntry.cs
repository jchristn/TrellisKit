namespace TrellisKit.Server.Services.Realtime
{
    using System;
    using System.Threading.Tasks;

    /// <summary>
    /// One entry of <see cref="RealtimeRenderCache"/>: a pending or completed load and its expiry.
    /// Thread safety: immutable.
    /// </summary>
    public sealed class RealtimeCacheEntry
    {
        #region Public-Members

        /// <summary>
        /// The load.
        /// </summary>
        public Task<object?> Value { get; }

        /// <summary>
        /// UTC expiry.
        /// </summary>
        public DateTime ExpiresUtc { get; }

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate an entry.
        /// </summary>
        /// <param name="value">Load.</param>
        /// <param name="expiresUtc">Expiry.</param>
        /// <exception cref="ArgumentNullException">The load is null.</exception>
        public RealtimeCacheEntry(Task<object?> value, DateTime expiresUtc)
        {
            Value = value ?? throw new ArgumentNullException(nameof(value));
            ExpiresUtc = expiresUtc;
        }

        #endregion
    }
}
