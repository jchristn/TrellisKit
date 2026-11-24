namespace TrellisKit.Core.Database.Interfaces
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core.Enumeration;
    using TrellisKit.Core.Models;
    using TrellisKit.Core.Responses;

    /// <summary>
    /// Deployment-level platform administrator accounts and their sign-in sessions (separate from every property's
    /// member accounts).
    /// </summary>
    public interface IPlatformAdminMethods
    {
        /// <summary>
        /// Create a platform administrator.
        /// </summary>
        /// <param name="admin">Administrator.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        /// <exception cref="Exceptions.DuplicateRecordException">An active administrator has that email.</exception>
        Task CreateAsync(PlatformAdmin admin, CancellationToken token = default);

        /// <summary>
        /// Read an administrator by id (including removed ones).
        /// </summary>
        /// <param name="id">Id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Administrator, or null.</returns>
        Task<PlatformAdmin?> ReadAsync(string id, CancellationToken token = default);

        /// <summary>
        /// Read an active (not removed) administrator by email, case-insensitively.
        /// </summary>
        /// <param name="email">Email.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Administrator, or null.</returns>
        Task<PlatformAdmin?> ReadByEmailAsync(string email, CancellationToken token = default);

        /// <summary>
        /// Read an active administrator by the SHA-256 of a pending invitation token.
        /// </summary>
        /// <param name="inviteTokenHash">Token hash.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Administrator, or null.</returns>
        Task<PlatformAdmin?> ReadByInviteTokenHashAsync(string inviteTokenHash, CancellationToken token = default);

        /// <summary>
        /// Update an administrator.
        /// </summary>
        /// <param name="admin">Administrator.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        Task UpdateAsync(PlatformAdmin admin, CancellationToken token = default);

        /// <summary>
        /// Enumerate active administrators.
        /// </summary>
        /// <param name="request">Normalized enumeration request.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Page.</returns>
        Task<EnumerationResult<PlatformAdmin>> EnumerateAsync(EnumerationRequest request, CancellationToken token = default);

        /// <summary>
        /// Count active administrators.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Count.</returns>
        Task<long> CountActiveAsync(CancellationToken token = default);

        /// <summary>
        /// Create a sign-in session.
        /// </summary>
        /// <param name="session">Session.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        Task CreateSessionAsync(PlatformSession session, CancellationToken token = default);

        /// <summary>
        /// Read an active (not revoked, not expired) session by token hash.
        /// </summary>
        /// <param name="tokenHash">SHA-256 (hex) of the bearer token.</param>
        /// <param name="nowUtc">Now.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Session, or null.</returns>
        Task<PlatformSession?> ReadActiveSessionAsync(string tokenHash, DateTime nowUtc, CancellationToken token = default);

        /// <summary>
        /// Record session use.
        /// </summary>
        /// <param name="sessionId">Session id.</param>
        /// <param name="usedUtc">Use time.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        Task TouchSessionAsync(string sessionId, DateTime usedUtc, CancellationToken token = default);

        /// <summary>
        /// Revoke one session.
        /// </summary>
        /// <param name="sessionId">Session id.</param>
        /// <param name="revokedUtc">Revocation time.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        Task RevokeSessionAsync(string sessionId, DateTime revokedUtc, CancellationToken token = default);

        /// <summary>
        /// Revoke every session of an administrator.
        /// </summary>
        /// <param name="adminId">Administrator id.</param>
        /// <param name="revokedUtc">Revocation time.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        Task RevokeSessionsForAdminAsync(string adminId, DateTime revokedUtc, CancellationToken token = default);

        /// <summary>
        /// Delete sessions that expired before a time.
        /// </summary>
        /// <param name="olderThanUtc">Cutoff.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Rows deleted.</returns>
        Task<int> PruneSessionsAsync(DateTime olderThanUtc, CancellationToken token = default);
    }
}
