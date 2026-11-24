namespace TrellisKit.Core.Database.Interfaces
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Models;

    /// <summary>
    /// Password credential data access. Implementations are safe to call concurrently.
    /// </summary>
    public interface ICredentialMethods
    {
        /// <summary>
        /// Read a member's password credential.
        /// </summary>
        /// <param name="userId">Member identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Credential, or null when the member has no password.</returns>
        /// <exception cref="ArgumentNullException">An argument is null or empty.</exception>
        Task<UserCredential?> ReadByUserAsync(string userId, CancellationToken token = default);

        /// <summary>
        /// Insert or replace a member's password credential.
        /// </summary>
        /// <param name="credential">Credential.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The credential.</returns>
        /// <exception cref="ArgumentNullException">An argument is null or empty.</exception>
        Task<UserCredential> UpsertAsync(UserCredential credential, CancellationToken token = default);

        /// <summary>
        /// Delete a member's password credential.
        /// </summary>
        /// <param name="userId">Member identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True when one was deleted.</returns>
        /// <exception cref="ArgumentNullException">An argument is null or empty.</exception>
        Task<bool> DeleteByUserAsync(string userId, CancellationToken token = default);
    }
}
