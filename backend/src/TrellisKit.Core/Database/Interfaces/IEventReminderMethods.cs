namespace TrellisKit.Core.Database.Interfaces
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core.Models;

    /// <summary>
    /// Event reminders (docs/PHASE4_CONTRACT.md section 4.4). Every statement is property scoped.
    /// </summary>
    public interface IEventReminderMethods
    {
        /// <summary>
        /// The reminders of an event, largest offset first.
        /// </summary>
        /// <param name="eventId">Event.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Reminders.</returns>
        Task<List<EventReminder>> ListForEventAsync(string eventId, CancellationToken token = default);

        /// <summary>
        /// Unsent, unskipped reminders of undeleted events that are due, oldest first.
        /// </summary>
        /// <param name="nowUtc">Now.</param>
        /// <param name="limit">Most rows.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Reminders.</returns>
        Task<List<EventReminder>> ListDueAsync(DateTime nowUtc, int limit, CancellationToken token = default);

        /// <summary>
        /// Mark a reminder sent (conditional: only when it is still unsent and unskipped).
        /// </summary>
        /// <param name="id">Reminder.</param>
        /// <param name="nowUtc">Now.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True when this call marked it.</returns>
        Task<bool> MarkSentAsync(string id, DateTime nowUtc, CancellationToken token = default);

        /// <summary>
        /// Mark a reminder skipped (conditional: only when it is still unsent and unskipped).
        /// </summary>
        /// <param name="id">Reminder.</param>
        /// <param name="nowUtc">Now.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True when this call marked it.</returns>
        Task<bool> MarkSkippedAsync(string id, DateTime nowUtc, CancellationToken token = default);

        /// <summary>
        /// Skip every unsent reminder of an event (cancellation stops reminders).
        /// </summary>
        /// <param name="eventId">Event.</param>
        /// <param name="nowUtc">Now.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Reminders skipped.</returns>
        Task<int> SkipPendingAsync(string eventId, DateTime nowUtc, CancellationToken token = default);
    }
}
