namespace TrellisKit.Server.Services.Realtime
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// A realtime bus that delivers events only within this process (realtime.bus InProcess): single task deployments,
    /// tests, and local development without a PostgreSQL listener. Events are delivered asynchronously after
    /// <see cref="PublishAsync"/> returns; there is no transactional statement, so writers publish after commit.
    /// Thread safety: safe to use concurrently.
    /// </summary>
    public sealed class InProcessRealtimeBus : IRealtimeBus
    {
        #region Public-Members

        /// <inheritdoc />
        public string Mode
        {
            get { return "InProcess"; }
        }

        /// <inheritdoc />
        public event Func<RealtimeEvent, Task>? EventReceived;

        /// <inheritdoc />
        public event Func<Task>? Reconnected
        {
            add { }
            remove { }
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public string? NotifyStatement(RealtimeEvent realtimeEvent)
        {
            ArgumentNullException.ThrowIfNull(realtimeEvent);
            return null;
        }

        /// <inheritdoc />
        public Task PublishAsync(RealtimeEvent realtimeEvent, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(realtimeEvent);
            Func<RealtimeEvent, Task>? handlers = EventReceived;
            if (handlers == null) return Task.CompletedTask;
            _ = Task.Run(async () =>
            {
                foreach (Func<RealtimeEvent, Task> handler in handlers.GetInvocationList())
                {
                    try
                    {
                        await handler(realtimeEvent).ConfigureAwait(false);
                    }
                    catch (Exception)
                    {
                        // A failing receiver never affects the publisher or the other receivers.
                    }
                }
            }, CancellationToken.None);
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public Task StartAsync(CancellationToken token = default)
        {
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public ValueTask DisposeAsync()
        {
            EventReceived = null;
            return ValueTask.CompletedTask;
        }

        #endregion
    }
}
