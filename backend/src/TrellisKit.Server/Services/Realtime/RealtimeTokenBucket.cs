namespace TrellisKit.Server.Services.Realtime
{
    using System;

    /// <summary>
    /// Token bucket for inbound frames of one connection (realtime.maxInboundFramesPerSecond with a burst of twice the
    /// rate, docs/PHASE5_CONTRACT.md section 4.12).
    /// Thread safety: safe to use concurrently (internally locked).
    /// </summary>
    public sealed class RealtimeTokenBucket
    {
        #region Private-Members

        private readonly object _Lock = new object();
        private double _Tokens;
        private DateTime _LastUtc;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate a full bucket.
        /// </summary>
        /// <param name="burst">Capacity. Minimum 1.</param>
        public RealtimeTokenBucket(int burst)
        {
            _Tokens = Math.Max(1, burst);
            _LastUtc = DateTime.UtcNow;
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Take one token.
        /// </summary>
        /// <param name="ratePerSecond">Refill rate. Minimum 1.</param>
        /// <param name="burst">Capacity. Minimum 1.</param>
        /// <param name="nowUtc">Now.</param>
        /// <param name="retryAfterMs">Milliseconds until a token is available when refused, else 0.</param>
        /// <returns>True when a token was taken.</returns>
        public bool TryTake(int ratePerSecond, int burst, DateTime nowUtc, out int retryAfterMs)
        {
            int rate = Math.Max(1, ratePerSecond);
            int capacity = Math.Max(1, burst);
            lock (_Lock)
            {
                double elapsed = Math.Max(0, (nowUtc - _LastUtc).TotalSeconds);
                _LastUtc = nowUtc;
                _Tokens = Math.Min(capacity, _Tokens + elapsed * rate);
                if (_Tokens >= 1)
                {
                    _Tokens -= 1;
                    retryAfterMs = 0;
                    return true;
                }
                retryAfterMs = (int)Math.Ceiling((1 - _Tokens) * 1000.0 / rate);
                return false;
            }
        }

        #endregion
    }
}
