namespace TrellisKit.Server.Services.Realtime
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// Carries realtime events between server tasks (docs/PHASE5_CONTRACT.md section 4.11). The default implementation
    /// is PostgreSQL LISTEN and NOTIFY; <see cref="InProcessRealtimeBus"/> serves a single task and tests.
    /// Implementations are deployment-level (one per server task, shared by every property) and thread safe.
    /// </summary>
    public interface IRealtimeBus : IAsyncDisposable
    {
        /// <summary>
        /// Implementation label for logs and health, for example "Postgres" or "InProcess".
        /// </summary>
        string Mode { get; }

        /// <summary>
        /// Raised on the receiving task for every event (including events this task published). Handlers must not block.
        /// </summary>
        event Func<RealtimeEvent, Task>? EventReceived;

        /// <summary>
        /// Raised after the bus reconnected and notifications may have been missed (clients must resync).
        /// </summary>
        event Func<Task>? Reconnected;

        /// <summary>
        /// The SQL statement that publishes an event when its transaction commits (PostgreSQL pg_notify), to append to a
        /// write's statement batch so the event is delivered only if the write commits. Null when the implementation
        /// cannot publish transactionally; callers then call <see cref="PublishAsync"/> after the write.
        /// </summary>
        /// <param name="realtimeEvent">Event.</param>
        /// <returns>Statement, or null.</returns>
        /// <exception cref="ArgumentNullException">The event is null.</exception>
        string? NotifyStatement(RealtimeEvent realtimeEvent);

        /// <summary>
        /// Publish an event now (outside a write transaction).
        /// </summary>
        /// <param name="realtimeEvent">Event.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        /// <exception cref="ArgumentNullException">The event is null.</exception>
        Task PublishAsync(RealtimeEvent realtimeEvent, CancellationToken token = default);

        /// <summary>
        /// Start listening (idempotent).
        /// </summary>
        /// <param name="token">Lifetime token.</param>
        /// <returns>Task.</returns>
        Task StartAsync(CancellationToken token = default);
    }
}
