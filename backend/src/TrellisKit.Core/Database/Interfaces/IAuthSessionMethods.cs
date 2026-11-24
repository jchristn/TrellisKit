namespace TrellisKit.Core.Database.Interfaces
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core.Enumeration;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Models;
    using TrellisKit.Core.Responses;

    /// <summary>
    /// Sign-in session data access. Implementations are safe to call concurrently.
    /// </summary>
    public interface IAuthSessionMethods
    {
        /// <summary>
        /// Insert a session.
        /// </summary>
        /// <param name="session">Session.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The session.</returns>
        /// <exception cref="ArgumentNullException">An argument is null or empty.</exception>
        Task<AuthSession> CreateAsync(AuthSession session, CancellationToken token = default);

        /// <summary>
        /// Read a session.
        /// </summary>
        /// <param name="id">Session identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Session, or null.</returns>
        /// <exception cref="ArgumentNullException">An argument is null or empty.</exception>
        Task<AuthSession?> ReadAsync(string id, CancellationToken token = default);

        /// <summary>
        /// List a member's active sessions, newest first.
        /// </summary>
        /// <param name="userId">Member identifier.</param>
        /// <param name="nowUtc">Current time.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Sessions.</returns>
        /// <exception cref="ArgumentNullException">An argument is null or empty.</exception>
        Task<List<AuthSession>> EnumerateActiveByUserAsync(string userId, DateTime nowUtc, CancellationToken token = default);

        /// <summary>
        /// One keyset batch of a member's sessions (active, expired, and revoked), ordered by id (identifiers are K-sortable), for streaming exports.
        /// </summary>
        /// <param name="userId">Member identifier.</param>
        /// <param name="afterId">Return records with an id after this one, or null for the first batch.</param>
        /// <param name="limit">Batch size, 1 to 1000.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Up to limit records.</returns>
        /// <exception cref="ArgumentNullException">The member identifier is null or empty.</exception>
        Task<List<AuthSession>> EnumerateByUserAfterAsync(string userId, string? afterId, int limit, CancellationToken token = default);

        /// <summary>
        /// Record session use.
        /// </summary>
        /// <param name="id">Session identifier.</param>
        /// <param name="usedUtc">Time (UTC).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        /// <exception cref="ArgumentNullException">An argument is null or empty.</exception>
        Task TouchAsync(string id, DateTime usedUtc, CancellationToken token = default);

        /// <summary>
        /// Revoke a session if it is not already revoked.
        /// </summary>
        /// <param name="id">Session identifier.</param>
        /// <param name="reason">Reason.</param>
        /// <param name="revokedUtc">Time (UTC).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True when it was revoked by this call.</returns>
        /// <exception cref="ArgumentNullException">An argument is null or empty.</exception>
        Task<bool> RevokeAsync(string id, string reason, DateTime revokedUtc, CancellationToken token = default);

        /// <summary>
        /// Revoke every active session of a member.
        /// </summary>
        /// <param name="userId">Member identifier.</param>
        /// <param name="exceptSessionId">Session to keep, or null to revoke all.</param>
        /// <param name="reason">Reason.</param>
        /// <param name="revokedUtc">Time (UTC).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Identifiers of the revoked sessions.</returns>
        /// <exception cref="ArgumentNullException">An argument is null or empty.</exception>
        Task<List<string>> RevokeAllForUserAsync(string userId, string? exceptSessionId, string reason, DateTime revokedUtc, CancellationToken token = default);

        /// <summary>
        /// Enumerate one member's sessions (collection <see cref="PhaseOneCollections.Sessions"/>).
        /// </summary>
        /// <param name="userId">Member.</param>
        /// <param name="request">Normalized enumeration.</param>
        /// <param name="includeInactive">Whether revoked and expired sessions are included.</param>
        /// <param name="nowUtc">Current time.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Page.</returns>
        /// <exception cref="ArgumentNullException">An argument is null or empty.</exception>
        Task<EnumerationResult<AuthSession>> EnumerateByUserAsync(string userId, EnumerationRequest request, bool includeInactive, DateTime nowUtc, CancellationToken token = default);
    }
}
