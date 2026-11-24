namespace TrellisKit.Core.Database.Interfaces
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Models;

    /// <summary>
    /// Email verification and password reset token data access. Implementations are safe to call concurrently.
    /// </summary>
    public interface IOneTimeTokenMethods
    {
        /// <summary>
        /// Insert a token.
        /// </summary>
        /// <param name="oneTimeToken">Token record.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The token.</returns>
        /// <exception cref="ArgumentNullException">An argument is null or empty.</exception>
        /// <exception cref="TrellisKit.Core.Exceptions.DuplicateRecordException">A unique value is already in use.</exception>
        Task<OneTimeToken> CreateAsync(OneTimeToken oneTimeToken, CancellationToken token = default);

        /// <summary>
        /// Read a token by hash.
        /// </summary>
        /// <param name="tokenHash">SHA-256 hex of the token.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Token, or null.</returns>
        /// <exception cref="ArgumentNullException">An argument is null or empty.</exception>
        Task<OneTimeToken?> ReadByHashAsync(string tokenHash, CancellationToken token = default);

        /// <summary>
        /// Atomically mark an unused, unexpired token as used. Exactly one concurrent caller succeeds.
        /// </summary>
        /// <param name="id">Token identifier.</param>
        /// <param name="nowUtc">Current time.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True when this call consumed it.</returns>
        /// <exception cref="ArgumentNullException">An argument is null or empty.</exception>
        Task<bool> TryConsumeAsync(string id, DateTime nowUtc, CancellationToken token = default);

        /// <summary>
        /// Mark every unused token of a member and purpose as used.
        /// </summary>
        /// <param name="userId">Member identifier.</param>
        /// <param name="purpose">Purpose.</param>
        /// <param name="nowUtc">Current time.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Rows changed.</returns>
        /// <exception cref="ArgumentNullException">An argument is null or empty.</exception>
        Task<int> InvalidateAsync(string userId, OneTimeTokenPurposeEnum purpose, DateTime nowUtc, CancellationToken token = default);
    }
}
