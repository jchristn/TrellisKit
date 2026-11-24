namespace TrellisKit.Core.Database.Interfaces
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core.Enumeration;
    using TrellisKit.Core.Models;
    using TrellisKit.Core.Responses;

    /// <summary>
    /// Offline download licenses (docs/PHASE4_CONTRACT.md section 5.9).
    /// </summary>
    public interface ILessonDownloadMethods
    {
        /// <summary>
        /// Issue a license, or renew the active license of the same member, device, and lesson.
        /// </summary>
        /// <param name="download">License.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The stored license.</returns>
        Task<LessonDownload> IssueAsync(LessonDownload download, CancellationToken token = default);

        /// <summary>
        /// Read a license, or null.
        /// </summary>
        /// <param name="id">Identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>License or null.</returns>
        Task<LessonDownload?> ReadAsync(string id, CancellationToken token = default);

        /// <summary>
        /// Extend an active license.
        /// </summary>
        /// <param name="id">License.</param>
        /// <param name="expiresUtc">New expiry.</param>
        /// <param name="nowUtc">Now.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The stored license, or null when it is not active.</returns>
        Task<LessonDownload?> RenewAsync(string id, DateTime expiresUtc, DateTime nowUtc, CancellationToken token = default);

        /// <summary>
        /// Revoke a license.
        /// </summary>
        /// <param name="id">License.</param>
        /// <param name="reason">Reason.</param>
        /// <param name="nowUtc">Now.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True when revoked now.</returns>
        Task<bool> RevokeAsync(string id, string reason, DateTime nowUtc, CancellationToken token = default);

        /// <summary>
        /// Active (not revoked, not expired) licenses of a member.
        /// </summary>
        /// <param name="userId">Member.</param>
        /// <param name="nowUtc">Now.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Count.</returns>
        Task<long> CountActiveAsync(string userId, DateTime nowUtc, CancellationToken token = default);

        /// <summary>
        /// Distinct devices with active licenses of a member.
        /// </summary>
        /// <param name="userId">Member.</param>
        /// <param name="nowUtc">Now.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Device identifiers.</returns>
        Task<List<string>> ActiveDevicesAsync(string userId, DateTime nowUtc, CancellationToken token = default);

        /// <summary>
        /// Enumerate a member's licenses.
        /// </summary>
        /// <param name="request">Normalized request.</param>
        /// <param name="userId">Member.</param>
        /// <param name="activeOnly">Only active licenses.</param>
        /// <param name="deviceId">Device, or null.</param>
        /// <param name="nowUtc">Now.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Page.</returns>
        Task<EnumerationResult<LessonDownload>> EnumerateAsync(EnumerationRequest request, string userId, bool activeOnly, string? deviceId, DateTime nowUtc, CancellationToken token = default);

        /// <summary>
        /// Revoke every active license of a member (ban) or of a lesson (unpublish, archive, delete).
        /// </summary>
        /// <param name="userId">Member, or null.</param>
        /// <param name="lessonId">Lesson, or null.</param>
        /// <param name="reason">Reason.</param>
        /// <param name="nowUtc">Now.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Licenses revoked.</returns>
        Task<int> RevokeAllAsync(string? userId, string? lessonId, string reason, DateTime nowUtc, CancellationToken token = default);

        /// <summary>
        /// Mark expired licenses revoked (reason expired) and delete revoked rows older than the cutoff.
        /// </summary>
        /// <param name="nowUtc">Now.</param>
        /// <param name="deleteBeforeUtc">Delete revoked rows older than this.</param>
        /// <param name="limit">Maximum rows per step. Minimum 1.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The platforms of the licenses that expired in this pass (one entry per license).</returns>
        Task<List<string>> PruneAsync(DateTime nowUtc, DateTime deleteBeforeUtc, int limit, CancellationToken token = default);

        /// <summary>
        /// A member's licenses after a cursor (export).
        /// </summary>
        /// <param name="userId">Member.</param>
        /// <param name="afterId">Cursor, or null.</param>
        /// <param name="limit">Maximum rows. Minimum 1.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Licenses.</returns>
        Task<List<LessonDownload>> ListForUserAsync(string userId, string? afterId, int limit, CancellationToken token = default);

        /// <summary>
        /// Delete a member's licenses (account deletion).
        /// </summary>
        /// <param name="userId">Member.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Rows deleted.</returns>
        Task<int> DeleteForUserAsync(string userId, CancellationToken token = default);
    }
}
