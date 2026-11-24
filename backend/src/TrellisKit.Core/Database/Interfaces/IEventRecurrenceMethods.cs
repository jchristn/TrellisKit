namespace TrellisKit.Core.Database.Interfaces
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core.Models;

    /// <summary>
    /// Recurrence rules, their weekdays, and their exceptions (resolved decision 11). Every statement is property scoped.
    /// </summary>
    public interface IEventRecurrenceMethods
    {
        /// <summary>
        /// Insert a recurrence with its weekdays.
        /// </summary>
        /// <param name="recurrence">Recurrence.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        Task CreateAsync(EventRecurrence recurrence, CancellationToken token = default);

        /// <summary>
        /// Read an undeleted recurrence, or null.
        /// </summary>
        /// <param name="id">Identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Recurrence or null.</returns>
        Task<EventRecurrence?> ReadAsync(string id, CancellationToken token = default);

        /// <summary>
        /// Read undeleted recurrences by id.
        /// </summary>
        /// <param name="ids">Identifiers.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Recurrences by id.</returns>
        Task<Dictionary<string, EventRecurrence>> ReadManyAsync(IEnumerable<string> ids, CancellationToken token = default);

        /// <summary>
        /// Write a recurrence's rule and state, replacing its weekdays.
        /// </summary>
        /// <param name="recurrence">Recurrence.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        Task UpdateAsync(EventRecurrence recurrence, CancellationToken token = default);

        /// <summary>
        /// Soft delete a recurrence.
        /// </summary>
        /// <param name="id">Identifier.</param>
        /// <param name="nowUtc">Now.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        Task DeleteAsync(string id, DateTime nowUtc, CancellationToken token = default);

        /// <summary>
        /// Recurrences whose materialized horizon is before a time (the recurrence worker), oldest first.
        /// </summary>
        /// <param name="beforeUtc">Horizon target.</param>
        /// <param name="limit">Most rows.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Recurrences.</returns>
        Task<List<EventRecurrence>> ListBehindHorizonAsync(DateTime beforeUtc, int limit, CancellationToken token = default);

        /// <summary>
        /// The exceptions of a recurrence.
        /// </summary>
        /// <param name="recurrenceId">Recurrence.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Exceptions by original start.</returns>
        Task<List<EventRecurrenceException>> ListExceptionsAsync(string recurrenceId, CancellationToken token = default);

        /// <summary>
        /// Insert or replace the exception of one original start.
        /// </summary>
        /// <param name="exception">Exception.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        Task UpsertExceptionAsync(EventRecurrenceException exception, CancellationToken token = default);

        /// <summary>
        /// Move occurrences (and their exceptions) at or after an original start from one recurrence to another (a
        /// Following split).
        /// </summary>
        /// <param name="fromRecurrenceId">Old recurrence.</param>
        /// <param name="toRecurrenceId">New recurrence.</param>
        /// <param name="eventIds">Occurrences to move.</param>
        /// <param name="sinceOriginalUtc">Exceptions at or after this original start move too.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        Task MoveAsync(string fromRecurrenceId, string toRecurrenceId, IEnumerable<string> eventIds, DateTime sinceOriginalUtc, CancellationToken token = default);
    }
}
