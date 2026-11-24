namespace TrellisKit.Server.Services.Realtime
{
    using System;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// What an inbound frame handler receives (the messaging seam for typing, ack, read, and future client frame types):
    /// the property-bound services, the connection, the member, the client frame id, and the frame's data. The gateway
    /// has already authenticated the connection, applied the inbound rate limits and the frame size cap, checked the
    /// handler's required capability, and (for typing Start) the per-conversation throttle.
    /// Thread safety: one instance per frame; not shared.
    /// </summary>
    public sealed class RealtimeInboundContext
    {
        #region Public-Members

        /// <summary>
        /// Property-bound services of the connection's property.
        /// </summary>
        public ApplicationServices Services { get; }

        /// <summary>
        /// The connection.
        /// </summary>
        public IRealtimeConnectionView Connection { get; }

        /// <summary>
        /// Member (same as Connection.UserId).
        /// </summary>
        public string UserId
        {
            get { return Connection.UserId; }
        }

        /// <summary>
        /// Frame type.
        /// </summary>
        public string Type { get; }

        /// <summary>
        /// Client frame id, or null.
        /// </summary>
        public string? FrameId { get; }

        /// <summary>
        /// The frame's data object (JsonValueKind.Undefined when absent). Valid only during the handler call.
        /// </summary>
        public JsonElement Data { get; }

        #endregion

        #region Private-Members

        private readonly Func<string, object?, Task> _Send;
        private readonly Func<string, string, string?, Task> _Error;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the context (the gateway does this).
        /// </summary>
        /// <param name="services">Services.</param>
        /// <param name="connection">Connection.</param>
        /// <param name="type">Frame type.</param>
        /// <param name="frameId">Client frame id, or null.</param>
        /// <param name="data">Frame data.</param>
        /// <param name="send">Queues a frame (type, data) on this connection.</param>
        /// <param name="error">Queues an error frame (code, message, frame id) on this connection.</param>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        public RealtimeInboundContext(ApplicationServices services, IRealtimeConnectionView connection, string type, string? frameId, JsonElement data, Func<string, object?, Task> send, Func<string, string, string?, Task> error)
        {
            Services = services ?? throw new ArgumentNullException(nameof(services));
            Connection = connection ?? throw new ArgumentNullException(nameof(connection));
            Type = type ?? throw new ArgumentNullException(nameof(type));
            FrameId = frameId;
            Data = data;
            _Send = send ?? throw new ArgumentNullException(nameof(send));
            _Error = error ?? throw new ArgumentNullException(nameof(error));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Queue a server frame on this connection (for example an ack reply).
        /// </summary>
        /// <param name="type">Frame type.</param>
        /// <param name="data">Data object.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        /// <exception cref="ArgumentNullException">The type is null or empty.</exception>
        public Task SendAsync(string type, object? data, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(type)) throw new ArgumentNullException(nameof(type));
            token.ThrowIfCancellationRequested();
            return _Send(type, data);
        }

        /// <summary>
        /// Queue an error frame echoing this frame's id; the connection stays open.
        /// </summary>
        /// <param name="code">Error code, for example ValidationFailed.</param>
        /// <param name="message">Message.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        /// <exception cref="ArgumentNullException">The code is null or empty.</exception>
        public Task ErrorAsync(string code, string message, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(code)) throw new ArgumentNullException(nameof(code));
            token.ThrowIfCancellationRequested();
            return _Error(code, message ?? String.Empty, FrameId);
        }

        /// <summary>
        /// A string property of the data object, or null.
        /// </summary>
        /// <param name="name">Property name.</param>
        /// <returns>Value, or null.</returns>
        public string? DataString(string name)
        {
            if (Data.ValueKind != JsonValueKind.Object) return null;
            return Data.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
        }

        /// <summary>
        /// An integer property of the data object, or null.
        /// </summary>
        /// <param name="name">Property name.</param>
        /// <returns>Value, or null.</returns>
        public long? DataLong(string name)
        {
            if (Data.ValueKind != JsonValueKind.Object) return null;
            return Data.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out long number) ? number : null;
        }

        #endregion
    }
}
