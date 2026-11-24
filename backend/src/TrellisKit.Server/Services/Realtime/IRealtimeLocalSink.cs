namespace TrellisKit.Server.Services.Realtime
{
    /// <summary>
    /// The in-process fast path into this task's realtime gateway (implemented by <see cref="RealtimeGateway"/>).
    /// <see cref="RealtimeModule.DeliverAsync"/> dispatches locally through it before publishing on the bus, and the
    /// gateway drops the bus echo of an event it already dispatched (by event id).
    /// Thread safety: implementations are safe to call concurrently.
    /// </summary>
    public interface IRealtimeLocalSink
    {
        /// <summary>
        /// Dispatch an event to this task's sockets now (non-blocking: the event is queued on its dispatch lane).
        /// </summary>
        /// <param name="realtimeEvent">Event (its property id is already set).</param>
        void DispatchLocal(RealtimeEvent realtimeEvent);

        /// <summary>
        /// Whether this task holds an authenticated socket of a member.
        /// </summary>
        /// <param name="propertyId">Property id.</param>
        /// <param name="userId">Member.</param>
        /// <returns>True when at least one local socket exists.</returns>
        bool HasLocalConnection(string propertyId, string userId);
    }
}
