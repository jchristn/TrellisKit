namespace TrellisKit.Server.Services.Ai
{
    using System.Threading.Tasks;

    /// <summary>
    /// Receives the chat events of section 10.2 (an SSE stream, or nothing for JSON replies). Implementations never
    /// throw when the client went away: generation continues and the result is stored.
    /// </summary>
    public interface IChatEventSink
    {
        /// <summary>
        /// Emit one event.
        /// </summary>
        /// <param name="name">Event name, for example reply.delta.</param>
        /// <param name="data">Event data (serialized as one-line JSON).</param>
        /// <returns>Task.</returns>
        Task EmitAsync(string name, object data);
    }
}
