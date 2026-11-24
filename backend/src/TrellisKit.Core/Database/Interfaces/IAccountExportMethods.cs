namespace TrellisKit.Core.Database.Interfaces
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core.Exceptions;
    using TrellisKit.Core.Models;

    /// <summary>
    /// Asynchronous account export jobs (table accountexports). A partial unique index allows one Pending or Running
    /// export per member, and workers claim jobs with a conditional update, so several server instances never write the
    /// same export. Implementations are safe to call concurrently.
    /// </summary>
    public interface IAccountExportMethods
    {
        /// <summary>
        /// Insert a Pending export.
        /// </summary>
        /// <param name="export">Export.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The export.</returns>
        /// <exception cref="ArgumentNullException">The export is null.</exception>
        /// <exception cref="DuplicateRecordException">The member already has a Pending or Running export.</exception>
        Task<AccountExport> CreateAsync(AccountExport export, CancellationToken token = default);

        /// <summary>
        /// Read an export.
        /// </summary>
        /// <param name="id">Identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The export, or null.</returns>
        /// <exception cref="ArgumentNullException">The identifier is null or empty.</exception>
        Task<AccountExport?> ReadAsync(string id, CancellationToken token = default);

        /// <summary>
        /// Claim up to limit Pending exports (and Running exports whose claim lapsed), marking them Running.
        /// </summary>
        /// <param name="holder">Worker instance identifier.</param>
        /// <param name="nowUtc">Current time.</param>
        /// <param name="claimUntilUtc">When the claim lapses.</param>
        /// <param name="limit">Most exports to claim. Minimum 1.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Claimed exports.</returns>
        /// <exception cref="ArgumentNullException">The holder is null or empty.</exception>
        Task<List<AccountExport>> ClaimAsync(string holder, DateTime nowUtc, DateTime claimUntilUtc, int limit, CancellationToken token = default);

        /// <summary>
        /// Mark a claimed export Ready.
        /// </summary>
        /// <param name="id">Identifier.</param>
        /// <param name="holder">Worker instance holding the claim.</param>
        /// <param name="sizeBytes">File size.</param>
        /// <param name="completedUtc">Completion time.</param>
        /// <param name="expiresUtc">When the file is deleted.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True when the holder still held the claim.</returns>
        /// <exception cref="ArgumentNullException">An argument is null or empty.</exception>
        Task<bool> CompleteAsync(string id, string holder, long sizeBytes, DateTime completedUtc, DateTime expiresUtc, CancellationToken token = default);

        /// <summary>
        /// Mark a claimed export Failed.
        /// </summary>
        /// <param name="id">Identifier.</param>
        /// <param name="holder">Worker instance holding the claim.</param>
        /// <param name="error">Failure description.</param>
        /// <param name="completedUtc">Failure time.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        /// <exception cref="ArgumentNullException">An argument is null or empty.</exception>
        Task FailAsync(string id, string holder, string error, DateTime completedUtc, CancellationToken token = default);

        /// <summary>
        /// Ready exports whose retention ended, oldest first.
        /// </summary>
        /// <param name="nowUtc">Current time.</param>
        /// <param name="limit">Most exports to return. Minimum 1.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Exports.</returns>
        Task<List<AccountExport>> EnumerateExpiredAsync(DateTime nowUtc, int limit, CancellationToken token = default);

        /// <summary>
        /// Mark a Ready export Expired (its file was deleted).
        /// </summary>
        /// <param name="id">Identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True when the export was Ready.</returns>
        /// <exception cref="ArgumentNullException">The identifier is null or empty.</exception>
        Task<bool> MarkExpiredAsync(string id, CancellationToken token = default);

        /// <summary>
        /// The member's Pending or Running export, or null.
        /// </summary>
        /// <param name="userId">Member identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Export, or null.</returns>
        /// <exception cref="ArgumentNullException">The member identifier is null or empty.</exception>
        Task<AccountExport?> ReadActiveByUserAsync(string userId, CancellationToken token = default);

        /// <summary>
        /// Delete every export of a member (account deletion).
        /// </summary>
        /// <param name="userId">Member identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Identifiers of the deleted exports.</returns>
        /// <exception cref="ArgumentNullException">The member identifier is null or empty.</exception>
        Task<List<string>> DeleteByUserAsync(string userId, CancellationToken token = default);
    }
}
