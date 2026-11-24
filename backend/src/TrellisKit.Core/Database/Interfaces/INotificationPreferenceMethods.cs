namespace TrellisKit.Core.Database.Interfaces
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Models;

    /// <summary>
    /// Notification preference (notificationpreferences) and notification hours (notificationhours) data access.
    /// Missing rows mean the defaults, which live in code. Implementations are safe to call concurrently.
    /// </summary>
    public interface INotificationPreferenceMethods
    {
        /// <summary>
        /// The stored category rows of several members.
        /// </summary>
        /// <param name="userIds">Members.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Rows (members without rows contribute none).</returns>
        /// <exception cref="ArgumentNullException">The list is null.</exception>
        Task<List<NotificationPreference>> EnumerateAsync(IEnumerable<string> userIds, CancellationToken token = default);

        /// <summary>
        /// Insert or replace category rows in one transaction.
        /// </summary>
        /// <param name="preferences">Rows.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        /// <exception cref="ArgumentNullException">The list is null.</exception>
        Task UpsertAsync(List<NotificationPreference> preferences, CancellationToken token = default);

        /// <summary>
        /// The stored notification hours of several members.
        /// </summary>
        /// <param name="userIds">Members.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Rows (members without a row contribute none).</returns>
        /// <exception cref="ArgumentNullException">The list is null.</exception>
        Task<List<NotificationHours>> EnumerateHoursAsync(IEnumerable<string> userIds, CancellationToken token = default);

        /// <summary>
        /// Insert or replace a member's notification hours.
        /// </summary>
        /// <param name="hours">Row.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        /// <exception cref="ArgumentNullException">The row is null.</exception>
        Task UpsertHoursAsync(NotificationHours hours, CancellationToken token = default);

        /// <summary>
        /// Delete a member's preference and hours rows (account deletion).
        /// </summary>
        /// <param name="userId">Member.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        /// <exception cref="ArgumentNullException">The member is null or empty.</exception>
        Task DeleteByUserAsync(string userId, CancellationToken token = default);

        /// <summary>
        /// Members due for a category email digest: email on for the category (an explicit preference row), a verified
        /// address, not deleted or banned, and no digest check since a time. Oldest check first (never checked first).
        /// </summary>
        /// <param name="categories">Digest categories.</param>
        /// <param name="checkedBeforeUtc">Due when the last check is before this time or missing.</param>
        /// <param name="limit">Most rows (1 to 1000).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Candidates.</returns>
        Task<List<EmailDigestCandidate>> ListDigestsDueAsync(IReadOnlyList<NotificationCategoryEnum> categories, DateTime checkedBeforeUtc, int limit, CancellationToken token = default);

        /// <summary>
        /// Record a digest check (and, when one was sent, the send time) for a member and category.
        /// </summary>
        /// <param name="userId">Member.</param>
        /// <param name="category">Category.</param>
        /// <param name="checkedUtc">Check time.</param>
        /// <param name="sentUtc">Send time, or null when nothing was sent (the previous send time stays).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        Task MarkDigestAsync(string userId, NotificationCategoryEnum category, DateTime checkedUtc, DateTime? sentUtc, CancellationToken token = default);
    }
}
