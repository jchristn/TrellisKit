namespace TrellisKit.Core.Database.Interfaces
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core.Models;

    /// <summary>
    /// Per-user posting permission data access. Implementations are safe to call concurrently.
    /// </summary>
    public interface IUserPermissionMethods
    {
        /// <summary>
        /// Read a member's permissions. A member without a stored row gets the defaults (everything granted).
        /// </summary>
        /// <param name="userId">Member identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Permissions; never null.</returns>
        /// <exception cref="ArgumentNullException">An argument is null or empty.</exception>
        Task<UserPermission> ReadAsync(string userId, CancellationToken token = default);

        /// <summary>
        /// Read several members' permissions. Members without a stored row get the defaults.
        /// </summary>
        /// <param name="userIds">Member identifiers.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Permissions keyed by member identifier, one entry per distinct requested identifier.</returns>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        Task<Dictionary<string, UserPermission>> ReadManyAsync(IEnumerable<string> userIds, CancellationToken token = default);

        /// <summary>
        /// Insert or replace a member's permissions.
        /// </summary>
        /// <param name="permission">Permissions.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The stored permissions.</returns>
        /// <exception cref="ArgumentNullException">An argument is null or empty.</exception>
        Task<UserPermission> UpsertAsync(UserPermission permission, CancellationToken token = default);

        /// <summary>
        /// Delete a member's stored permissions (they return to the defaults).
        /// </summary>
        /// <param name="userId">Member identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        /// <exception cref="ArgumentNullException">An argument is null or empty.</exception>
        Task DeleteAsync(string userId, CancellationToken token = default);
    }
}
