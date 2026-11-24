namespace TrellisKit.Core.Database.Interfaces
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Models;

    /// <summary>
    /// Refresh token data access. Implementations are safe to call concurrently.
    /// </summary>
    public interface IRefreshTokenMethods
    {
        /// <summary>
        /// Insert a refresh token.
        /// </summary>
        /// <param name="refreshToken">Token record.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The token.</returns>
        /// <exception cref="ArgumentNullException">An argument is null or empty.</exception>
        /// <exception cref="TrellisKit.Core.Exceptions.DuplicateRecordException">A unique value is already in use.</exception>
        Task<RefreshToken> CreateAsync(RefreshToken refreshToken, CancellationToken token = default);

        /// <summary>
        /// Read a refresh token by hash.
        /// </summary>
        /// <param name="tokenHash">SHA-256 hex of the token.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Token, or null.</returns>
        /// <exception cref="ArgumentNullException">An argument is null or empty.</exception>
        Task<RefreshToken?> ReadByHashAsync(string tokenHash, CancellationToken token = default);

        /// <summary>
        /// Atomically mark an unused token as used. Exactly one concurrent caller succeeds.
        /// </summary>
        /// <param name="id">Token identifier.</param>
        /// <param name="replacedById">Identifier of the replacement token.</param>
        /// <param name="usedUtc">Time (UTC).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True when this call marked it.</returns>
        /// <exception cref="ArgumentNullException">An argument is null or empty.</exception>
        Task<bool> TryMarkUsedAsync(string id, string replacedById, DateTime usedUtc, CancellationToken token = default);

        /// <summary>
        /// Delete tokens that expired before a cutoff.
        /// </summary>
        /// <param name="beforeUtc">Cutoff (UTC).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Rows deleted.</returns>
        Task<int> DeleteExpiredAsync(DateTime beforeUtc, CancellationToken token = default);
    }
}
