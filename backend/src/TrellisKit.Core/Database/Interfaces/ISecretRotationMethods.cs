namespace TrellisKit.Core.Database.Interfaces
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core.Models;
    using TrellisKit.Core.Security;

    /// <summary>
    /// Encrypted values of one property for secrets key rotation (propertysettings ciphertext and emailmessages
    /// payloads). Implementations are safe to call concurrently.
    /// </summary>
    public interface ISecretRotationMethods
    {
        /// <summary>
        /// Count the values encrypted with any of the given key ids.
        /// </summary>
        /// <param name="keyIds">Key ids.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Count (0 when no key id is given).</returns>
        /// <exception cref="ArgumentNullException">The key ids are null.</exception>
        Task<long> CountAsync(IEnumerable<string> keyIds, CancellationToken token = default);

        /// <summary>
        /// Read a keyset page of values encrypted with any of the given key ids, ordered by store and row key.
        /// </summary>
        /// <param name="keyIds">Key ids.</param>
        /// <param name="afterStore">Keyset position: store of the last value read, or null for the first page.</param>
        /// <param name="afterRowKey">Keyset position: row key of the last value read, or null.</param>
        /// <param name="limit">Maximum values. Minimum 1.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Values.</returns>
        /// <exception cref="ArgumentNullException">The key ids are null.</exception>
        Task<List<StoredSecret>> ReadBatchAsync(IEnumerable<string> keyIds, string? afterStore, string? afterRowKey, int limit, CancellationToken token = default);

        /// <summary>
        /// Replace a value's ciphertext only while it still holds the ciphertext that was read (idempotent and safe
        /// against a concurrent write or another instance's pass).
        /// </summary>
        /// <param name="current">The value as read.</param>
        /// <param name="replacement">New ciphertext, nonce, and key id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True when the row was rewritten.</returns>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        Task<bool> RewriteAsync(StoredSecret current, SecretCiphertext replacement, CancellationToken token = default);
    }
}
