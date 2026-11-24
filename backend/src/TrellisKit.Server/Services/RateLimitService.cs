namespace TrellisKit.Server.Services
{
    using System;
    using TrellisKit.Core.Exceptions;

    /// <summary>
    /// Phase 2 rate limits (contract section 12) over the shared in-process <see cref="SlidingWindowRateLimiter"/>.
    /// Every rejection is 429 RateLimited with Retry-After. SystemAdmin callers are exempt from member limits.
    /// Thread safety: safe to call concurrently.
    /// </summary>
    public class RateLimitService
    {
        #region Public-Members

        /// <summary>
        /// One minute.
        /// </summary>
        public static readonly TimeSpan Minute = TimeSpan.FromMinutes(1);

        /// <summary>
        /// One hour.
        /// </summary>
        public static readonly TimeSpan Hour = TimeSpan.FromHours(1);

        /// <summary>
        /// One day.
        /// </summary>
        public static readonly TimeSpan Day = TimeSpan.FromDays(1);

        /// <summary>
        /// The underlying limiter.
        /// </summary>
        public SlidingWindowRateLimiter Limiter { get; }

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the service.
        /// </summary>
        /// <param name="limiter">Shared limiter.</param>
        /// <exception cref="ArgumentNullException">The limiter is null.</exception>
        public RateLimitService(SlidingWindowRateLimiter limiter)
        {
            Limiter = limiter ?? throw new ArgumentNullException(nameof(limiter));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Record an attempt for a key or throw 429.
        /// </summary>
        /// <param name="key">Limiter key, for example "comments-min:usr_...".</param>
        /// <param name="limit">Attempts allowed per window.</param>
        /// <param name="window">Window.</param>
        /// <exception cref="ApiErrorException">429 RateLimited with Retry-After.</exception>
        public void Enforce(string key, int limit, TimeSpan window)
        {
            if (!Limiter.TryAcquire(key, limit, window, DateTime.UtcNow, out int retryAfter)) throw ApiErrorException.RateLimited(retryAfter);
        }

        /// <summary>
        /// Record an attempt for a member bucket or throw 429. SystemAdmin is exempt.
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="bucket">Bucket name, for example "feed-posts".</param>
        /// <param name="limit">Attempts allowed per window.</param>
        /// <param name="window">Window.</param>
        /// <exception cref="ArgumentNullException">The actor is null.</exception>
        /// <exception cref="ApiErrorException">429 RateLimited with Retry-After.</exception>
        public void EnforceMember(ContentActor actor, string bucket, int limit, TimeSpan window)
        {
            ArgumentNullException.ThrowIfNull(actor);
            if (actor.IsAnonymous)
            {
                // Anonymous browsing: signed-out readers share a per-IP bucket.
                EnforceIp(actor.SourceIp, bucket, limit, window);
                return;
            }
            if (actor.IsSystemAdmin || String.IsNullOrEmpty(actor.UserId)) return;
            Enforce(bucket + ":" + actor.UserId, limit, window);
        }

        /// <summary>
        /// Check a member bucket without recording an attempt; throws 429 when it is full. SystemAdmin is exempt.
        /// Pair with <see cref="EnforceMember"/> when several buckets must all pass before any is charged.
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="bucket">Bucket name.</param>
        /// <param name="limit">Attempts allowed per window.</param>
        /// <param name="window">Window.</param>
        /// <exception cref="ArgumentNullException">The actor is null.</exception>
        /// <exception cref="ApiErrorException">429 RateLimited with Retry-After.</exception>
        public void CheckMember(ContentActor actor, string bucket, int limit, TimeSpan window)
        {
            ArgumentNullException.ThrowIfNull(actor);
            if (actor.IsSystemAdmin || String.IsNullOrEmpty(actor.UserId)) return;
            string key = bucket + ":" + actor.UserId;
            DateTime now = DateTime.UtcNow;
            if (Limiter.IsLimited(key, limit, window, now)) throw ApiErrorException.RateLimited(Limiter.RetryAfterSeconds(key, limit, window, now));
        }

        /// <summary>
        /// Record an attempt for a source IP bucket (anonymous routes) or throw 429.
        /// </summary>
        /// <param name="sourceIp">Source IP, or null.</param>
        /// <param name="bucket">Bucket name.</param>
        /// <param name="limit">Attempts allowed per window.</param>
        /// <param name="window">Window.</param>
        /// <exception cref="ApiErrorException">429 RateLimited with Retry-After.</exception>
        public void EnforceIp(string? sourceIp, string bucket, int limit, TimeSpan window)
        {
            Enforce(bucket + ":ip:" + (String.IsNullOrEmpty(sourceIp) ? "unknown" : sourceIp), limit, window);
        }

        #endregion
    }
}
