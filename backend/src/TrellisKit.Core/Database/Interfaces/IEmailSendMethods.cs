namespace TrellisKit.Core.Database.Interfaces
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Models;

    /// <summary>
    /// Category email idempotency (emailsends) data access: one row per member, category, and source item.
    /// Implementations are safe to call concurrently.
    /// </summary>
    public interface IEmailSendMethods
    {
        /// <summary>
        /// Insert the row unless one exists for the same member, category, and source.
        /// </summary>
        /// <param name="send">Row.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True when this call created it (the caller may send).</returns>
        /// <exception cref="ArgumentNullException">The row is null.</exception>
        Task<bool> TryCreateAsync(EmailSend send, CancellationToken token = default);

        /// <summary>
        /// Change a row's status.
        /// </summary>
        /// <param name="id">Identifier.</param>
        /// <param name="status">Status.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        /// <exception cref="ArgumentNullException">The identifier is null or empty.</exception>
        Task UpdateStatusAsync(string id, EmailSendStatusEnum status, CancellationToken token = default);

        /// <summary>
        /// Whether a Sent email of a category reached the member at or after a time, about a source other than one.
        /// </summary>
        /// <param name="userId">Member.</param>
        /// <param name="category">Category.</param>
        /// <param name="sinceUtc">Start of the window.</param>
        /// <param name="excludeSourceId">Source to ignore.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True when one did.</returns>
        /// <exception cref="ArgumentNullException">The member is null or empty.</exception>
        Task<bool> SentSinceAsync(string userId, NotificationCategoryEnum category, DateTime sinceUtc, string excludeSourceId, CancellationToken token = default);

        /// <summary>
        /// Read the row of a member, category, and source.
        /// </summary>
        /// <param name="userId">Member.</param>
        /// <param name="category">Category.</param>
        /// <param name="sourceId">Source.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Row, or null.</returns>
        /// <exception cref="ArgumentNullException">An argument is null or empty.</exception>
        Task<EmailSend?> ReadAsync(string userId, NotificationCategoryEnum category, string sourceId, CancellationToken token = default);

        /// <summary>
        /// Delete a member's rows (account deletion).
        /// </summary>
        /// <param name="userId">Member.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Rows deleted.</returns>
        /// <exception cref="ArgumentNullException">The member is null or empty.</exception>
        Task<int> DeleteByUserAsync(string userId, CancellationToken token = default);
    }
}
